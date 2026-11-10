using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Catalog;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Repositories;
using QueryBuilder.Infrastructure.Security;
using QueryBuilder.Tests.Postgres;

namespace QueryBuilder.Tests;

/// <summary>Connection tests that need no server: nothing listens on port 1.</summary>
public class ConnectionTesterOfflineTests
{
    private static ConnectionTester Tester() =>
        new(new ConnectionStringResolver(
                new ConfigurationBuilder().Build(), new MetadataConnectionStringAccessor("Server=x"),
                DataSourceManagementTests.Protector(), NullLogger<ConnectionStringResolver>.Instance),
            NullLogger<ConnectionTester>.Instance);

    [Theory]
    [InlineData(DataSourceProvider.PostgreSql)]
    [InlineData(DataSourceProvider.SqlServer)]
    public async Task An_unreachable_host_gives_the_short_reason_and_no_password(DataSourceProvider provider)
    {
        var details = new ManagedConnectionDetails("127.0.0.1", 1, "d", "u", "pw-never-shown", ConnectionSslMode.Disable);

        var result = await Tester().TestAsync(provider, details, default);

        Assert.False(result.Ok);
        Assert.Equal("Couldn't reach the server.", result.Message);
        Assert.DoesNotContain("pw-never-shown", result.Message);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_passes_through()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var details = new ManagedConnectionDetails("127.0.0.1", 1, "d", "u", "p", ConnectionSslMode.Disable);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Tester().TestAsync(DataSourceProvider.PostgreSql, details, cts.Token));
    }

    [Fact]
    public async Task A_stored_source_whose_blob_cannot_be_read_says_to_re_enter_it()
    {
        var source = new DataSource
        {
            Name = "m", Provider = DataSourceProvider.PostgreSql, ConnectionStringName = DataSource.ManagedConnectionSentinel
        };
        source.EncryptedConnection = DataSourceManagementTests.Protector(9).Protect(source.Id, "{}");

        var result = await Tester().TestStoredAsync(source, default);

        Assert.False(result.Ok);
        Assert.Equal(ManagedConnections.SecretUnavailableMessage, result.Message);
    }
}

public class DataSourceManagementPostgresTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
{
    private const string Password = "pg-pass-must-not-leak";

    private ManagedConnectionDetails Details(string? databaseName = null, string? password = null)
    {
        var admin = new NpgsqlConnectionStringBuilder(database.AdminConnectionString);
        return new ManagedConnectionDetails(
            admin.Host!, admin.Port, databaseName ?? database.DatabaseName, admin.Username!, password ?? admin.Password ?? Password, ConnectionSslMode.Disable);
    }

    private static ConnectionStringResolver Resolver(IConnectionSecretProtector protector) =>
        new(new ConfigurationBuilder().Build(), new MetadataConnectionStringAccessor("Server=x"), protector, NullLogger<ConnectionStringResolver>.Instance);

    [PostgresFact]
    public async Task A_good_connection_tests_ok()
    {
        var tester = new ConnectionTester(Resolver(DataSourceManagementTests.Protector()), NullLogger<ConnectionTester>.Instance);

        var result = await tester.TestAsync(DataSourceProvider.PostgreSql, Details(), default);

        Assert.True(result.Ok, result.Message);
    }

    [PostgresFact]
    public async Task A_nonexistent_database_fails_with_the_server_message_and_no_password()
    {
        var tester = new ConnectionTester(Resolver(DataSourceManagementTests.Protector()), NullLogger<ConnectionTester>.Instance);

        // The real password, so a server that checks passwords reaches the missing-database error.
        var details = Details("qb_no_such_db_" + Guid.NewGuid().ToString("N"));
        var result = await tester.TestAsync(DataSourceProvider.PostgreSql, details, default);

        Assert.False(result.Ok);
        Assert.Contains("does not exist", result.Message);
        Assert.DoesNotContain(details.Password, result.Message);
    }

