using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Commands.Admin;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Queries.Admin;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Authorization;
using QueryBuilder.Infrastructure.Catalog;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Repositories;
using static QueryBuilder.Tests.DataSourceManagementTests;

namespace QueryBuilder.Tests;

[Collection(QueryBuilder.Tests.Hub.MetadataDatabaseCollection.Name)]
public class DataSourceManagementFixTests
{
    private static ManagedConnectionDetails Stored(ConnectionSslMode ssl) =>
        new("db.example.com", 5432, "sales", "app", Secret, ssl);

    // ---- 1: lowering encryption is a changed connection ---------------------------------------------------

    [Theory]
    [InlineData(ConnectionSslMode.Require, ConnectionSslMode.Disable)]
    [InlineData(ConnectionSslMode.VerifyFull, ConnectionSslMode.Require)]
    [InlineData(ConnectionSslMode.VerifyFull, ConnectionSslMode.Disable)]
    public async Task Lowering_encryption_with_a_blank_password_is_refused_on_edit_and_test(ConnectionSslMode saved, ConnectionSslMode requested)
    {
        var rig = new Rig();
        var source = rig.AddManaged(details: Stored(saved));

        var edit = await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Update().Handle(
            new UpdateDataSourceCommand(source.Id, "Warehouse", null, Input(password: "", ssl: requested)), default));
        var test = await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Test().Handle(
            new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(password: "", ssl: requested), source.Id), default));

        Assert.Equal("Re-enter the password when changing the host, port, username or lowering encryption.", edit.Message);
        Assert.Equal(edit.Message, test.Message);
        Assert.Null(rig.Tester.Tested);
        Assert.Equal(saved, rig.Stored(source).SslMode);
    }

    [Theory]
    [InlineData(ConnectionSslMode.Disable, ConnectionSslMode.Require)]
    [InlineData(ConnectionSslMode.Require, ConnectionSslMode.VerifyFull)]
    [InlineData(ConnectionSslMode.Require, ConnectionSslMode.Require)]
    public async Task Raising_or_keeping_encryption_keeps_the_stored_password(ConnectionSslMode saved, ConnectionSslMode requested)
    {
        var rig = new Rig();
        var source = rig.AddManaged(details: Stored(saved));

        await rig.Test().Handle(new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(password: "", ssl: requested), source.Id), default);
        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "Warehouse", null, Input(password: "", ssl: requested)), default);

        Assert.Equal(Secret, rig.Tester.Tested!.Password);
        Assert.Equal(Secret, rig.Stored(source).Password);
        Assert.Equal(requested, rig.Stored(source).SslMode);
    }

    [Fact]
    public async Task A_missing_ssl_mode_keeps_the_stored_one_instead_of_defaulting()
    {
        var rig = new Rig();
        var source = rig.AddManaged(details: Stored(ConnectionSslMode.VerifyFull));

        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "Warehouse", null, Input(password: "", ssl: null)), default);
        await rig.Test().Handle(new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(password: "", ssl: null), source.Id), default);

        Assert.Equal(ConnectionSslMode.VerifyFull, rig.Stored(source).SslMode);
        Assert.Equal(ConnectionSslMode.VerifyFull, rig.Tester.Tested!.SslMode);
        Assert.Equal(Secret, rig.Tester.Tested.Password);
    }

    [Fact]
    public void A_typed_password_may_lower_encryption()
    {
        var resolved = ManagedConnections.Resolve(DataSourceProvider.PostgreSql, Input(password: "typed", ssl: ConnectionSslMode.Disable),
            Stored(ConnectionSslMode.VerifyFull));

        Assert.Equal(ConnectionSslMode.Disable, resolved.SslMode);
    }

    // ---- 6: an unreadable blob is never overwritten by a blank password -----------------------------------

    [Fact]
    public async Task A_blank_password_on_an_unreadable_blob_is_refused_and_the_blob_is_untouched()
    {
        var rig = new Rig();
        var source = rig.AddManaged();
        var foreign = Protector(9).Protect(source.Id, ManagedConnections.Serialize(Details()));
        source.EncryptedConnection = foreign;

        var edit = await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Update().Handle(
            new UpdateDataSourceCommand(source.Id, "Warehouse", null, Input(password: "")), default));
        var test = await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Test().Handle(
            new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(password: ""), source.Id), default));

        Assert.Equal("Re-enter the password: the saved details can't be read with the current key.", edit.Message);
        Assert.Equal(edit.Message, test.Message);
        Assert.Equal(foreign, source.EncryptedConnection);
        Assert.Null(rig.Tester.Tested);
    }

    // ---- 5: a blank Postgres password must not fall back to the host's environment ------------------------

    [Fact]
    public void A_blank_postgres_password_builds_a_nonblank_filler_so_the_host_environment_is_never_used()
    {
        var built = ManagedConnectionStringBuilder.Build(
            DataSourceProvider.PostgreSql, new ManagedConnectionDetails("h", 5432, "d", "u", "", ConnectionSslMode.Disable));

        var parsed = new NpgsqlConnectionStringBuilder(built);
        Assert.False(string.IsNullOrEmpty(parsed.Password));
        Assert.Equal(built, ManagedConnectionStringBuilder.Build(
            DataSourceProvider.PostgreSql, new ManagedConnectionDetails("h", 5432, "d", "u", "", ConnectionSslMode.Disable)));
    }

    [Fact]
    public void A_real_postgres_password_is_used_as_typed() =>
        Assert.Equal("typed", new NpgsqlConnectionStringBuilder(ManagedConnectionStringBuilder.Build(
            DataSourceProvider.PostgreSql, new ManagedConnectionDetails("h", 5432, "d", "u", "typed", ConnectionSslMode.Disable))).Password);

    // ---- 10 and 11: input rules ---------------------------------------------------------------------------

    [Theory]
    [InlineData("db.example.com")]
    [InlineData("10.0.0.5")]
    [InlineData("my_host-1")]
    [InlineData("[::1]")]
    [InlineData("[2001:db8::1]")]
    public void Ordinary_hosts_are_accepted(string host) =>
        Assert.Equal(host, ManagedConnections.Resolve(DataSourceProvider.PostgreSql, Input(host: host), null).Host);

    [Theory]
    [InlineData("np:\\\\server\\pipe\\sql\\query")]
    [InlineData("\\\\server\\share")]
    [InlineData("tcp:db.example.com")]
    [InlineData("(localdb)\\MSSQLLocalDB")]
    [InlineData("/var/run/postgresql")]
    [InlineData("host:5432")]
    [InlineData("ho st")]
    [InlineData("a,b")]
    [InlineData("x;Password=y")]
    public void Pipes_paths_prefixes_and_syntax_characters_are_refused_by_the_validator_and_the_resolver(string host)
    {
        var validation = new TestConnectionCommandValidator().Validate(new TestConnectionCommand(DataSourceProvider.SqlServer, Input(host: host), null));

        Assert.False(validation.IsValid);
        Assert.ThrowsAny<Exception>(() => ManagedConnections.Resolve(DataSourceProvider.SqlServer, Input(host: host), null));
    }

    [Fact]
    public void Instance_names_are_for_sql_server_only_and_keep_their_port()
    {
        Assert.Equal("sql01\\SQLEXPRESS", ManagedConnections.Resolve(DataSourceProvider.SqlServer, Input(host: "sql01\\SQLEXPRESS", port: 1500), null).Host);
        Assert.Throws<CatalogValidationException>(() => ManagedConnections.Resolve(DataSourceProvider.PostgreSql, Input(host: "sql01\\x"), null));
    }

    [Theory]
    [InlineData("localhost", 1433, "tcp:localhost,1433")]
    [InlineData("sql01\\SQLEXPRESS", 1500, "tcp:sql01\\SQLEXPRESS,1500")]
    public void Sql_server_is_always_reached_over_tcp(string host, int port, string expected)
    {
        var parsed = new SqlConnectionStringBuilder(ManagedConnectionStringBuilder.Build(
            DataSourceProvider.SqlServer, new ManagedConnectionDetails(host, port, "d", "u", "p", ConnectionSslMode.Require)));

        Assert.Equal(expected, parsed.DataSource);
    }

    [Fact]
    public void The_final_sql_server_data_source_value_is_limited_to_128_characters()
    {
        // "tcp:" (4) + host + "," (1) + "1433" (4) = host + 9
        var fits = new string('h', 119);
        var tooLong = new string('h', 120);

        var ok = ManagedConnections.Resolve(DataSourceProvider.SqlServer, Input(host: fits, port: 1433), null);
        var ex = Assert.Throws<CatalogValidationException>(() =>
            ManagedConnections.Resolve(DataSourceProvider.SqlServer, Input(host: tooLong, port: 1433), null));

        Assert.Equal(128, ManagedConnections.SqlServerDataSourceLength(ok.Host, ok.Port));
        Assert.Contains("host", ex.Message);
        new SqlConnection(ManagedConnectionStringBuilder.Build(DataSourceProvider.SqlServer, ok)).Dispose();
        ManagedConnections.Resolve(DataSourceProvider.PostgreSql, Input(host: tooLong), null);
    }

    [Fact]
    public async Task A_managed_row_the_driver_rejects_is_a_connect_failure_not_an_exception()
    {
        var protector = Protector();
        var source = new DataSource
        {
            Name = "m", Provider = DataSourceProvider.SqlServer, ConnectionStringName = DataSource.ManagedConnectionSentinel
        };
        // Saved some other way, past the validators: a data source value over SQL Server's limit.
        source.EncryptedConnection = protector.Protect(source.Id, ManagedConnections.Serialize(
            new ManagedConnectionDetails(new string('h', 200), 1433, "d", "u", "p", ConnectionSslMode.Require)));
        var resolver = Resolver(protector);

        var run = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            new SqlServerQueryExecutionService(resolver).ExecuteAsync(source, new GeneratedQuery("SELECT 1", []), new Dictionary<string, string?>(), null, default));
        var catalog = await Assert.ThrowsAsync<QueryExecutionException>(() => RealCatalog(new MemRepo(source), resolver).GetCatalogAsync(source.Id, default));

        Assert.Equal("Couldn't connect to this data source.", run.Message);
        Assert.Equal("Couldn't connect to this data source.", catalog.Message);
    }

    [Theory]
    [InlineData("database")]
    [InlineData("username")]
    [InlineData("password")]
    public void Control_characters_are_refused_in_database_username_and_password(string field)
    {
        var bad = "a\0b";
        var input = field switch
        {
            "database" => Input(database: bad),
            "username" => Input(username: bad),
            _ => Input(password: bad)
        };

        Assert.False(new TestConnectionCommandValidator().Validate(new TestConnectionCommand(DataSourceProvider.PostgreSql, input, null)).IsValid);
    }

    [Fact]
    public void Sql_server_limits_the_password_to_128_characters_but_postgres_does_not()
    {
        ManagedConnections.Resolve(DataSourceProvider.SqlServer, Input(password: new string('p', 128), port: null), null);
        Assert.Throws<CatalogValidationException>(() =>
            ManagedConnections.Resolve(DataSourceProvider.SqlServer, Input(password: new string('p', 129), port: null), null));
        ManagedConnections.Resolve(DataSourceProvider.PostgreSql, Input(password: new string('p', 129)), null);
    }

    [Fact]
    public async Task A_builder_failure_in_a_test_is_a_failed_result_not_an_exception()
    {
        var tester = new ConnectionTester(Resolver(Protector()), NullLogger<ConnectionTester>.Instance);

        var result = await tester.TestAsync(DataSourceProvider.MySql, Details(), default);

        Assert.False(result.Ok);
        Assert.Equal("The connection details aren't valid.", result.Message);
    }

    // ---- 12: pools ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Editing_the_connection_and_turning_off_clear_the_pool()
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "Warehouse", null, Input(password: "new")), default);
        Assert.Equal(1, rig.Pools.Cleared);
        await rig.SetActive().Handle(new SetDataSourceActiveCommand(source.Id, false), default);
        Assert.Equal(2, rig.Pools.Cleared);
    }

    // ---- 15: a policy name is a real admin gate -----------------------------------------------------------

    [Fact]
    public void An_admin_policy_name_counts_as_a_gate_even_with_the_default_mode() =>
        new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=(localdb)\\none;Database=none";
            o.DataSourceManagement.Enabled = true;
            o.DataSourceManagement.EncryptionKey = Convert.ToBase64String(NewKey());
            o.AdminAuthorization.PolicyName = "QueryBuilderAdmins";
        });

    // ---- 2 and 16: unreachable and misconfigured SQL Server sources --------------------------------------

    private static DataCatalogService RealCatalog(IDataSourceRepository repository, IConnectionStringResolver resolver) =>
        new(repository, resolver, [new SqlServerCatalogReader()], new MemoryCache(new MemoryCacheOptions()), NullLogger<DataCatalogService>.Instance);

    private static ConnectionStringResolver ConfigResolver(params (string Name, string Value)[] entries) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(entries.ToDictionary(e => $"ConnectionStrings:{e.Name}", e => (string?)e.Value)).Build(),
            new MetadataConnectionStringAccessor("Server=meta;Database=m"), Protector(), NullLogger<ConnectionStringResolver>.Instance);

    [Theory]
    [InlineData("Server=127.0.0.1,1;Database=d;User Id=u;Password=p;Connect Timeout=5;Encrypt=false")]
    [InlineData(null)]
    [InlineData("Server=;;;not a connection string")]
    public async Task The_admin_page_opens_for_a_sql_server_source_that_cannot_be_reached_or_configured(string? configured)
    {
        var rig = new Rig();
        var source = rig.AddConfigNamed();
        var resolver = configured is null ? ConfigResolver() : ConfigResolver(("legacy", configured));

        var dto = await new GetAdminDataSourceDetailQueryHandler(rig.Repo, RealCatalog(rig.Repo, resolver), new RecordLimitSettings(null), rig.Protector)
            .Handle(new GetAdminDataSourceDetailQuery(source.Id), default);

        Assert.False(string.IsNullOrWhiteSpace(dto.CatalogError));
        Assert.Empty(dto.Objects);
        Assert.Equal(source.Id, dto.Id);
        Assert.DoesNotContain("127.0.0.1", dto.CatalogError);
    }

    [Fact]
    public async Task The_default_sentinel_is_hidden_and_reported_as_the_metadata_database()
    {
        var rig = new Rig();
        var source = rig.AddConfigNamed();
        source.ConnectionStringName = DataSource.DefaultConnectionStringSentinel;

        var dto = await new GetAdminDataSourceDetailQueryHandler(rig.Repo, rig.Catalog, new RecordLimitSettings(null), rig.Protector)
            .Handle(new GetAdminDataSourceDetailQuery(source.Id), default);

        Assert.Null(dto.ConnectionStringName);
        Assert.True(dto.UsesMetadataDatabase);
        Assert.DoesNotContain(DataSource.DefaultConnectionStringSentinel, System.Text.Json.JsonSerializer.Serialize(dto, CamelJson));
    }

    private static DataSource ManagedSqlServer(IConnectionSecretProtector protector)
    {
        var source = new DataSource
        {
            Name = "m", Provider = DataSourceProvider.SqlServer, ConnectionStringName = DataSource.ManagedConnectionSentinel,
            CatalogScope = CatalogScope.TablesAndViews
        };
        source.EncryptedConnection = protector.Protect(source.Id,
            ManagedConnections.Serialize(new ManagedConnectionDetails("127.0.0.1", 1, "d", "login-name-secret", "p", ConnectionSslMode.Disable)));
        return source;
    }

    [Fact]
    public async Task A_managed_sql_server_connect_failure_shows_business_users_the_short_message()
    {
        var protector = Protector();
        var source = ManagedSqlServer(protector);
        var resolver = Resolver(protector);

        var run = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            new SqlServerQueryExecutionService(resolver).ExecuteAsync(source, new GeneratedQuery("SELECT 1", []), new Dictionary<string, string?>(), null, default));
        var repo = new MemRepo(source);
        var catalog = await Assert.ThrowsAsync<QueryExecutionException>(() => RealCatalog(repo, resolver).GetCatalogAsync(source.Id, default));

        Assert.Equal("Couldn't connect to this data source.", run.Message);
        Assert.Equal("Couldn't connect to this data source.", catalog.Message);
    }

    [Fact]
    public async Task A_config_named_sql_server_connect_failure_keeps_its_existing_driver_message()
    {
        var source = new DataSource { Name = "c", ConnectionStringName = "legacy" };
        var resolver = ConfigResolver(("legacy", "Server=127.0.0.1,1;Database=d;User Id=u;Password=p;Connect Timeout=5;Encrypt=false"));

        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            new SqlServerQueryExecutionService(resolver).ExecuteAsync(source, new GeneratedQuery("SELECT 1", []), new Dictionary<string, string?>(), null, default));
        Assert.StartsWith("The database rejected the query: ", ex.Message);
    }

    // ---- 7: a policy save must not restore columns changed in between ------------------------------------

    [MetadataSqlFact]
    public async Task A_policy_save_does_not_restore_a_turn_off_or_a_password_changed_in_between()
    {
        var server = Environment.GetEnvironmentVariable(MetadataSqlFactAttribute.EnvironmentVariable)!;
        var name = $"qb_meta_it_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)).Options;

        try
        {
            await using (var setup = new AppDbContext(options))
            {
                await setup.Database.MigrateAsync();
            }

            var protector = Protector();
            var source = new DataSource
            {
                Name = "managed", Provider = DataSourceProvider.PostgreSql, ConnectionStringName = DataSource.ManagedConnectionSentinel, CreatedBy = "t"
            };
            source.EncryptedConnection = protector.Protect(source.Id, ManagedConnections.Serialize(Details()));
            await using (var seed = new AppDbContext(options))
            {
                await new DataSourceRepository(seed).AddAsync(source, default);
                await seed.SaveChangesAsync();
            }

            var replacement = protector.Protect(source.Id, ManagedConnections.Serialize(Details() with { Password = "rotated" }));

            // The slow catalog read is where another admin's change lands.
            var catalog = new ConcurrentChangeCatalog(async () =>
            {
                await using var other = new AppDbContext(options);
                var repo = new DataSourceRepository(other);
                var row = (await repo.GetByIdAsync(source.Id, default))!;
                row.IsActive = false;
                row.EncryptedConnection = replacement;
                repo.UpdateOnly(row, nameof(DataSource.IsActive), nameof(DataSource.EncryptedConnection));
                await repo.SaveChangesAsync(default);
            });

            await using (var db = new AppDbContext(options))
            {
                var handler = new UpdateCatalogPolicyCommandHandler(new DataSourceRepository(db), catalog, new User(), new AuditCapture());
                await handler.Handle(new UpdateCatalogPolicyCommand(source.Id, CatalogScope.Tables, []), default);
            }

            await using (var db = new AppDbContext(options))
            {
                var limit = new UpdateRecordLimitCommandHandler(new DataSourceRepository(db), new User(), new AuditCapture());
                await limit.Handle(new UpdateRecordLimitCommand(source.Id, 25), default);
            }

            await using (var check = new AppDbContext(options))
            {
                var row = await check.DataSources.AsNoTracking().SingleAsync(d => d.Id == source.Id);
                Assert.Equal(CatalogScope.Tables, row.CatalogScope);
                Assert.Equal(25, row.MaxRecords);
                Assert.False(row.IsActive);
                Assert.Equal(replacement, row.EncryptedConnection);
            }
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

    private sealed class ConcurrentChangeCatalog(Func<Task> onRead) : IDataCatalogService
    {
        public Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ValidateAsync(Guid dataSourceId, Domain.Model.QueryDefinition definition, CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task<List<Domain.Model.SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken)
        {
            await onRead();
            return [];
        }
        public Task<List<Domain.Model.AdminUnavailableObject>> GetUnavailableObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) =>
            Task.FromResult(new List<Domain.Model.AdminUnavailableObject>());
        public void InvalidateCatalogCache(Guid dataSourceId) { }
    }
}
