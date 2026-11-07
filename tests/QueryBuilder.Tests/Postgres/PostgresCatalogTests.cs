using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Catalog;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Tests.Postgres;

public class PostgresCatalogTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
{
    private static readonly Guid Id = Guid.Parse("c0ffee00-0000-0000-0000-000000000005");

    private DataCatalogService Service(DataSource dataSource, string? connectionString = null, ILogger<DataCatalogService>? logger = null)
    {
        var resolved = connectionString ?? database.ReaderConnectionString;
        return new DataCatalogService(
            new Repo(dataSource), new Resolver(resolved),
            [new SqlServerCatalogReader(), new PostgresCatalogReader(NullLogger<PostgresCatalogReader>.Instance)],
            new MemoryCache(new MemoryCacheOptions()), logger ?? NullLogger<DataCatalogService>.Instance);
    }

    private static DataSource Source(DataSourceProvider provider = DataSourceProvider.PostgreSql) => new()
    {
        Id = Id, Name = "pg", Provider = provider, CatalogScope = CatalogScope.TablesAndViews
    };

    private static SchemaObjectMetadata Find(DataSourceCatalog catalog, string schema, string name) =>
        catalog.Schemas.Single(s => s.Name == schema).Objects.Single(o => o.Name == name);

    private static bool Has(DataSourceCatalog catalog, string schema, string name) =>
        catalog.Schemas.Any(s => s.Name == schema && s.Objects.Any(o => o.Name == name));

    [PostgresFact]
    public async Task Reads_kinds_and_provider()
    {
        var catalog = await Service(Source()).GetCatalogAsync(Id, default);

        Assert.Equal(DataSourceProvider.PostgreSql, catalog.Provider);
        Assert.Equal(SchemaObjectKind.Table, Find(catalog, "qb", "t").Kind);
        Assert.Equal(SchemaObjectKind.Table, Find(catalog, "qb", "part").Kind);
        Assert.Equal(SchemaObjectKind.Table, Find(catalog, "qb", "ft").Kind);
        Assert.Equal(SchemaObjectKind.View, Find(catalog, "qb", "v").Kind);
        Assert.Equal(SchemaObjectKind.View, Find(catalog, "qb", "mv").Kind);
        Assert.Equal(SchemaObjectKind.View, Find(catalog, "qb", "MixedMv").Kind);
    }

    [PostgresFact]
    public async Task Hides_partition_children_ungranted_and_system_objects()
    {
        var catalog = await Service(Source()).GetCatalogAsync(Id, default);

        Assert.False(Has(catalog, "qb", "part_a"));
        Assert.False(Has(catalog, "qb", "secret"));
        Assert.False(Has(catalog, "qb", "colgrant"));
        Assert.DoesNotContain(catalog.Schemas, s => s.Name is "information_schema" or "pg_catalog" or "pg_toast");
    }

    [PostgresFact]
    public async Task Maps_columns_domains_keys_and_drops_dropped_columns()
    {
        var catalog = await Service(Source()).GetCatalogAsync(Id, default);
        var t = Find(catalog, "qb", "t");
        ColumnMetadata Column(string name) => t.Columns.Single(c => c.Name == name);

        Assert.DoesNotContain(t.Columns, c => c.Name == "dropped");
        Assert.Equal(ColumnDataType.Number, Column("c_domain").DataType);
        Assert.Equal("numeric", Column("c_domain").SqlType);
        Assert.Equal(ColumnDataType.Text, Column("c_mood").DataType);
        Assert.Equal(ColumnDataType.Guid, Column("c_uuid").DataType);
        Assert.Equal(ColumnDataType.DateTime, Column("c_timestamptz").DataType);
        Assert.Equal(ColumnDataType.Unknown, Column("c_money").DataType);
        Assert.Equal(ColumnDataType.Unknown, Column("c_array").DataType);
        Assert.True(Column("id").IsPrimaryKey);
        Assert.False(Column("id").IsNullable);
        Assert.True(Column("c_text").IsNullable);
        var fk = Column("parent_id");
        Assert.True(fk.IsForeignKey);
        Assert.Equal("parent", fk.ForeignKeyTable);
        Assert.Equal("id", fk.ForeignKeyColumn);
        Assert.Equal(t.Columns.Select(c => c.OrdinalPosition).Order(), t.Columns.Select(c => c.OrdinalPosition));
    }