    [PostgresFact]
    public async Task A_managed_postgres_source_runs_a_query_end_to_end_through_the_resolver()
    {
        var protector = DataSourceManagementTests.Protector();
        var source = new DataSource
        {
            Name = "managed pg", Provider = DataSourceProvider.PostgreSql, ConnectionStringName = DataSource.ManagedConnectionSentinel,
            CatalogScope = CatalogScope.TablesAndViews
        };
        source.EncryptedConnection = protector.Protect(source.Id, ManagedConnections.Serialize(Details()));
        var resolver = Resolver(protector);

        var rows = await new PostgresQueryExecutionService(resolver, NullLogger<PostgresQueryExecutionService>.Instance).ExecuteAsync(
            source, new GeneratedQuery("SELECT id FROM ex.ops ORDER BY id", []), new Dictionary<string, string?>(), null, default);
        var objects = await new PostgresCatalogReader(NullLogger<PostgresCatalogReader>.Instance)
            .ReadObjectsAsync(resolver.Resolve(source), default);

        Assert.Equal([1L, 2L, 3L, 4L], rows.Rows.Select(r => Convert.ToInt64(r["id"])).ToArray());
        Assert.Contains(objects, o => o.SchemaName == "ex" && o.Name == "ops");

        var tester = new ConnectionTester(resolver, NullLogger<ConnectionTester>.Instance);
        Assert.True((await tester.TestStoredAsync(source, default)).Ok);
    }

