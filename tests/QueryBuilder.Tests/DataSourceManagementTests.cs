using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.DataSources;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Queries.Admin;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Authorization;
using QueryBuilder.Infrastructure.Catalog;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Security;

namespace QueryBuilder.Tests;

public class DataSourceManagementTests
{
    internal const string Secret = "S3cr3t-p@ss;w0rd";

    internal static readonly JsonSerializerOptions CamelJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    internal static byte[] NewKey(byte fill = 7) => Enumerable.Repeat(fill, 32).Select(b => b).ToArray();

    internal static AesGcmConnectionSecretProtector Protector(byte fill = 7) => new(new ConnectionSecretKey(NewKey(fill)));

    internal static ConnectionInput Input(
        string host = "db.example.com", int? port = 5432, string database = "sales", string username = "app",
        string? password = Secret, ConnectionSslMode? ssl = ConnectionSslMode.Require) =>
        new(host, port, database, username, password, ssl);

    internal static ManagedConnectionDetails Details(string host = "db.example.com", int port = 5432, string username = "app") =>
        new(host, port, "sales", username, Secret, ConnectionSslMode.Require);

    // ---- protector ----------------------------------------------------------------------------------------

    [Fact]
    public void Protector_round_trips_and_never_stores_plaintext()
    {
        var id = Guid.NewGuid();
        var blob = Protector().Protect(id, "hello " + Secret);

        Assert.DoesNotContain(Secret, blob);
        Assert.Equal("hello " + Secret, Protector().Unprotect(id, blob));
    }

    [Fact]
    public void Protector_uses_a_fresh_nonce_each_time()
    {
        var id = Guid.NewGuid();
        Assert.NotEqual(Protector().Protect(id, "x"), Protector().Protect(id, "x"));
    }

    [Fact]
    public void Protector_blob_starts_with_the_version_byte_and_has_the_documented_layout()
    {
        var bytes = Convert.FromBase64String(Protector().Protect(Guid.NewGuid(), "abc"));

        Assert.Equal(1, bytes[0]);
        Assert.Equal(1 + 12 + 16 + 3, bytes.Length);
    }

    [Fact]
    public void Protector_blob_copied_to_another_row_fails()
    {
        var blob = Protector().Protect(Guid.NewGuid(), "x");

        Assert.Throws<ConnectionSecretException>(() => Protector().Unprotect(Guid.NewGuid(), blob));
    }

    [Fact]
    public void Protector_wrong_key_fails_without_leaking_anything()
    {
        var id = Guid.NewGuid();
        var blob = Protector(7).Protect(id, Secret);

        var ex = Assert.Throws<ConnectionSecretException>(() => Protector(8).Unprotect(id, blob));

        Assert.DoesNotContain(Secret, ex.Message);
        Assert.DoesNotContain(Convert.ToBase64String(NewKey(8)), ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(13)]
    [InlineData(29)]
    [InlineData(31)]
    public void Protector_tampered_blob_fails(int byteIndex)
    {
        var id = Guid.NewGuid();
        var bytes = Convert.FromBase64String(Protector().Protect(id, "some details"));
        bytes[byteIndex] ^= 0x01;

        Assert.Throws<ConnectionSecretException>(() => Protector().Unprotect(id, Convert.ToBase64String(bytes)));
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]
    public void Protector_garbage_fails_with_the_specific_exception(string garbage) =>
        Assert.Throws<ConnectionSecretException>(() => Protector().Unprotect(Guid.NewGuid(), garbage));

    [Fact]
    public void Protector_unknown_version_fails()
    {
        var id = Guid.NewGuid();
        var bytes = Convert.FromBase64String(Protector().Protect(id, "x"));
        bytes[0] = 9;

        Assert.Throws<ConnectionSecretException>(() => Protector().Unprotect(id, Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Protector_without_a_key_throws_on_both_directions()
    {
        var none = new AesGcmConnectionSecretProtector(new ConnectionSecretKey(null));

        Assert.Throws<ConnectionSecretException>(() => none.Protect(Guid.NewGuid(), "x"));
        Assert.Throws<ConnectionSecretException>(() => none.Unprotect(Guid.NewGuid(), Protector().Protect(Guid.NewGuid(), "x")));
    }

    // ---- startup validation -------------------------------------------------------------------------------

    private static void Register(Action<QueryBuilderEditorOptions> configure) =>
        new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=(localdb)\\none;Database=none";
            configure(o);
        });

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]
    [InlineData("   ")]
    public void Invalid_key_throws_at_startup_even_without_enabling(string key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Register(o => o.DataSourceManagement.EncryptionKey = key));

        Assert.Contains("EncryptionKey", ex.Message);
        Assert.Contains("32 bytes", ex.Message);
    }

    [Fact]
    public void Thirty_one_byte_key_is_refused() =>
        Assert.Throws<InvalidOperationException>(() =>
            Register(o => o.DataSourceManagement.EncryptionKey = Convert.ToBase64String(new byte[31])));

    [Fact]
    public void Enabled_without_a_key_throws() =>
        Assert.Contains("EncryptionKey", Assert.Throws<InvalidOperationException>(() => Register(o =>
        {
            o.DataSourceManagement.Enabled = true;
            o.AdminAuthorization.Mode = QueryBuilderAuthorizationMode.Authenticated;
        })).Message);

