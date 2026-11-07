using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Commands.Admin;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataScoping;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Catalog;

namespace QueryBuilder.Tests;

public class IdentifierMatchingTests
{
    private static readonly string[] Keys = ["ProgramId"];

    private static ColumnMetadata Col(string name, bool nondeterministic = false, ColumnDataType type = ColumnDataType.Text) =>
        new() { Name = name, DataType = type, HasNondeterministicCollation = nondeterministic };

    private static SchemaObjectMetadata Obj(string schema, string name, params ColumnMetadata[] columns) =>
        new() { SchemaName = schema, Name = name, Kind = SchemaObjectKind.Table, Columns = [.. columns] };

    private static DataSourceCatalog PgCatalog(
        IEnumerable<SchemaObjectMetadata> objects, IEnumerable<UnavailableObject>? unavailable = null) => new()
    {
        DataSourceId = TestData.DataSourceId,
        Provider = DataSourceProvider.PostgreSql,
        Schemas = [new SchemaMetadata { Name = "public", Objects = [.. objects] }],
        UnavailableObjects = [.. unavailable ?? []]
    };

    private static UnavailableObject Unavail(string schema, string name) =>
        new() { SchemaName = schema, Name = name, Kind = SchemaObjectKind.Table, Reason = UnavailableReason.NameClash };

    private static AdminUnavailableObject AdminUnavail(string schema, string name, params string[] clashes) =>
        new() { SchemaName = schema, Name = name, Kind = SchemaObjectKind.Table, Reason = UnavailableReason.NameClash, ClashesWith = [.. clashes] };

    [Fact]
    public void Comparison_is_exact_for_postgres_and_ignores_case_for_others()
    {
        Assert.Equal(StringComparison.Ordinal, IdentifierMatching.Comparison(DataSourceProvider.PostgreSql));
        Assert.Equal(StringComparison.OrdinalIgnoreCase, IdentifierMatching.Comparison(DataSourceProvider.SqlServer));
        Assert.Equal(StringComparison.OrdinalIgnoreCase, IdentifierMatching.Comparison(DataSourceProvider.MySql));
        Assert.Same(StringComparer.Ordinal, IdentifierMatching.Comparer(DataSourceProvider.PostgreSql));
        Assert.Same(StringComparer.OrdinalIgnoreCase, IdentifierMatching.Comparer(DataSourceProvider.SqlServer));
    }

    [Fact]
    public void Evaluate_postgres_rule_on_Orders_does_not_apply_to_orders()
    {
        var access = DataScopeEvaluator.Evaluate(
            "public.orders", [TestData.Rule("public.Orders")], TestData.Scoped(("ProgramId", ["10"])), Keys, StringComparison.Ordinal);

        Assert.Same(ObjectScopeAccess.Hidden, access);
    }

    [Fact]
    public void Evaluate_sql_server_still_ignores_case()
    {
        var access = DataScopeEvaluator.Evaluate(
            "sales.VW_ORDERS", [TestData.Rule("sales.vw_Orders")], TestData.Scoped(("ProgramId", ["10"])), Keys,
            IdentifierMatching.Comparison(DataSourceProvider.SqlServer));

        Assert.Same(ObjectScopeAccess.Unfiltered, access);
    }

    private static DataScopeGuard Guard(DataSourceCatalog catalog, ResolvedDataScope scope, params DataScopeRule[] rules) =>
        new(new FakeScope(scope), new FakeRules(rules), new FakeCatalog(catalog));

    private static QueryDefinition PublicDefinition(string objectName) => new()
    {
        Source = new QuerySource { SchemaName = "public", ObjectName = objectName, Alias = "t0" },
        Columns = [new QueryColumn { TableAlias = "t0", ColumnName = "Id" }]
    };

    [Fact]
    public async Task Guard_postgres_filters_on_exact_column_when_case_pair_exists()
    {
        var catalog = PgCatalog([Obj("public", "Orders", Col("Id"), Col("customerid"), Col("CustomerId"))]);
        var guard = Guard(catalog, TestData.Scoped(("ProgramId", ["10"])),
            TestData.Rule("public.Orders", "ProgramId", "CustomerId"));

        var predicates = await guard.AuthorizeAsync(TestData.DataSourceId, PublicDefinition("Orders"), default);

        Assert.Equal("CustomerId", Assert.Single(predicates).ColumnName);
    }

