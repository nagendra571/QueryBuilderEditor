using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Commands.Admin;
using QueryBuilder.Application.DataScoping;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Queries.Admin;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests;

public class ScopeKeysTests
{
    private static readonly Guid SourceA = TestData.DataSourceId;
    private static readonly Guid SourceB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DataSource Source(Guid id, params string[] keys) =>
        new() { Id = id, Name = $"Source {id.ToString()[..1]}", IsActive = true, ScopeKeys = [.. keys] };

    // ---- Effective ----------------------------------------------------------------------------------------

    [Fact]
    public void Effective_puts_host_keys_first_and_keeps_their_casing()
    {
        var keys = DataScopeKeys.Effective(["ProgramId"], Source(SourceA, "programid", "RegionId"));

        Assert.Equal(["ProgramId", "RegionId"], keys);
    }

    [Fact]
    public void Effective_drops_repeats_ignoring_case_within_each_list()
    {
        Assert.Equal(["A", "B"], DataScopeKeys.Effective(["A", "a"], ["B", "b"]));
    }

    [Fact]
    public void Effective_with_nothing_is_empty() => Assert.Empty(DataScopeKeys.Effective([], Source(SourceA)));

    // ---- UpdateScopeKeysCommand ---------------------------------------------------------------------------

    private sealed class Rig
    {
        public Rig(bool enabled = true, DataSource[]? sources = null)
        {
            Repo = new DataSourceManagementTests.MemRepo(sources ?? [Source(SourceA)]);
            Scope = new FakeScope(ResolvedDataScope.Unrestricted, enabled, ["ProgramId"]);
            Handler = new UpdateScopeKeysCommandHandler(Repo, Scope, new DataSourceManagementTests.User(), Audit);
        }

        public DataSourceManagementTests.MemRepo Repo { get; }
        public FakeScope Scope { get; }
        public DataSourceManagementTests.AuditCapture Audit { get; } = new();
        public UpdateScopeKeysCommandHandler Handler { get; }

        public Task Send(Guid id, List<string>? keys) => Handler.Handle(new UpdateScopeKeysCommand(id, keys), default);
    }

    [Fact]
    public async Task Saves_valid_keys_and_audits_before_and_after()
    {
        var rig = new Rig();

        await rig.Send(SourceA, ["RegionId", "Tenant_1"]);

        Assert.Equal(["RegionId", "Tenant_1"], rig.Repo.Items.Single().ScopeKeys);
        Assert.Equal(1, rig.Repo.Saves);
        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(AuditAction.DataSourceScopeKeysUpdated, entry.Action);
        Assert.Equal(SourceA, entry.DataSourceId);
        var json = System.Text.Json.JsonSerializer.Serialize(entry.Details);
        Assert.Contains("\"before\":[]", json);
        Assert.Contains("\"after\":[\"RegionId\",\"Tenant_1\"]", json);
    }

    [Fact]
    public async Task Removing_all_keys_is_allowed()
    {
        var rig = new Rig(sources: [Source(SourceA, "RegionId")]);

        await rig.Send(SourceA, []);

        Assert.Empty(rig.Repo.Items.Single().ScopeKeys);
    }

