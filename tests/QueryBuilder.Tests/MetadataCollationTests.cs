using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Repositories;

namespace QueryBuilder.Tests;

/// <summary>Runs only when QB_TEST_METADATA_SQLSERVER names a SQL Server login that can create databases.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MetadataSqlFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "QB_TEST_METADATA_SQLSERVER";

    public MetadataSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
        {
            Skip = $"Set {EnvironmentVariable} to a SQL Server connection string to run this test.";
        }
    }
}

[Collection(QueryBuilder.Tests.Hub.MetadataDatabaseCollection.Name)]
public class MetadataCollationTests
{
    private static async Task WithDatabaseAsync(Func<AppDbContext, IMemoryCache, Guid, Task> body)
    {
        var server = Environment.GetEnvironmentVariable(MetadataSqlFactAttribute.EnvironmentVariable)!;
        var name = $"qb_meta_it_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(server) { InitialCatalog = name };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        try
        {
            await using var db = new AppDbContext(options);
            await db.Database.MigrateAsync();

            var collation = await ScalarAsync(builder.ConnectionString,
                "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'))");
            Assert.Contains("_CI_", collation);

            var dataSource = new DataSource { Name = "ds", ConnectionStringName = "x" };
            db.DataSources.Add(dataSource);
            await db.SaveChangesAsync();

            using var cache = new MemoryCache(new MemoryCacheOptions());
            await body(db, cache, dataSource.Id);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            server = new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString;
            await using var master = new SqlConnection(server);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static DataScopeRule Rule(Guid dataSourceId, string objectName) => new()
    {
        DataSourceId = dataSourceId,
        ObjectName = objectName,
        ScopeKey = "ProgramId",
        ColumnName = "ProgramId",
        CreatedBy = "test",
    };

    [MetadataSqlFact]
    public Task Saving_case_only_pair_returns_validation_error_and_keeps_existing_rules() => WithDatabaseAsync(async (db, cache, dsId) =>
    {
        var repository = new DataScopeRuleRepository(db, cache);
        await repository.ReplaceForDataSourceAsync(dsId, [Rule(dsId, "public.Seeded")], CancellationToken.None);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => repository.ReplaceForDataSourceAsync(
            dsId, [Rule(dsId, "public.Orders"), Rule(dsId, "public.orders")], CancellationToken.None));
        // SQL Server names whichever of the pair it inserted second, and EF's insert order varies with the random ids.
        Assert.Contains("public.orders", ex.Message, StringComparison.OrdinalIgnoreCase);

        db.ChangeTracker.Clear();
        var remaining = await db.DataScopeRules.AsNoTracking().Where(x => x.DataSourceId == dsId).ToListAsync();
        Assert.Equal(["public.Seeded"], remaining.Select(x => x.ObjectName));
    });

    [MetadataSqlFact]
    public Task Mixed_case_rule_round_trips_exactly() => WithDatabaseAsync(async (db, cache, dsId) =>
    {
        var repository = new DataScopeRuleRepository(db, cache);
        await repository.ReplaceForDataSourceAsync(dsId, [Rule(dsId, "public.Orders")], CancellationToken.None);

        db.ChangeTracker.Clear();
        var loaded = await db.DataScopeRules.AsNoTracking().Where(x => x.DataSourceId == dsId).ToListAsync();
        Assert.Equal("public.Orders", Assert.Single(loaded).ObjectName);
    });
}