    [Fact]
    public void Enabled_with_anonymous_admins_throws_unless_allowed()
    {
        var key = Convert.ToBase64String(NewKey());
        var ex = Assert.Throws<InvalidOperationException>(() => Register(o =>
        {
            o.DataSourceManagement.Enabled = true;
            o.DataSourceManagement.EncryptionKey = key;
        }));
        Assert.Contains("AllowAnonymousAdmins", ex.Message);
        Assert.DoesNotContain(key, ex.Message);

        Register(o =>
        {
            o.DataSourceManagement.Enabled = true;
            o.DataSourceManagement.EncryptionKey = key;
            o.DataSourceManagement.AllowAnonymousAdmins = true;
        });
    }

    [Theory]
    [InlineData(QueryBuilderAuthorizationMode.Authenticated)]
    [InlineData(QueryBuilderAuthorizationMode.Role)]
    public void Enabled_with_a_real_admin_gate_is_accepted(QueryBuilderAuthorizationMode mode) =>
        Register(o =>
        {
            o.DataSourceManagement.Enabled = true;
            o.DataSourceManagement.EncryptionKey = Convert.ToBase64String(NewKey());
            o.AdminAuthorization.Mode = mode;
            o.AdminAuthorization.RoleNames = ["Admin"];
        });

    [Fact]
    public void A_key_alone_is_accepted_and_leaves_management_off()
    {
        var services = new ServiceCollection();
        services.AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=(localdb)\\none;Database=none";
            o.DataSourceManagement.EncryptionKey = Convert.ToBase64String(NewKey());
        });
        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<DataSourceManagementSettings>().Enabled);
        Assert.NotNull(provider.GetRequiredService<IConnectionSecretProtector>());
    }

    // ---- connection string builder ------------------------------------------------------------------------

    [Fact]
    public void Hostile_values_do_not_inject_keywords_on_sql_server()
    {
        var details = new ManagedConnectionDetails(
            "x;Password=y", 1433, "db;Initial Catalog=evil", "bob;Integrated Security=true", Secret, ConnectionSslMode.Require);

        var parsed = new SqlConnectionStringBuilder(ManagedConnectionStringBuilder.Build(DataSourceProvider.SqlServer, details));

        Assert.Equal("tcp:x;Password=y,1433", parsed.DataSource);
        Assert.Equal("db;Initial Catalog=evil", parsed.InitialCatalog);
        Assert.Equal("bob;Integrated Security=true", parsed.UserID);
        Assert.False(parsed.IntegratedSecurity);
        Assert.Equal(Secret, parsed.Password);
    }

    [Fact]
    public void Hostile_values_do_not_inject_keywords_on_postgres()
    {
        var details = new ManagedConnectionDetails(
            "x;Password=y", 5432, "db;Database=evil", "bob;Password=x", Secret, ConnectionSslMode.Require);

        var parsed = new NpgsqlConnectionStringBuilder(ManagedConnectionStringBuilder.Build(DataSourceProvider.PostgreSql, details));

        Assert.Equal("x;Password=y", parsed.Host);
        Assert.Equal("db;Database=evil", parsed.Database);
        Assert.Equal("bob;Password=x", parsed.Username);
        Assert.Equal(Secret, parsed.Password);
    }

    [Theory]
    [InlineData(ConnectionSslMode.Disable, false, false)]
    [InlineData(ConnectionSslMode.Require, true, true)]
    [InlineData(ConnectionSslMode.VerifyFull, true, false)]
    public void Ssl_mode_maps_for_sql_server(ConnectionSslMode mode, bool mandatory, bool trust)
    {
        var parsed = new SqlConnectionStringBuilder(ManagedConnectionStringBuilder.Build(
            DataSourceProvider.SqlServer, new ManagedConnectionDetails("h", 1433, "d", "u", "p", mode)));

        Assert.Equal(mandatory ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional, parsed.Encrypt);
        Assert.Equal(trust, parsed.TrustServerCertificate);
        Assert.Equal(10, parsed.ConnectTimeout);
    }

    [Theory]
    [InlineData(ConnectionSslMode.Disable, SslMode.Disable)]
    [InlineData(ConnectionSslMode.Require, SslMode.Require)]
    [InlineData(ConnectionSslMode.VerifyFull, SslMode.VerifyFull)]
    public void Ssl_mode_maps_for_postgres(ConnectionSslMode mode, SslMode expected)
    {
        var parsed = new NpgsqlConnectionStringBuilder(ManagedConnectionStringBuilder.Build(
            DataSourceProvider.PostgreSql, new ManagedConnectionDetails("h", 5432, "d", "u", "p", mode)));

        Assert.Equal(expected, parsed.SslMode);
        Assert.Equal(10, parsed.Timeout);
    }

    [Fact]
    public void Sql_server_host_and_port_become_one_data_source_value()
    {
        var parsed = new SqlConnectionStringBuilder(ManagedConnectionStringBuilder.Build(
            DataSourceProvider.SqlServer, new ManagedConnectionDetails("sql.example.com", 14330, "d", "u", "p", ConnectionSslMode.Require)));

        Assert.Equal("tcp:sql.example.com,14330", parsed.DataSource);
    }

    [Fact]
    public void Unsupported_providers_cannot_be_built() =>
        Assert.Throws<NotSupportedException>(() => ManagedConnectionStringBuilder.Build(DataSourceProvider.MySql, Details()));

    // ---- resolver -----------------------------------------------------------------------------------------

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }

    internal static ConnectionStringResolver Resolver(IConnectionSecretProtector protector, ILogger<ConnectionStringResolver>? logger = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:legacy"] = "Server=legacy;Database=d" }).Build(),
            new MetadataConnectionStringAccessor("Server=meta;Database=m"), protector, logger ?? NullLogger<ConnectionStringResolver>.Instance);

    private static DataSource Managed(IConnectionSecretProtector protector, ManagedConnectionDetails? details = null)
    {
        var source = new DataSource
        {
            Name = "managed", Provider = DataSourceProvider.PostgreSql, ConnectionStringName = DataSource.ManagedConnectionSentinel
        };
        source.EncryptedConnection = protector.Protect(source.Id, ManagedConnections.Serialize(details ?? Details()));
        return source;
    }

    [Fact]
    public void Resolver_decrypts_and_builds_a_managed_source()
    {
        var protector = Protector();
        var parsed = new NpgsqlConnectionStringBuilder(Resolver(protector).Resolve(Managed(protector)));

        Assert.Equal("db.example.com", parsed.Host);
        Assert.Equal("app", parsed.Username);
        Assert.Equal(Secret, parsed.Password);
    }

    [Fact]
    public void Resolver_failure_is_a_clean_query_error_logged_without_secrets_and_leaves_other_sources_alone()
    {
        var source = Managed(Protector(7));
        var logger = new CapturingLogger<ConnectionStringResolver>();
        var resolver = Resolver(Protector(8), logger);

        var ex = Assert.Throws<QueryExecutionException>(() => resolver.Resolve(source));

        Assert.Equal("Couldn't connect to this data source.", ex.Message);
        Assert.DoesNotContain(Secret, string.Join('\n', logger.Messages));
        Assert.Equal("Server=legacy;Database=d", resolver.Resolve(new DataSource { ConnectionStringName = "legacy" }));
        Assert.Equal("Server=meta;Database=m", resolver.Resolve(new DataSource { ConnectionStringName = DataSource.DefaultConnectionStringSentinel }));
    }

    [Fact]
    public void Resolver_without_a_key_refuses_a_managed_source_the_same_way()
    {
        var source = Managed(Protector());
        var resolver = Resolver(new AesGcmConnectionSecretProtector(new ConnectionSecretKey(null)));

        Assert.Equal("Couldn't connect to this data source.", Assert.Throws<QueryExecutionException>(() => resolver.Resolve(source)).Message);
    }

    [Fact]
    public void Resolver_managed_row_with_no_blob_is_refused()
    {
        var source = new DataSource { ConnectionStringName = DataSource.ManagedConnectionSentinel };

        Assert.Throws<QueryExecutionException>(() => Resolver(Protector()).Resolve(source));
    }

    // ---- fakes --------------------------------------------------------------------------------------------

    internal sealed class MemRepo(params DataSource[] initial) : IDataSourceRepository
    {
        public List<DataSource> Items { get; } = [.. initial];
        public int Saves { get; private set; }
        public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));
        public Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken) => Task.FromResult(Items.Where(x => x.IsActive).ToList());
        public Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(Items.ToList());
        public Task AddAsync(DataSource dataSource, CancellationToken cancellationToken) { Items.Add(dataSource); return Task.CompletedTask; }
        public void UpdateOnly(DataSource dataSource, params string[] propertyNames) { }
        public Task<bool> TryReplaceEncryptedConnectionAsync(Guid id, string expected, string replacement, CancellationToken cancellationToken)
        {
            var row = Items.FirstOrDefault(x => x.Id == id);
            if (row is null || row.EncryptedConnection != expected) return Task.FromResult(false);
            row.EncryptedConnection = replacement;
            return Task.FromResult(true);
        }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) { Saves++; return Task.FromResult(1); }
    }

    internal sealed class AuditCapture : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken) { Entries.Add(entry); return Task.CompletedTask; }
    }

    internal sealed class User : ICurrentUserService
    {
        public string UserId => "admin@example.com";
        public string DisplayName => "Admin";
        public string? Email => null;
        public QueryBuilder.Domain.Enums.HubRole? Role => null;
        public bool IsSuperAdmin => false;
    }

    internal sealed class CatalogStub : IDataCatalogService
    {
        public List<Guid> Invalidated { get; } = [];
        public Exception? RawFailure { get; init; }
        public Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) =>
            RawFailure is null ? Task.FromResult(new List<SchemaObjectMetadata>()) : throw RawFailure;
        public Task<List<AdminUnavailableObject>> GetUnavailableObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) =>
            Task.FromResult(new List<AdminUnavailableObject>());
        public void InvalidateCatalogCache(Guid dataSourceId) => Invalidated.Add(dataSourceId);
    }

    internal sealed class PoolStub : IConnectionPoolCleaner
    {
        public int Cleared { get; private set; }
        public void Clear(DataSource dataSource) => Cleared++;
    }

    internal sealed class TesterStub : IConnectionTester
    {
        public ManagedConnectionDetails? Tested { get; private set; }
        public Task<ConnectionTestResult> TestAsync(DataSourceProvider provider, ManagedConnectionDetails details, CancellationToken cancellationToken)
        {
            Tested = details;
            return Task.FromResult(new ConnectionTestResult(true, "Connected."));
        }
        public Task<ConnectionTestResult> TestStoredAsync(DataSource dataSource, CancellationToken cancellationToken) =>
            Task.FromResult(new ConnectionTestResult(true, "Connected."));
    }

    internal sealed class Rig
    {
        public MemRepo Repo { get; }
        public AuditCapture Audit { get; } = new();
        public CatalogStub Catalog { get; } = new();
        public TesterStub Tester { get; } = new();
        public PoolStub Pools { get; } = new();
        public IConnectionSecretProtector Protector { get; } = DataSourceManagementTests.Protector();
        public DataSourceManagementSettings Settings { get; }

        public Rig(bool enabled = true, params DataSource[] existing)
        {
            Repo = new MemRepo(existing);
            Settings = new DataSourceManagementSettings(enabled);
        }

        public CreateDataSourceCommandHandler Create() => new(Repo, Protector, Settings, new User(), Audit);
        public UpdateDataSourceCommandHandler Update() => new(Repo, Protector, Catalog, Pools, Settings, new User(), Audit);
        public SetDataSourceActiveCommandHandler SetActive() => new(Repo, Catalog, Pools, Settings, new User(), Audit);
        public TestConnectionCommandHandler Test() => new(Repo, Protector, Tester, Settings);
        public TestStoredConnectionCommandHandler TestStored() => new(Repo, Tester, Settings);

        public DataSource AddManaged(string name = "Warehouse", DataSourceProvider provider = DataSourceProvider.PostgreSql, ManagedConnectionDetails? details = null)
        {
            var source = new DataSource { Name = name, Provider = provider, ConnectionStringName = DataSource.ManagedConnectionSentinel, CreatedBy = "x" };
            source.EncryptedConnection = Protector.Protect(source.Id, ManagedConnections.Serialize(details ?? Details()));
            Repo.Items.Add(source);
            return source;
        }

        public DataSource AddConfigNamed(string name = "Legacy")
        {
            var source = new DataSource { Name = name, ConnectionStringName = "legacy", CreatedBy = "x" };
            Repo.Items.Add(source);
            return source;
        }

        public ManagedConnectionDetails Stored(DataSource source) =>
            ManagedConnections.Deserialize(Protector.Unprotect(source.Id, source.EncryptedConnection!));
    }

    // ---- create -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_encrypts_the_details_and_stores_no_plaintext_password()
    {
        var rig = new Rig();

        var created = await rig.Create().Handle(
            new CreateDataSourceCommand("  Warehouse ", "desc", DataSourceProvider.PostgreSql, Input()), default);

        var row = Assert.Single(rig.Repo.Items);
        Assert.Equal(created.Id, row.Id);
        Assert.Equal("Warehouse", row.Name);
        Assert.Equal(DataSource.ManagedConnectionSentinel, row.ConnectionStringName);
        Assert.Equal(CatalogScope.Views, row.CatalogScope);
        Assert.True(row.IsActive);
        Assert.False(string.IsNullOrEmpty(row.EncryptedConnection));
        Assert.DoesNotContain(Secret, row.EncryptedConnection);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(row.Description) + row.Name + row.ConnectionStringName);

        var stored = rig.Stored(row);
        Assert.Equal(Secret, stored.Password);
        Assert.Equal("db.example.com", stored.Host);
        Assert.Equal(5432, stored.Port);
        Assert.Equal(1, rig.Repo.Saves);
    }

    [Fact]
    public async Task Create_blob_is_bound_to_the_new_row()
    {
        var rig = new Rig();
        await rig.Create().Handle(new CreateDataSourceCommand("A", null, DataSourceProvider.SqlServer, Input(port: null)), default);
        var row = rig.Repo.Items.Single();

        Assert.Equal(1433, rig.Stored(row).Port);
        Assert.Throws<ConnectionSecretException>(() => rig.Protector.Unprotect(Guid.NewGuid(), row.EncryptedConnection!));
    }

    [Fact]
    public async Task Create_response_and_audit_never_contain_the_password()
    {
        var rig = new Rig();

        var created = await rig.Create().Handle(
            new CreateDataSourceCommand("Warehouse", null, DataSourceProvider.PostgreSql, Input()), default);

        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(AuditAction.DataSourceCreated, entry.Action);
        var serialized = JsonSerializer.Serialize(created, CamelJson) + JsonSerializer.Serialize(entry, CamelJson) + entry.Summary;
        Assert.DoesNotContain(Secret, serialized);
        Assert.DoesNotContain(rig.Repo.Items[0].EncryptedConnection!, serialized);
        Assert.Contains("db.example.com", serialized);
    }

    [Fact]
    public async Task Create_without_a_password_is_allowed_for_trust_authentication()
    {
        var rig = new Rig();

        await rig.Create().Handle(new CreateDataSourceCommand("Trusted", null, DataSourceProvider.PostgreSql, Input(password: null)), default);

        Assert.Equal("", rig.Stored(rig.Repo.Items.Single()).Password);
    }

    // ---- edit and the password rule -----------------------------------------------------------------------

    [Fact]
    public async Task Edit_with_a_blank_password_and_the_same_host_port_and_username_keeps_the_stored_password()
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "Renamed", "d",
            Input(database: "other", password: "", ssl: ConnectionSslMode.VerifyFull)), default);

        var stored = rig.Stored(source);
        Assert.Equal(Secret, stored.Password);
        Assert.Equal("other", stored.Database);
        Assert.Equal(ConnectionSslMode.VerifyFull, stored.SslMode);
        Assert.Equal("Renamed", source.Name);
        Assert.Contains(source.Id, rig.Catalog.Invalidated);
    }

    [Theory]
    [InlineData("other.example.com", 5432, "app")]
    [InlineData("db.example.com", 5433, "app")]
    [InlineData("db.example.com", 5432, "someone-else")]
    public async Task Edit_with_a_blank_password_refuses_when_host_port_or_username_changed(string host, int port, string username)
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Update().Handle(
            new UpdateDataSourceCommand(source.Id, "Warehouse", null, Input(host: host, port: port, username: username, password: null)), default));

        Assert.Equal(ManagedConnections.RePasswordMessage, ex.Message);
        Assert.Equal(Secret, rig.Stored(source).Password);
        Assert.Equal("db.example.com", rig.Stored(source).Host);
    }

    [Fact]
    public async Task Edit_with_a_new_password_and_a_new_host_is_allowed()
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "Warehouse", null,
            Input(host: "other.example.com", password: "new-one")), default);

        Assert.Equal("new-one", rig.Stored(source).Password);
        Assert.Equal("other.example.com", rig.Stored(source).Host);
    }

    [Fact]
    public async Task Edit_without_connection_changes_only_name_and_description()
    {
        var rig = new Rig();
        var source = rig.AddManaged();
        var before = source.EncryptedConnection;

        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "New name", "New description", null), default);

        Assert.Equal(before, source.EncryptedConnection);
        Assert.Equal("New description", source.Description);
    }

    [Fact]
    public async Task Edit_audit_lists_changed_fields_and_never_the_password()
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "Warehouse", null,
            Input(database: "other", password: "brand-new-secret")), default);

        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(AuditAction.DataSourceUpdated, entry.Action);
        var serialized = JsonSerializer.Serialize(entry, CamelJson);
        Assert.Contains("database", serialized);
        Assert.DoesNotContain("brand-new-secret", serialized);
        Assert.DoesNotContain(Secret, serialized);
        Assert.DoesNotContain(source.EncryptedConnection!, serialized);
    }

    [Fact]
    public async Task Edit_keeps_the_provider_and_the_policy()
    {
        var rig = new Rig();
        var source = rig.AddManaged(provider: DataSourceProvider.SqlServer);
        source.CatalogScope = CatalogScope.Tables;
        source.AllowedObjects = ["sales.orders"];
        source.MaxRecords = 50;

        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "Warehouse", null, Input(password: "")), default);

        Assert.Equal(DataSourceProvider.SqlServer, source.Provider);
        Assert.Equal(CatalogScope.Tables, source.CatalogScope);
        Assert.Equal(["sales.orders"], source.AllowedObjects);
        Assert.Equal(50, source.MaxRecords);
    }

    [Fact]
    public async Task Edit_refuses_a_connection_for_a_source_that_comes_from_host_configuration()
    {
        var rig = new Rig();
        var source = rig.AddConfigNamed();

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Update().Handle(
            new UpdateDataSourceCommand(source.Id, "Legacy", null, Input()), default));
        Assert.Contains("host configuration", ex.Message);

        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "Legacy renamed", "just text", null), default);
        Assert.Equal("Legacy renamed", source.Name);
        Assert.Equal("legacy", source.ConnectionStringName);
        Assert.Null(source.EncryptedConnection);
    }

    [Fact]
    public async Task Edit_of_a_source_whose_blob_cannot_be_read_needs_the_whole_connection_again()
    {
        var rig = new Rig();
        var source = rig.AddManaged();
        source.EncryptedConnection = Protector(9).Protect(source.Id, ManagedConnections.Serialize(Details()));

        await rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "Warehouse", null, Input(password: "typed-again")), default);

        Assert.Equal("typed-again", rig.Stored(source).Password);
    }

    // ---- test connection ----------------------------------------------------------------------------------

    [Fact]
    public async Task Test_with_an_existing_id_and_blank_password_uses_the_stored_password_when_unchanged()
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        var result = await rig.Test().Handle(new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(password: ""), source.Id), default);

        Assert.True(result.Ok);
        Assert.Equal(Secret, rig.Tester.Tested!.Password);
    }

    [Theory]
    [InlineData("other.example.com", 5432, "app")]
    [InlineData("db.example.com", 5433, "app")]
    [InlineData("db.example.com", 5432, "other-user")]
    public async Task Test_with_a_blank_password_refuses_when_host_port_or_username_changed_and_never_connects(string host, int port, string username)
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Test().Handle(
            new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(host: host, port: port, username: username, password: ""), source.Id), default));

        Assert.Equal(ManagedConnections.RePasswordMessage, ex.Message);
        Assert.Null(rig.Tester.Tested);
    }

    [Fact]
    public async Task Test_without_an_existing_id_never_borrows_a_stored_password()
    {
        var rig = new Rig();
        rig.AddManaged();

        await rig.Test().Handle(new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(password: ""), null), default);

        Assert.Equal("", rig.Tester.Tested!.Password);
    }

    [Fact]
    public async Task Test_refuses_a_provider_that_differs_from_the_stored_one()
    {
        var rig = new Rig();
        var source = rig.AddManaged(provider: DataSourceProvider.PostgreSql);

        await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Test().Handle(
            new TestConnectionCommand(DataSourceProvider.SqlServer, Input(password: ""), source.Id), default));
        Assert.Null(rig.Tester.Tested);
    }

    // ---- 404 while the feature is off ---------------------------------------------------------------------

    [Fact]
    public async Task Every_management_command_is_not_found_while_disabled()
    {
        var rig = new Rig(enabled: false);
        var source = rig.AddManaged();

        await Assert.ThrowsAsync<NotFoundException>(() => rig.Create().Handle(new CreateDataSourceCommand("X", null, DataSourceProvider.PostgreSql, Input()), default));
        await Assert.ThrowsAsync<NotFoundException>(() => rig.Update().Handle(new UpdateDataSourceCommand(source.Id, "X", null, null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => rig.SetActive().Handle(new SetDataSourceActiveCommand(source.Id, false), default));
        await Assert.ThrowsAsync<NotFoundException>(() => rig.Test().Handle(new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(), null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => rig.TestStored().Handle(new TestStoredConnectionCommand(source.Id), default));

        Assert.Single(rig.Repo.Items);
        Assert.True(source.IsActive);
        Assert.Empty(rig.Audit.Entries);
    }

    // ---- turn off / on ------------------------------------------------------------------------------------

    [Fact]
    public async Task Turning_a_source_off_and_on_audits_and_clears_the_catalog_cache()
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        await rig.SetActive().Handle(new SetDataSourceActiveCommand(source.Id, false), default);
        Assert.False(source.IsActive);
        await rig.SetActive().Handle(new SetDataSourceActiveCommand(source.Id, true), default);
        Assert.True(source.IsActive);

        Assert.Equal([AuditAction.DataSourceDeactivated, AuditAction.DataSourceActivated], rig.Audit.Entries.Select(e => e.Action));
        Assert.Equal(2, rig.Catalog.Invalidated.Count);
    }

    [Fact]
    public void Audit_actions_are_appended_after_the_existing_values()
    {
        Assert.Equal(19, (int)AuditAction.DashboardUnshared);
        Assert.Equal(20, (int)AuditAction.DataSourceCreated);
        Assert.Equal(21, (int)AuditAction.DataSourceUpdated);
        Assert.Equal(22, (int)AuditAction.DataSourceActivated);
        Assert.Equal(23, (int)AuditAction.DataSourceDeactivated);
    }

    // ---- validation ---------------------------------------------------------------------------------------

    private static async Task<List<string>> Problems(Rig rig, CreateDataSourceCommand command) =>
        (await new CreateDataSourceCommandValidator(rig.Repo).ValidateAsync(command)).Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList();

    private static CreateDataSourceCommand Create(string? name = "Warehouse", string? description = null, ConnectionInput? connection = null,
        DataSourceProvider provider = DataSourceProvider.PostgreSql) => new(name, description, provider, connection ?? Input());

    [Fact]
    public async Task A_valid_command_passes() => Assert.Empty(await Problems(new Rig(), Create()));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Name_is_required(string? name) => Assert.NotEmpty(await Problems(new Rig(), Create(name)));

    [Fact]
    public async Task Name_is_limited_to_200_characters()
    {
        Assert.Empty(await Problems(new Rig(), Create(new string('a', 200))));
        Assert.NotEmpty(await Problems(new Rig(), Create(new string('a', 201))));
    }

    [Fact]
    public async Task Description_is_limited_to_1000_characters()
    {
        Assert.Empty(await Problems(new Rig(), Create(description: new string('a', 1000))));
        Assert.NotEmpty(await Problems(new Rig(), Create(description: new string('a', 1001))));
    }

    [Theory]
    [InlineData(DataSourceProvider.MySql)]
    [InlineData(DataSourceProvider.Sqlite)]
    [InlineData((DataSourceProvider)99)]
    public async Task Only_sql_server_and_postgres_are_accepted(DataSourceProvider provider) =>
        Assert.NotEmpty(await Problems(new Rig(), Create(provider: provider)));

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("tab\there")]
    [InlineData("new\nline")]
    [InlineData("a,b")]
    [InlineData("x;Password=y")]
    public async Task Bad_hosts_are_refused(string host) =>
        Assert.NotEmpty(await Problems(new Rig(), Create(connection: Input(host: host))));

    [Fact]
    public async Task Host_is_limited_to_255_characters()
    {
        Assert.Empty(await Problems(new Rig(), Create(connection: Input(host: new string('h', 255)))));
        Assert.NotEmpty(await Problems(new Rig(), Create(connection: Input(host: new string('h', 256)))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task Port_must_be_1_to_65535(int port) => Assert.NotEmpty(await Problems(new Rig(), Create(connection: Input(port: port))));

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public async Task Port_edges_are_accepted(int port) => Assert.Empty(await Problems(new Rig(), Create(connection: Input(port: port))));

    [Fact]
    public async Task Database_username_and_password_limits()
    {
        Assert.NotEmpty(await Problems(new Rig(), Create(connection: Input(database: ""))));
        Assert.NotEmpty(await Problems(new Rig(), Create(connection: Input(database: new string('d', 129)))));
        Assert.NotEmpty(await Problems(new Rig(), Create(connection: Input(username: ""))));
        Assert.NotEmpty(await Problems(new Rig(), Create(connection: Input(username: new string('u', 129)))));
        Assert.Empty(await Problems(new Rig(), Create(connection: Input(password: new string('p', 5000)))));
        Assert.NotEmpty(await Problems(new Rig(), Create(connection: Input(password: new string('p', 5001)))));
    }

    [Fact]
    public async Task Unknown_ssl_mode_is_refused() =>
        Assert.NotEmpty(await Problems(new Rig(), Create(connection: Input(ssl: (ConnectionSslMode)42))));

    [Fact]
    public async Task Missing_connection_is_refused() =>
        Assert.NotEmpty((await new CreateDataSourceCommandValidator(new MemRepo()).ValidateAsync(new CreateDataSourceCommand("A", null, DataSourceProvider.PostgreSql, null))).Errors);

    [Theory]
    [InlineData("Warehouse")]
    [InlineData("WAREHOUSE")]
    [InlineData("  warehouse  ")]
    public async Task Duplicate_names_are_refused_ignoring_case_and_the_message_names_the_conflict(string name)
    {
        var rig = new Rig();
        rig.AddManaged("Warehouse");

        var problems = await Problems(rig, Create(name));

        Assert.Contains(problems, p => p.Contains("already exists") && p.Contains(name.Trim()));
    }

    [Fact]
    public async Task Edit_may_keep_its_own_name_but_not_take_another_sources()
    {
        var rig = new Rig();
        var mine = rig.AddManaged("Warehouse");
        rig.AddManaged("Reports");
        var validator = new UpdateDataSourceCommandValidator(rig.Repo);

        Assert.True((await validator.ValidateAsync(new UpdateDataSourceCommand(mine.Id, "warehouse", null, null))).IsValid);
        Assert.False((await validator.ValidateAsync(new UpdateDataSourceCommand(mine.Id, "REPORTS", null, null))).IsValid);
    }

    [Fact]
    public async Task Test_command_validates_the_connection_and_provider()
    {
        var validator = new TestConnectionCommandValidator();

        Assert.True((await validator.ValidateAsync(new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(), null))).IsValid);
        Assert.False((await validator.ValidateAsync(new TestConnectionCommand(DataSourceProvider.MySql, Input(), null))).IsValid);
        Assert.False((await validator.ValidateAsync(new TestConnectionCommand(DataSourceProvider.PostgreSql, Input(host: ""), null))).IsValid);
    }

    // ---- detail query -------------------------------------------------------------------------------------

    private static GetAdminDataSourceDetailQueryHandler DetailHandler(Rig rig, CatalogStub? catalog = null, IConnectionSecretProtector? protector = null) =>
        new(rig.Repo, catalog ?? rig.Catalog, new RecordLimitSettings(null), protector ?? rig.Protector);

    [Fact]
    public async Task Detail_returns_the_connection_without_the_password_and_without_the_sentinel()
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        var dto = await DetailHandler(rig).Handle(new GetAdminDataSourceDetailQuery(source.Id), default);

        Assert.True(dto.IsManaged);
        Assert.False(dto.ConnectionUnreadable);
        Assert.Null(dto.ConnectionStringName);
        Assert.Equal("db.example.com", dto.Connection!.Host);
        Assert.True(dto.Connection.HasPassword);
        var json = JsonSerializer.Serialize(dto, CamelJson);
        Assert.DoesNotContain(Secret, json);
        Assert.DoesNotContain(DataSource.ManagedConnectionSentinel, json);
        Assert.DoesNotContain(source.EncryptedConnection!, json);
        Assert.DoesNotContain("\"password\"", json);
    }

    [Fact]
    public async Task Detail_with_an_unreadable_blob_flags_it_instead_of_failing()
    {
        var rig = new Rig();
        var source = rig.AddManaged();

        var dto = await DetailHandler(rig, protector: Protector(9)).Handle(new GetAdminDataSourceDetailQuery(source.Id), default);

        Assert.True(dto.ConnectionUnreadable);
        Assert.Null(dto.Connection);
        Assert.True(dto.IsManaged);
    }

    [Fact]
    public async Task Detail_of_an_unreachable_source_still_opens_with_the_reason()
    {
        var rig = new Rig();
        var source = rig.AddManaged();
        var catalog = new CatalogStub { RawFailure = new QueryExecutionException("Couldn't connect to this data source.") };

        var dto = await DetailHandler(rig, catalog).Handle(new GetAdminDataSourceDetailQuery(source.Id), default);

        Assert.Equal("Couldn't connect to this data source.", dto.CatalogError);
        Assert.Empty(dto.Objects);
    }

    [Fact]
    public async Task Detail_of_a_config_named_source_shows_the_name_but_no_connection()
    {
        var rig = new Rig();
        var source = rig.AddConfigNamed();
        source.IsActive = false;

        var dto = await DetailHandler(rig).Handle(new GetAdminDataSourceDetailQuery(source.Id), default);

        Assert.False(dto.IsManaged);
        Assert.False(dto.IsActive);
        Assert.Equal("legacy", dto.ConnectionStringName);
        Assert.Null(dto.Connection);
        Assert.False(dto.ConnectionUnreadable);
    }

    // ---- deactivation -------------------------------------------------------------------------------------

    private static DataCatalogService CatalogServiceFor(DataSourceCatalog catalog)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"catalog:{TestData.DataSourceId}", catalog);
        return new DataCatalogService(new ActiveDataSources(), null!, [], cache, NullLogger<DataCatalogService>.Instance);
    }

    private static DataCatalogService CatalogServiceFor(DataSourceCatalog catalog, bool active = true)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"catalog:{TestData.DataSourceId}", catalog);
        return new DataCatalogService(new ActiveDataSources(active), null!, [], cache, NullLogger<DataCatalogService>.Instance);
    }

    [Fact]
    public async Task Validate_refuses_an_inactive_source_even_when_a_catalog_is_cached()
    {
        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            CatalogServiceFor(TestData.Catalog(), active: false).ValidateAsync(TestData.DataSourceId, TestData.Definition(), default));

        Assert.Equal("This data source is turned off.", ex.Message);
    }

    [Fact]
    public async Task Validate_still_accepts_an_active_source() =>
        await CatalogServiceFor(TestData.Catalog()).ValidateAsync(TestData.DataSourceId, TestData.Definition(), default);

    private sealed class ExplodingResolver : IConnectionStringResolver
    {
        public int Calls { get; private set; }
        public string Resolve(DataSource dataSource) { Calls++; throw new InvalidOperationException("must not connect"); }
    }

    [Fact]
    public async Task An_inactive_source_makes_no_connection_on_a_cache_miss()
    {
        var resolver = new ExplodingResolver();
        var service = new DataCatalogService(new ActiveDataSources(false), resolver, [], new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DataCatalogService>.Instance);

        await Assert.ThrowsAsync<CatalogValidationException>(() => service.ValidateAsync(TestData.DataSourceId, TestData.Definition(), default));
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            new GetCatalogQueryHandler(service, new PassThroughGuard(), new ActiveDataSources(false)).Handle(new GetCatalogQuery(TestData.DataSourceId), default));

        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task Catalog_query_refuses_an_inactive_source_but_the_service_still_returns_it_for_admin_pages()
    {
        var catalog = TestData.Catalog();
        var service = CatalogServiceFor(catalog, active: false);

        Assert.Same(catalog, await service.GetCatalogAsync(TestData.DataSourceId, default));
        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            new GetCatalogQueryHandler(service, new PassThroughGuard(), new ActiveDataSources(false)).Handle(new GetCatalogQuery(TestData.DataSourceId), default));
        Assert.Equal("This data source is turned off.", ex.Message);
    }

    [Fact]
    public async Task Turned_off_is_read_fresh_so_a_cached_catalog_cannot_outlive_it()
    {
        var catalog = TestData.Catalog();
        var service = CatalogServiceFor(catalog);
        var handlerOn = new GetCatalogQueryHandler(service, new PassThroughGuard(), new ActiveDataSources(true));
        var handlerOff = new GetCatalogQueryHandler(service, new PassThroughGuard(), new ActiveDataSources(false));

        Assert.Same(catalog, await handlerOn.Handle(new GetCatalogQuery(TestData.DataSourceId), default));
        await Assert.ThrowsAsync<CatalogValidationException>(() => handlerOff.Handle(new GetCatalogQuery(TestData.DataSourceId), default));
    }

    [Fact]
    public async Task App_users_of_an_inactive_source_are_unavailable_without_connecting()
    {
        var resolver = new ExplodingResolver();
        var directory = new AppUsersDirectory(new ActiveDataSources(false), resolver, NullLogger<AppUsersDirectory>.Instance);

        var result = await directory.GetUsersAsync(TestData.DataSourceId, default);

        Assert.False(result.Available);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task Business_picker_lists_only_active_sources()
    {
        var rig = new Rig();
        rig.AddManaged("On");
        rig.AddManaged("Off").IsActive = false;

        var list = await new GetDataSourcesQueryHandler(rig.Repo).Handle(new GetDataSourcesQuery(), default);

        Assert.Equal(["On"], list.Select(d => d.Name));
    }

    private sealed class PassThroughGuard : IDataScopeGuard
    {
        public Task<DataSourceCatalog> FilterCatalogAsync(DataSourceCatalog catalog, CancellationToken cancellationToken) => Task.FromResult(catalog);
        public Task<IReadOnlyList<ScopePredicate>> AuthorizeAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePredicate>>([]);
    }
}