    [PostgresFact]
    public async Task Flags_nondeterministic_collations_on_columns_domains_and_views()
    {
        var catalog = await Service(Source()).GetCatalogAsync(Id, default);
        var t = Find(catalog, "qb", "t");
        var vci = Find(catalog, "qb", "v_ci");

        Assert.True(t.Columns.Single(c => c.Name == "c_ci").HasNondeterministicCollation);
        Assert.True(t.Columns.Single(c => c.Name == "c_ci_domain").HasNondeterministicCollation);
        Assert.True(vci.Columns.Single(c => c.Name == "shouty").HasNondeterministicCollation);
        Assert.False(t.Columns.Single(c => c.Name == "c_text").HasNondeterministicCollation);
        Assert.False(t.Columns.Single(c => c.Name == "id").HasNondeterministicCollation);
        Assert.False(vci.Columns.Single(c => c.Name == "c_text").HasNondeterministicCollation);
    }

    [PostgresFact]
    public async Task Name_clashes_are_unavailable_not_in_schemas()
    {
        var service = Service(Source());
        var catalog = await service.GetCatalogAsync(Id, default);

        Assert.False(Has(catalog, "public", "Orders"));
        Assert.False(Has(catalog, "public", "orders"));
        Assert.False(Has(catalog, "a.b", "c"));
        Assert.False(Has(catalog, "a", "b.c"));
        Assert.Equal(
            ["a.b.c", "a.b.c", "public.Orders", "public.orders"],
            catalog.UnavailableObjects.Select(u => $"{u.SchemaName}.{u.Name}").Order(StringComparer.Ordinal));

        var admin = await service.GetUnavailableObjectsAsync(Id, default);
        Assert.Equal(["public.orders"], admin.Single(u => u.Name == "Orders").ClashesWith);
    }

    [PostgresFact]
    public async Task MixedMv_is_readable_and_validates_by_exact_name()
    {
        var service = Service(Source());
        var definition = new QueryDefinition
        {
            Source = new QuerySource { SchemaName = "qb", ObjectName = "MixedMv", Alias = "m" },
            Columns = [new QueryColumn { TableAlias = "m", ColumnName = "id" }]
        };

        await service.ValidateAsync(Id, definition, default);

        definition.Source.ObjectName = "mixedmv";
        await Assert.ThrowsAsync<CatalogValidationException>(() => service.ValidateAsync(Id, definition, default));
    }

    [PostgresFact]
    public async Task Allowlist_removes_unavailable_objects_from_catalog_but_not_from_admin_list()
    {
        var dataSource = Source();
        dataSource.AllowedObjects = ["qb.v"];
        var service = Service(dataSource);

        var catalog = await service.GetCatalogAsync(Id, default);
        var admin = await service.GetUnavailableObjectsAsync(Id, default);

        Assert.Equal(["qb.v"], catalog.Schemas.SelectMany(s => s.Objects).Select(o => $"{o.SchemaName}.{o.Name}"));
        Assert.DoesNotContain(catalog.UnavailableObjects, u => u.Name == "Orders");
        Assert.Contains(admin, u => u is { SchemaName: "public", Name: "Orders" });
    }

    [PostgresFact]
    public async Task Clash_survivor_stays_out_when_the_allowlist_names_only_it()
    {
        var dataSource = Source();
        dataSource.AllowedObjects = ["public.orders"];

        var catalog = await Service(dataSource).GetCatalogAsync(Id, default);

        Assert.Empty(catalog.Schemas);
        Assert.Equal(["public.orders"], catalog.UnavailableObjects.Select(u => $"{u.SchemaName}.{u.Name}"));
    }

    [PostgresFact]
    public async Task Clash_survivor_stays_out_when_allowed_schemas_names_only_it()
    {
        var dataSource = Source();
        dataSource.AllowedSchemas = ["a"];

        var catalog = await Service(dataSource).GetCatalogAsync(Id, default);

        Assert.Empty(catalog.Schemas);
        var unavailable = Assert.Single(catalog.UnavailableObjects);
        Assert.Equal(("a", "b.c"), (unavailable.SchemaName, unavailable.Name));
    }

