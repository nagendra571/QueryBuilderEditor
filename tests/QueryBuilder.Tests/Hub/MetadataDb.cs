using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Tests.Hub;

/// <summary>A throwaway metadata database on the server named by <see cref="MetadataSqlFactAttribute.EnvironmentVariable"/>.</summary>
internal static class MetadataDb
{
    public static async Task WithDatabaseAsync(Func<DbContextOptions<AppDbContext>, string, Task> body, bool migrate = true)
    {
        var server = Environment.GetEnvironmentVariable(MetadataSqlFactAttribute.EnvironmentVariable)!;
        var name = $"qb_meta_it_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        try
        {
            if (migrate)
            {
                await using var db = new AppDbContext(options);
                await db.Database.MigrateAsync();
            }

            await body(options, connectionString);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            var master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString;
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
            await drop.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>Every class that creates or drops throwaway metadata databases shares this collection, so they run one at a time: in parallel they exhausted LocalDB's connections and failed intermittently.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MetadataDatabaseCollection
{
    public const string Name = "MetadataDatabase";
}