    [Fact]
    public async Task Refused_when_scoping_is_off_in_code()
    {
        var rig = new Rig(enabled: false);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Send(SourceA, ["RegionId"]));
        Assert.Contains("turned off in the app's code", ex.Message);
        Assert.Equal(0, rig.Repo.Saves);
    }

    [Fact]
    public async Task Unknown_data_source_is_not_found() =>
        await Assert.ThrowsAsync<NotFoundException>(() => new Rig().Send(SourceB, ["RegionId"]));

    [Fact]
    public async Task Missing_list_is_refused() =>
        await Assert.ThrowsAsync<CatalogValidationException>(() => new Rig().Send(SourceA, null));

    [Theory]
    [InlineData("x-y")]
    [InlineData("")]
    [InlineData(" RegionId")]
    [InlineData("RegionId ")]
    [InlineData("1Region")]
    [InlineData("Re gion")]
    public async Task Invalid_names_are_refused_without_trimming(string key)
    {
        var rig = new Rig();

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => rig.Send(SourceA, [key]));
        Assert.Contains("is invalid", ex.Message);
        Assert.Equal(0, rig.Repo.Saves);
    }

    [Fact]
    public async Task A_name_over_128_characters_is_refused() =>
        await Assert.ThrowsAsync<CatalogValidationException>(() => new Rig().Send(SourceA, ["a" + new string('b', 128)]));

    [Fact]
    public async Task At_most_20_keys()
    {
        var rig = new Rig();

        await rig.Send(SourceA, Enumerable.Range(0, 20).Select(i => $"K{i}").ToList());
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            rig.Send(SourceA, Enumerable.Range(0, 21).Select(i => $"K{i}").ToList()));
    }

    [Fact]
    public async Task Duplicates_ignoring_case_are_refused() =>
        await Assert.ThrowsAsync<CatalogValidationException>(() => new Rig().Send(SourceA, ["RegionId", "regionid"]));

    [Fact]
    public async Task A_host_key_cant_be_added_ignoring_case()
    {
        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => new Rig().Send(SourceA, ["programid"]));
        Assert.Contains("already set in the app's code", ex.Message);
    }

    // ---- Guard --------------------------------------------------------------------------------------------

    private static DataScopeGuard Guard(ResolvedDataScope scope, DataSource[] sources, params DataScopeRule[] rules) =>
        new(new FakeScope(scope, true, ["ProgramId"]), new DataSourceManagementTests.MemRepo(sources), new MemRules(rules),
            new CatalogStub(TestData.Catalog()));

    private static readonly DataScopeRule RegionRule = TestData.Rule("sales.vw_Orders", "RegionId", "RegionCode");

    [Fact]
    public async Task A_view_mapped_to_an_admin_added_key_is_filtered_by_the_resolvers_values()
    {
        var guard = Guard(TestData.Scoped(("RegionId", ["EU"])), [Source(SourceA, "RegionId")], RegionRule);

        var predicate = Assert.Single(await guard.AuthorizeAsync(SourceA, TestData.Definition(), default));
        Assert.Equal("RegionCode", predicate.ColumnName);
        Assert.Equal(["EU"], predicate.Values);
        var catalog = await guard.FilterCatalogAsync(TestData.Catalog(), default);
        Assert.Equal(["vw_Orders"], catalog.Schemas.SelectMany(s => s.Objects).Select(o => o.Name));
    }

    [Fact]
    public async Task A_key_added_to_one_data_source_does_not_apply_to_another()
    {
        var ruleForB = new DataScopeRule { DataSourceId = SourceB, ObjectName = "sales.vw_Orders", ScopeKey = "RegionId", ColumnName = "RegionCode" };
        var guard = Guard(TestData.Scoped(("RegionId", ["EU"])), [Source(SourceA, "RegionId"), Source(SourceB)], ruleForB);
        var catalogB = TestData.Catalog();
        catalogB.DataSourceId = SourceB;

        await Assert.ThrowsAsync<ForbiddenException>(() => guard.AuthorizeAsync(SourceB, TestData.Definition(), default));
        Assert.Empty((await guard.FilterCatalogAsync(catalogB, default)).Schemas);
    }

    [Fact]
    public async Task Removing_the_key_hides_the_view_from_scoped_users_but_not_from_unrestricted_ones()
    {
        var source = Source(SourceA, "RegionId");
        var scoped = Guard(TestData.Scoped(("RegionId", ["EU"])), [source], RegionRule);
        var unrestricted = Guard(ResolvedDataScope.Unrestricted, [source], RegionRule);

        Assert.Single(await scoped.AuthorizeAsync(SourceA, TestData.Definition(), default));

        source.ScopeKeys = [];

        await Assert.ThrowsAsync<ForbiddenException>(() => scoped.AuthorizeAsync(SourceA, TestData.Definition(), default));
        Assert.Empty((await scoped.FilterCatalogAsync(TestData.Catalog(), default)).Schemas);
        Assert.Empty(await unrestricted.AuthorizeAsync(SourceA, TestData.Definition(), default));
        Assert.Equal(3, (await unrestricted.FilterCatalogAsync(TestData.Catalog(), default)).Schemas.Single().Objects.Count);

        source.ScopeKeys = ["RegionId"];
        Assert.Single(await scoped.AuthorizeAsync(SourceA, TestData.Definition(), default));
    }

    [Fact]
    public async Task Guard_fails_closed_when_the_data_source_row_is_gone() =>
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Guard(TestData.Scoped(("ProgramId", ["10"])), [], TestData.Rule("sales.vw_Orders", "ProgramId", "ProgramId"))
                .AuthorizeAsync(SourceA, TestData.Definition(), default));

    // ---- GetDataScopeQuery / UpdateDataScopeCommand -------------------------------------------------------

    private static GetDataScopeQueryHandler Query(DataSource source, FakeScope scope, params DataScopeRule[] rules) =>
        new(new DataSourceManagementTests.MemRepo(source), new CatalogStub(TestData.Catalog()), new MemRules(rules), scope);

    [Fact]
    public async Task Query_returns_host_data_source_and_effective_keys_and_maps_against_the_effective_set()
    {
        var scope = new FakeScope(ResolvedDataScope.Unrestricted, true, ["ProgramId"]);
        var dto = await Query(Source(SourceA, "RegionId", "programid"), scope, RegionRule)
            .Handle(new GetDataScopeQuery(SourceA), default);

        Assert.True(dto.Enabled);
        Assert.Equal(["ProgramId"], dto.HostKeys);
        // "programid" became a host key after it was added here, so it's listed once, as the host's.
        Assert.Equal(["RegionId"], dto.DataSourceKeys);
        Assert.Equal(["ProgramId", "RegionId"], dto.DeclaredKeys);
        var orders = dto.Objects.Single(o => o.ObjectName == "sales.vw_Orders");
        Assert.Equal("RegionCode", orders.Mappings["RegionId"]);
        Assert.Empty(orders.StaleKeys);
    }

    [Fact]
    public async Task Query_reports_a_removed_key_as_stale()
    {
        var scope = new FakeScope(ResolvedDataScope.Unrestricted, true, ["ProgramId"]);
        var dto = await Query(Source(SourceA), scope, RegionRule).Handle(new GetDataScopeQuery(SourceA), default);

        var orders = dto.Objects.Single(o => o.ObjectName == "sales.vw_Orders");
        Assert.Empty(orders.Mappings);
        Assert.Equal(["RegionId"], orders.StaleKeys);
    }

    [Fact]
    public async Task Query_when_scoping_is_off_still_returns_the_key_lists_with_no_objects()
    {
        var scope = new FakeScope(ResolvedDataScope.Unrestricted, false, ["ProgramId"]);
        var dto = await Query(Source(SourceA, "RegionId"), scope).Handle(new GetDataScopeQuery(SourceA), default);

        Assert.False(dto.Enabled);
        Assert.Equal(["ProgramId"], dto.HostKeys);
        Assert.Equal(["RegionId"], dto.DataSourceKeys);
        Assert.Empty(dto.Objects);
    }

    [Fact]
    public async Task Saving_a_mapping_accepts_an_admin_added_key_and_rejects_an_unknown_one()
    {
        var rules = new MemRules();
        var handler = new UpdateDataScopeCommandHandler(
            new DataSourceManagementTests.MemRepo(Source(SourceA, "RegionId")), new CatalogStub(TestData.Catalog()), rules,
            new FakeScope(ResolvedDataScope.Unrestricted, true, ["ProgramId"]), new DataSourceManagementTests.User(),
            new DataSourceManagementTests.AuditCapture());

        await handler.Handle(new UpdateDataScopeCommand(SourceA,
            [new("sales.vw_Orders", DataScopeState.Scoped, new() { ["RegionId"] = "RegionCode" })]), default);
        Assert.Equal("RegionId", Assert.Single(await rules.GetForDataSourceAsync(SourceA, default)).ScopeKey);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => handler.Handle(new UpdateDataScopeCommand(SourceA,
            [new("sales.vw_Orders", DataScopeState.Scoped, new() { ["Nope"] = "RegionCode" })]), default));
        Assert.Contains("not a declared data-scope key", ex.Message);
    }

    // ---- fakes --------------------------------------------------------------------------------------------

    internal sealed class FakeScope(ResolvedDataScope scope, bool enabled, string[] hostKeys) : ICurrentDataScope
    {
        public bool IsEnabled => enabled;
        public IReadOnlyList<string> HostKeys => hostKeys;
        public Task<ResolvedDataScope> GetAsync(CancellationToken cancellationToken) => Task.FromResult(scope);
    }

    private sealed class MemRules(params DataScopeRule[] initial) : IDataScopeRuleRepository
    {
        private IReadOnlyList<DataScopeRule> _rules = initial;

        public Task<IReadOnlyList<DataScopeRule>> GetForDataSourceAsync(Guid dataSourceId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DataScopeRule>>(_rules.Where(r => r.DataSourceId == dataSourceId).ToList());

        public Task ReplaceForDataSourceAsync(Guid dataSourceId, IReadOnlyList<DataScopeRule> rules, CancellationToken cancellationToken)
        {
            _rules = rules.Select(r => { r.DataSourceId = dataSourceId; return r; }).ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class CatalogStub(DataSourceCatalog catalog) : IDataCatalogService
    {
        public Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken) => Task.FromResult(catalog);
        public Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<AdminUnavailableObject>> GetUnavailableObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) => Task.FromResult(new List<AdminUnavailableObject>());
        public void InvalidateCatalogCache(Guid dataSourceId) { }
    }
}