    [PostgresFact]
    public async Task A_managed_source_with_a_5000_character_password_connects_and_queries()
    {
        var role = "qb_long_pw_" + Guid.NewGuid().ToString("N")[..8];
        var password = string.Concat(Enumerable.Range(0, 5000).Select(i => "aB3$xé!q'"[i % 9]));
        await using (var admin = new NpgsqlConnection(database.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN PASSWORD '{password.Replace("'", "''")}'", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var protector = DataSourceManagementTests.Protector();
            var source = new DataSource
            {
                Name = "long pw", Provider = DataSourceProvider.PostgreSql, ConnectionStringName = DataSource.ManagedConnectionSentinel
            };
            source.EncryptedConnection = protector.Protect(source.Id, ManagedConnections.Serialize(Details() with { Username = role, Password = password }));
            var resolver = Resolver(protector);

            var rows = await new PostgresQueryExecutionService(resolver, NullLogger<PostgresQueryExecutionService>.Instance).ExecuteAsync(
                source, new GeneratedQuery("SELECT current_user AS who", []), new Dictionary<string, string?>(), null, default);

            Assert.Equal(role, rows.Rows.Single()["who"]?.ToString());
            Assert.True((await new ConnectionTester(resolver, NullLogger<ConnectionTester>.Instance).TestStoredAsync(source, default)).Ok);

            var wrong = await new ConnectionTester(resolver, NullLogger<ConnectionTester>.Instance).TestAsync(
                DataSourceProvider.PostgreSql, Details() with { Username = role, Password = password[..4999] + "Z" }, default);
            // Under trust authentication any password works, so the test would prove nothing.
            Assert.False(wrong.Ok, "The test server accepted a wrong password: trust only qb_it_reader in pg_hba.conf, not every role.");
            Assert.DoesNotContain(password[..64], wrong.Message);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(database.AdminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP ROLE IF EXISTS {role}", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [PostgresFact]
    public async Task A_managed_source_with_the_wrong_key_fails_as_a_query_error_not_a_crash()
    {
        var source = new DataSource
        {
            Name = "managed pg", Provider = DataSourceProvider.PostgreSql, ConnectionStringName = DataSource.ManagedConnectionSentinel
        };
        source.EncryptedConnection = DataSourceManagementTests.Protector(1).Protect(source.Id, ManagedConnections.Serialize(Details()));
        var resolver = Resolver(DataSourceManagementTests.Protector(2));

        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            new PostgresQueryExecutionService(resolver, NullLogger<PostgresQueryExecutionService>.Instance).ExecuteAsync(
                source, new GeneratedQuery("SELECT 1", []), new Dictionary<string, string?>(), null, default));

        Assert.Equal("Couldn't connect to this data source.", ex.Message);
    }
}

public class DataSourceManagementMetadataTests
{
    private static async Task WithDatabaseAsync(Func<DbContextOptions<AppDbContext>, string, Task> body)
    {
        var server = Environment.GetEnvironmentVariable(MetadataSqlFactAttribute.EnvironmentVariable)!;
        var name = $"qb_meta_it_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        try
        {
            await using (var db = new AppDbContext(options))
            {
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

    [MetadataSqlFact]
    public async Task A_managed_row_round_trips_through_the_real_metadata_database()
    {
        var protector = DataSourceManagementTests.Protector();
        var details = new ManagedConnectionDetails("db.example.com", 5432, "sales", "app", "round-trip-secret", ConnectionSslMode.VerifyFull);

        await WithDatabaseAsync(async (options, _) =>
        {
            var source = new DataSource
            {
                Name = "managed", Provider = DataSourceProvider.PostgreSql,
                ConnectionStringName = DataSource.ManagedConnectionSentinel, CreatedBy = "test"
            };
            source.EncryptedConnection = protector.Protect(source.Id, ManagedConnections.Serialize(details));
            var legacy = new DataSource { Name = "legacy", ConnectionStringName = "legacy", CreatedBy = "test" };

            await using (var db = new AppDbContext(options))
            {
                var repository = new DataSourceRepository(db);
                await repository.AddAsync(source, default);
                await repository.AddAsync(legacy, default);
                await repository.SaveChangesAsync(default);
            }

            await using (var db = new AppDbContext(options))
            {
                var repository = new DataSourceRepository(db);
                var loaded = (await repository.GetByIdAsync(source.Id, default))!;

                Assert.True(loaded.IsManaged);
                Assert.DoesNotContain("round-trip-secret", loaded.EncryptedConnection);
                Assert.Equal(details, ManagedConnections.Deserialize(protector.Unprotect(loaded.Id, loaded.EncryptedConnection!)));
                Assert.Null((await repository.GetByIdAsync(legacy.Id, default))!.EncryptedConnection);

                loaded.IsActive = false;
                loaded.Name = "managed (off)";
                repository.UpdateOnly(loaded, nameof(DataSource.IsActive), nameof(DataSource.Name));
                await repository.SaveChangesAsync(default);
            }

            await using (var db = new AppDbContext(options))
            {
                var repository = new DataSourceRepository(db);
                Assert.Equal(["legacy"], (await repository.GetActiveAsync(default)).Select(d => d.Name));
                Assert.Equal(2, (await repository.GetAllAsync(default)).Count);

                var stored = await db.DataSources.AsNoTracking().SingleAsync(d => d.Id == source.Id);
                Assert.Equal("managed (off)", stored.Name);
            }
        });
    }

    [MetadataSqlFact]
    public async Task A_config_named_sql_server_source_tests_ok_through_the_stored_path()
    {
        await WithDatabaseAsync(async (_, connectionString) =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:throwaway"] = connectionString }).Build();
            var resolver = new ConnectionStringResolver(
                configuration, new MetadataConnectionStringAccessor("Server=x"), DataSourceManagementTests.Protector(),
                NullLogger<ConnectionStringResolver>.Instance);
            var tester = new ConnectionTester(resolver, NullLogger<ConnectionTester>.Instance);

            var ok = await tester.TestStoredAsync(new DataSource { Name = "t", ConnectionStringName = "throwaway" }, default);
            var missing = await tester.TestStoredAsync(new DataSource { Name = "t", ConnectionStringName = "not-configured" }, default);

            Assert.True(ok.Ok, ok.Message);
            Assert.False(missing.Ok);
            await Task.CompletedTask;
        });
    }
}