    [PostgresFact]
    public async Task Kind_filter_applies_to_unavailable_objects_too()
    {
        var dataSource = Source();
        dataSource.CatalogScope = CatalogScope.Views;
        var service = Service(dataSource);

        var catalog = await service.GetCatalogAsync(Id, default);

        Assert.All(catalog.Schemas.SelectMany(s => s.Objects), o => Assert.Equal(SchemaObjectKind.View, o.Kind));
        Assert.Empty(catalog.UnavailableObjects);
        Assert.NotEmpty(await service.GetUnavailableObjectsAsync(Id, default));
    }

    [PostgresFact]
    public async Task Logs_one_warning_per_clash_naming_schema_and_object()
    {
        var logger = new CapturingLogger<DataCatalogService>();

        await Service(Source(), logger: logger).GetCatalogAsync(Id, default);

        Assert.Equal(2, logger.Warnings.Count);
        Assert.Contains(logger.Warnings, w => w.Contains("\"a.b\".\"c\"") && w.Contains("\"a\".\"b.c\""));
        Assert.Contains(logger.Warnings, w => w.Contains("public.Orders") && w.Contains("public.orders"));
    }

    [PostgresFact]
    public async Task Raw_objects_are_usable_only()
    {
        var raw = await Service(Source()).GetRawObjectsAsync(Id, default);

        Assert.Contains(raw, o => o is { SchemaName: "qb", Name: "t" });
        Assert.DoesNotContain(raw, o => o.Name is "Orders" or "orders" or "c" or "b.c");
        Assert.All(raw, o => Assert.Empty(o.Columns));
    }

    [Fact]
    public async Task Connection_failure_is_wrapped_without_details()
    {
        var unreachable = "Host=127.0.0.1;Port=1;Username=u;Database=d;Timeout=2;Pooling=false";

        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            Service(Source(), unreachable).GetCatalogAsync(Id, default));

        Assert.Equal("Couldn't connect to this data source.", ex.Message);
    }

    [Theory]
    [InlineData("Server=db;Database=d;Trusted_Connection=True")]
    [InlineData("Host=127.0.0.1;Port=notaport")]
    [InlineData("Username=u;Database=d")]
    [InlineData("")]
    public async Task Malformed_connection_string_is_wrapped_without_details(string connectionString)
    {
        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            Service(Source(), connectionString).GetCatalogAsync(Id, default));

        Assert.Equal("Couldn't connect to this data source.", ex.Message);
    }

    [Fact]
    public async Task Cancellation_is_not_wrapped()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PostgresCatalogReader(NullLogger<PostgresCatalogReader>.Instance)
                .ReadObjectsAsync("Host=127.0.0.1;Port=1;Username=u;Database=d;Timeout=2;Pooling=false", cts.Token));
    }

    [PostgresFact]
    public async Task Rejected_query_surfaces_as_query_execution_exception()
    {
        // A 1 ms statement timeout would also kill Npgsql's own type-loading queries while opening, so skip those
        // and let the catalog query be the first thing the server refuses.
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(database.ReaderConnectionString) { Options = "-c statement_timeout=1", ServerCompatibilityMode = Npgsql.ServerCompatibilityMode.NoTypeLoading };

        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            new PostgresCatalogReader(NullLogger<PostgresCatalogReader>.Instance)
                .ReadColumnsAsync(builder.ConnectionString, default));

        Assert.StartsWith("The database rejected the query: ", ex.Message);
    }

    [Fact]
    public async Task Unknown_provider_names_the_provider()
    {
        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            Service(Source(DataSourceProvider.MySql), "unused").GetCatalogAsync(Id, default));

        Assert.Contains("MySql", ex.Message);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    private sealed class Resolver(string connectionString) : IConnectionStringResolver
    {
        public string Resolve(DataSource dataSource) => connectionString;
    }

    private sealed class Repo(DataSource dataSource) : IDataSourceRepository
    {
        public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<DataSource?>(dataSource);
        public Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(DataSource dataSource, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void UpdateOnly(DataSource dataSource, params string[] propertyNames) { }
        public void Update(DataSource ds) { }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