    [Fact]
    public async Task Guard_postgres_hides_object_when_only_a_differently_cased_column_exists()
    {
        var catalog = PgCatalog([Obj("public", "Orders", Col("Id"), Col("customerid"))]);
        var guard = Guard(catalog, TestData.Scoped(("ProgramId", ["10"])),
            TestData.Rule("public.Orders", "ProgramId", "CustomerId"));

        Assert.Empty((await guard.FilterCatalogAsync(catalog, default)).Schemas);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            guard.AuthorizeAsync(TestData.DataSourceId, PublicDefinition("Orders"), default));
    }

    [Fact]
    public async Task Guard_postgres_does_not_find_object_by_wrong_case_name()
    {
        var catalog = PgCatalog([Obj("public", "Orders", Col("Id"), Col("CustomerId"))]);
        var guard = Guard(catalog, TestData.Scoped(("ProgramId", ["10"])),
            TestData.Rule("public.Orders", "ProgramId", "CustomerId"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            guard.AuthorizeAsync(TestData.DataSourceId, PublicDefinition("orders"), default));
    }

    [Fact]
    public async Task Guard_hides_object_when_scope_column_is_nondeterministic()
    {
        var catalog = PgCatalog([
            Obj("public", "Tenants", Col("Id"), Col("tenant", nondeterministic: true)),
            Obj("public", "Orders", Col("Id"), Col("tenant"))]);
        var guard = Guard(catalog, TestData.Scoped(("ProgramId", ["acme"])),
            TestData.Rule("public.Tenants", "ProgramId", "tenant"),
            TestData.Rule("public.Orders", "ProgramId", "tenant"));

        var filtered = await guard.FilterCatalogAsync(catalog, default);

        Assert.Equal(["Orders"], filtered.Schemas.SelectMany(s => s.Objects).Select(o => o.Name));
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            guard.AuthorizeAsync(TestData.DataSourceId, PublicDefinition("Tenants"), default));
        Assert.Equal("You don't have access to 'public.Tenants'.", ex.Message);
    }

    [Fact]
    public async Task FilterCatalog_scoped_user_gets_no_unavailable_objects_and_keeps_provider()
    {
        var catalog = PgCatalog([Obj("public", "Items", Col("Id"), Col("ProgramId"))], [Unavail("public", "Orders")]);
        var guard = Guard(catalog, TestData.Scoped(("ProgramId", ["10"])),
            TestData.Rule("public.Items", "ProgramId", "ProgramId"));

        var filtered = await guard.FilterCatalogAsync(catalog, default);

        Assert.Empty(filtered.UnavailableObjects);
        Assert.Equal(DataSourceProvider.PostgreSql, filtered.Provider);
        Assert.Single(catalog.UnavailableObjects);
    }

    [Fact]
    public async Task FilterCatalog_unrestricted_user_gets_unavailable_objects()
    {
        var catalog = PgCatalog([Obj("public", "Items", Col("Id"))], [Unavail("public", "Orders")]);
        var guard = Guard(catalog, ResolvedDataScope.Unrestricted);

        var filtered = await guard.FilterCatalogAsync(catalog, default);

        Assert.Equal("Orders", Assert.Single(filtered.UnavailableObjects).Name);
    }

    [Fact]
    public async Task Catalog_json_never_names_a_foreign_key_target()
    {
        var items = Obj("public", "Items", Col("Id"), Col("ProgramId"));
        items.Columns[0].IsForeignKey = true;
        items.Columns[0].ForeignKeyTable = "public.Hidden_Orders";
        items.Columns[0].ForeignKeyColumn = "HiddenSecretColumn";
        var catalog = PgCatalog([items], [Unavail("public", "Hidden_Orders")]);
        var guard = Guard(catalog, TestData.Scoped(("ProgramId", ["10"])), TestData.Rule("public.Items", "ProgramId", "ProgramId"));

        var filtered = await guard.FilterCatalogAsync(catalog, default);
        var json = System.Text.Json.JsonSerializer.Serialize(filtered, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
        });

        Assert.Contains("\"isForeignKey\":true", json);
        Assert.DoesNotContain("Hidden_Orders", json);
        Assert.DoesNotContain("HiddenSecretColumn", json);
        Assert.DoesNotContain("foreignKeyTable", json);
    }

    private static DataCatalogService CatalogServiceFor(DataSourceCatalog catalog)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"catalog:{TestData.DataSourceId}", catalog);
        return new DataCatalogService(new ActiveDataSources(), null!, [], cache, NullLogger<DataCatalogService>.Instance);
    }

    private static QueryDefinition Split(string schema, string name) => new()
    {
        Source = new QuerySource { SchemaName = schema, ObjectName = name, Alias = "t0" },
        Columns = [new QueryColumn { TableAlias = "t0", ColumnName = "Id" }]
    };

    [Fact]
    public async Task Validate_refuses_a_different_split_of_the_same_joined_name_on_postgres()
    {
        var service = CatalogServiceFor(new DataSourceCatalog
        {
            DataSourceId = TestData.DataSourceId,
            Provider = DataSourceProvider.PostgreSql,
            Schemas = [new SchemaMetadata { Name = "a.b", Objects = [Obj("a.b", "c", Col("Id"))] }]
        });

        await service.ValidateAsync(TestData.DataSourceId, Split("a.b", "c"), default);
        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            service.ValidateAsync(TestData.DataSourceId, Split("a", "b.c"), default));
        Assert.Equal("'a.b.c' was not found in the data source catalog.", ex.Message);
    }

    [Fact]
    public async Task Validate_refuses_a_different_split_of_the_same_joined_name_on_sql_server()
    {
        var service = CatalogServiceFor(new DataSourceCatalog
        {
            DataSourceId = TestData.DataSourceId,
            Provider = DataSourceProvider.SqlServer,
            Schemas = [new SchemaMetadata { Name = "a.b", Objects = [Obj("a.b", "c", Col("Id"))] }]
        });

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            service.ValidateAsync(TestData.DataSourceId, Split("a", "b.c"), default));
    }

    [Fact]
    public async Task Validate_on_sql_server_still_accepts_names_that_differ_only_in_case()
    {
        var service = CatalogServiceFor(new DataSourceCatalog
        {
            DataSourceId = TestData.DataSourceId,
            Provider = DataSourceProvider.SqlServer,
            Schemas = [new SchemaMetadata { Name = "Sales", Objects = [Obj("Sales", "Orders", Col("Id"))] }]
        });

        await service.ValidateAsync(TestData.DataSourceId, Split("SALES", "orders"), default);
    }

    [Fact]
    public async Task Validate_refuses_unavailable_object_with_not_found_message()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"catalog:{TestData.DataSourceId}",
            PgCatalog([Obj("public", "Items", Col("Id"))], [Unavail("public", "Orders")]));
        var service = new DataCatalogService(new ActiveDataSources(), null!, [], cache, NullLogger<DataCatalogService>.Instance);

        var unavailable = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            service.ValidateAsync(TestData.DataSourceId, PublicDefinition("Orders"), default));
        var missing = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            service.ValidateAsync(TestData.DataSourceId, PublicDefinition("Nowhere"), default));

        Assert.Equal("'public.Orders' was not found in the data source catalog.", unavailable.Message);
        Assert.Equal(missing.Message.Replace("Nowhere", "Orders"), unavailable.Message);
    }

    private sealed class Harness
    {
        public required DataSourceCatalog Catalog { get; init; }
        public List<AdminUnavailableObject> Unavailable { get; init; } = [];
        public List<DataScopeRule> ExistingRules { get; init; } = [];
        public List<string> AllowedObjects { get; init; } = [];
        public List<SchemaObjectMetadata> RawObjects { get; init; } = [];
        public IReadOnlyList<DataScopeRule>? Replaced { get; set; }
        public DataSource DataSource { get; } = new() { Id = TestData.DataSourceId, Name = "pg", Provider = DataSourceProvider.PostgreSql };

        public UpdateDataScopeCommandHandler ScopeHandler() => new(
            new Repo(DataSource), new FakeCatalog(Catalog, Unavailable, RawObjects),
            new FakeRules(ExistingRules, r => Replaced = r), new FakeScope(ResolvedDataScope.Unrestricted),
            new VizFixture.FakeUser { UserId = "admin" }, new VizFixture.FakeAudit());

        public UpdateCatalogPolicyCommandHandler PolicyHandler()
        {
            DataSource.AllowedObjects = AllowedObjects;
            return new(new Repo(DataSource), new FakeCatalog(Catalog, Unavailable, RawObjects),
                new VizFixture.FakeUser { UserId = "admin" }, new VizFixture.FakeAudit());
        }
    }

    private static Harness PgHarness(
        IEnumerable<SchemaObjectMetadata> usable, IEnumerable<AdminUnavailableObject>? unavailable = null) => new()
    {
        Catalog = PgCatalog(usable),
        Unavailable = [.. unavailable ?? []],
        RawObjects = [.. usable]
    };

    private static UpdateDataScopeCommand ScopeCommand(string objectName, DataScopeState state, Dictionary<string, string>? mappings) =>
        new(TestData.DataSourceId, [new DataScopeObjectRuleDto(objectName, state, mappings)]);

    [Fact]
    public async Task UpdateDataScope_refuses_nondeterministic_scope_column()
    {
        var h = PgHarness([Obj("public", "Tenants", Col("Id"), Col("tenant", nondeterministic: true))]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => h.ScopeHandler().Handle(
            ScopeCommand("public.Tenants", DataScopeState.Scoped, new() { ["ProgramId"] = "tenant" }), default));

        Assert.Contains("tenant", ex.Message);
        Assert.Null(h.Replaced);
    }

    [Fact]
    public async Task UpdateDataScope_matches_columns_exactly_on_postgres()
    {
        var h = PgHarness([Obj("public", "Orders", Col("Id"), Col("customerid"), Col("CustomerId"))]);

        await h.ScopeHandler().Handle(
            ScopeCommand("public.Orders", DataScopeState.Scoped, new() { ["ProgramId"] = "CustomerId" }), default);

        Assert.Equal("CustomerId", Assert.Single(h.Replaced!).ColumnName);
        await Assert.ThrowsAsync<CatalogValidationException>(() => PgHarness([Obj("public", "Orders", Col("customerid"))])
            .ScopeHandler().Handle(
                ScopeCommand("public.Orders", DataScopeState.Scoped, new() { ["ProgramId"] = "CustomerId" }), default));
    }

    [Fact]
    public async Task UpdateDataScope_carries_forward_rules_for_unavailable_objects()
    {
        var existing = TestData.Rule("public.Orders", "ProgramId", "ProgramId");
        existing.CreatedBy = "someone-else";
        var h = PgHarness([Obj("public", "Items", Col("Id"))], [AdminUnavail("public", "Orders", "public.orders")]);
        h.ExistingRules.Add(existing);
        h.ExistingRules.Add(TestData.Rule("public.Gone", "ProgramId", "ProgramId"));

        await h.ScopeHandler().Handle(ScopeCommand("public.Items", DataScopeState.NotScoped, null), default);

        Assert.Equal(["public.Items", "public.Orders"], h.Replaced!.Select(r => r.ObjectName).OrderBy(n => n).ToArray());
        var carried = h.Replaced!.Single(r => r.ObjectName == "public.Orders");
        Assert.NotSame(existing, carried);
        Assert.Equal("ProgramId", carried.ScopeKey);
        Assert.Equal("ProgramId", carried.ColumnName);
        Assert.Equal("someone-else", carried.CreatedBy);
    }

    [Fact]
    public async Task UpdateDataScope_does_not_duplicate_a_rule_when_a_stale_catalog_still_lists_the_clashing_object()
    {
        var catalog = PgCatalog([Obj("public", "Orders", Col("Id"))]);
        var h = new Harness { Catalog = catalog, Unavailable = [AdminUnavail("public", "Orders", "public.orders")] };
        h.ExistingRules.Add(TestData.Rule("public.Orders", "ProgramId", "ProgramId"));

        await h.ScopeHandler().Handle(ScopeCommand("public.Orders", DataScopeState.NotScoped, null), default);

        Assert.Equal(["public.Orders"], h.Replaced!.Select(r => r.ObjectName));
    }

    [Fact]
    public async Task Guard_postgres_keeps_orders_hidden_when_only_the_Orders_rule_exists()
    {
        var catalog = PgCatalog([Obj("public", "orders", Col("Id"))]);
        var guard = Guard(catalog, TestData.Scoped(("ProgramId", ["10"])), TestData.Rule("public.Orders"));

        Assert.Empty((await guard.FilterCatalogAsync(catalog, default)).Schemas);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            guard.AuthorizeAsync(TestData.DataSourceId, PublicDefinition("orders"), default));
    }

    [Fact]
    public async Task UpdateDataScope_does_not_accept_a_decision_on_an_unavailable_object()
    {
        var h = PgHarness([Obj("public", "Items", Col("Id"))], [AdminUnavail("public", "Orders", "public.orders")]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => h.ScopeHandler().Handle(
            ScopeCommand("public.Orders", DataScopeState.NotScoped, null), default));

        Assert.Contains("not in this data source's catalog", ex.Message);
    }

    [Fact]
    public async Task UpdateDataScope_postgres_treats_wrong_case_object_as_not_in_catalog()
    {
        var h = PgHarness([Obj("public", "Orders", Col("Id"))]);

        await Assert.ThrowsAsync<CatalogValidationException>(() => h.ScopeHandler().Handle(
            ScopeCommand("public.orders", DataScopeState.NotScoped, null), default));
    }

    [Fact]
    public async Task UpdateCatalogPolicy_keeps_existing_unavailable_allowlist_entry()
    {
        var h = PgHarness([Obj("public", "Items", Col("Id"))], [AdminUnavail("public", "Orders", "public.orders")]);
        h.AllowedObjects.Add("public.Orders");

        await h.PolicyHandler().Handle(
            new UpdateCatalogPolicyCommand(TestData.DataSourceId, CatalogScope.TablesAndViews, ["public.Items", "public.Orders"]), default);

        Assert.Equal(["public.Items", "public.Orders"], h.DataSource.AllowedObjects);
    }

    [Fact]
    public async Task UpdateCatalogPolicy_refuses_newly_added_unavailable_entry()
    {
        var h = PgHarness([Obj("public", "Items", Col("Id"))], [AdminUnavail("public", "Orders", "public.orders")]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => h.PolicyHandler().Handle(
            new UpdateCatalogPolicyCommand(TestData.DataSourceId, CatalogScope.TablesAndViews, ["public.Orders"]), default));

        Assert.Contains("public.Orders", ex.Message);
    }

    [Fact]
    public async Task UpdateCatalogPolicy_keeps_unavailable_entry_the_allowlist_kind_filter_would_exclude()
    {
        var h = PgHarness([Obj("public", "Items", Col("Id"))], [AdminUnavail("public", "Orders", "public.orders")]);
        h.AllowedObjects.Add("public.Orders");

        await h.PolicyHandler().Handle(
            new UpdateCatalogPolicyCommand(TestData.DataSourceId, CatalogScope.Views, ["public.Orders"]), default);

        Assert.Equal(["public.Orders"], h.DataSource.AllowedObjects);
    }

    [Fact]
    public async Task UpdateCatalogPolicy_postgres_refuses_wrong_case_entry()
    {
        var h = PgHarness([Obj("public", "Orders", Col("Id"))]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => h.PolicyHandler().Handle(
            new UpdateCatalogPolicyCommand(TestData.DataSourceId, CatalogScope.TablesAndViews, ["public.orders"]), default));

        Assert.Contains("public.orders", ex.Message);
    }

    [Fact]
    public async Task UpdateCatalogPolicy_sql_server_still_accepts_wrong_case_entry()
    {
        var h = PgHarness([Obj("sales", "Orders", Col("Id"))]);
        h.DataSource.Provider = DataSourceProvider.SqlServer;

        await h.PolicyHandler().Handle(
            new UpdateCatalogPolicyCommand(TestData.DataSourceId, CatalogScope.TablesAndViews, ["SALES.orders"]), default);

        Assert.Equal(["SALES.orders"], h.DataSource.AllowedObjects);
    }

    private sealed class FakeScope(ResolvedDataScope scope) : ICurrentDataScope
    {
        public bool IsEnabled => true;
        public IReadOnlyList<string> DeclaredKeys => ["ProgramId", "Region"];
        public Task<ResolvedDataScope> GetAsync(CancellationToken cancellationToken) => Task.FromResult(scope);
    }

    private sealed class FakeRules(IReadOnlyList<DataScopeRule> rules, Action<IReadOnlyList<DataScopeRule>>? onReplace = null)
        : IDataScopeRuleRepository
    {
        public Task<IReadOnlyList<DataScopeRule>> GetForDataSourceAsync(Guid dataSourceId, CancellationToken cancellationToken) =>
            Task.FromResult(rules);

        public Task ReplaceForDataSourceAsync(Guid dataSourceId, IReadOnlyList<DataScopeRule> newRules, CancellationToken cancellationToken)
        {
            onReplace?.Invoke(newRules);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCatalog(
        DataSourceCatalog catalog,
        List<AdminUnavailableObject>? unavailable = null,
        List<SchemaObjectMetadata>? raw = null) : IDataCatalogService
    {
        public Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken) => Task.FromResult(catalog);
        public Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) =>
            Task.FromResult(raw ?? []);
        public Task<List<AdminUnavailableObject>> GetUnavailableObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) =>
            Task.FromResult(unavailable ?? []);
        public void InvalidateCatalogCache(Guid dataSourceId) { }
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
