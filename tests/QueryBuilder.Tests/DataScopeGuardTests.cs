using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataScoping;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests;

public class DataScopeGuardTests
{
    private static DataScopeGuard Guard(ResolvedDataScope scope, params DataScopeRule[] rules) =>
        new(new FakeScope(scope), new FakeRules(rules), new FakeCatalog(TestData.Catalog()));

    [Fact]
    public async Task Catalog_for_scoped_user_contains_only_decided_and_usable_objects()
    {
        var guard = Guard(TestData.Scoped(("ProgramId", ["10"])),
            TestData.Rule("sales.vw_Orders", "ProgramId", "ProgramId"),
            TestData.Rule("sales.vw_Programs"));

        var catalog = await guard.FilterCatalogAsync(TestData.Catalog(), default);

        var names = catalog.Schemas.SelectMany(s => s.Objects).Select(o => o.Name).ToList();
        Assert.Equal(["vw_Orders", "vw_Programs"], names);
    }

    [Fact]
    public async Task Catalog_filtering_never_mutates_the_shared_cached_instance()
    {
        var shared = TestData.Catalog();
        var guard = Guard(ResolvedDataScope.Denied);

        var filtered = await guard.FilterCatalogAsync(shared, default);

        Assert.Empty(filtered.Schemas);
        Assert.Equal(3, shared.Schemas.Single().Objects.Count);
    }

    [Fact]
    public async Task Unrestricted_user_gets_the_catalog_and_no_predicates()
    {
        var guard = Guard(ResolvedDataScope.Unrestricted);
        var catalog = TestData.Catalog();

        Assert.Same(catalog, await guard.FilterCatalogAsync(catalog, default));
        Assert.Empty(await guard.AuthorizeAsync(TestData.DataSourceId, TestData.Definition("vw_Secret"), default));
    }

    [Fact]
    public async Task Authorize_returns_typed_predicate_for_scoped_object()
    {
        var guard = Guard(TestData.Scoped(("ProgramId", ["10"])), TestData.Rule("sales.vw_Orders", "PROGRAMID", "programid"));

        var predicates = await guard.AuthorizeAsync(TestData.DataSourceId, TestData.Definition(), default);

        var predicate = Assert.Single(predicates);
        Assert.Equal("t0", predicate.Alias);
        Assert.Equal("ProgramId", predicate.ColumnName);
        Assert.Equal(ColumnDataType.Number, predicate.DataType);
        Assert.Equal(["10"], predicate.Values);
    }

    [Fact]
    public async Task Authorize_forbids_undecided_object()
    {
        var guard = Guard(TestData.Scoped(("ProgramId", ["10"])), TestData.Rule("sales.vw_Orders", "ProgramId", "ProgramId"));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            guard.AuthorizeAsync(TestData.DataSourceId, TestData.Definition("vw_Secret"), default));
        Assert.Contains("sales.vw_Secret", ex.Message);
    }

    [Fact]
    public async Task Authorize_forbids_a_hidden_object_reached_through_a_join()
    {
        var guard = Guard(TestData.Scoped(("ProgramId", ["10"])), TestData.Rule("sales.vw_Orders", "ProgramId", "ProgramId"));
        var definition = TestData.Definition();
        definition.Joins.Add(new QueryJoin { SchemaName = "sales", ObjectName = "vw_Secret", Alias = "t1" });

        await Assert.ThrowsAsync<ForbiddenException>(() => guard.AuthorizeAsync(TestData.DataSourceId, definition, default));
    }

    [Fact]
    public async Task Mapped_column_missing_from_the_view_hides_it_instead_of_serving_it_unfiltered()
    {
        var guard = Guard(TestData.Scoped(("ProgramId", ["10"])), TestData.Rule("sales.vw_Secret", "ProgramId", "ProgramId"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            guard.AuthorizeAsync(TestData.DataSourceId, TestData.Definition("vw_Secret"), default));
        var catalog = await guard.FilterCatalogAsync(TestData.Catalog(), default);
        Assert.Empty(catalog.Schemas);
    }

    private sealed class FakeScope(ResolvedDataScope scope) : ICurrentDataScope
    {
        public bool IsEnabled => true;
        public IReadOnlyList<string> DeclaredKeys => ["ProgramId", "Region"];
        public Task<ResolvedDataScope> GetAsync(CancellationToken cancellationToken) => Task.FromResult(scope);
    }

    private sealed class FakeRules(IReadOnlyList<DataScopeRule> rules) : IDataScopeRuleRepository
    {
        public Task<IReadOnlyList<DataScopeRule>> GetForDataSourceAsync(Guid dataSourceId, CancellationToken cancellationToken) =>
            Task.FromResult(rules);

        public Task ReplaceForDataSourceAsync(Guid dataSourceId, IReadOnlyList<DataScopeRule> newRules, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCatalog(DataSourceCatalog catalog) : IDataCatalogService
    {
        public Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken) => Task.FromResult(catalog);
        public Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void InvalidateCatalogCache(Guid dataSourceId) { }
    }
}
