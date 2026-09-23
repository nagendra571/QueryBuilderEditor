using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Editor;
using QueryBuilder.Editor.DataScoping;

namespace QueryBuilder.Tests;

public class DataScopeOptionsTests
{
    private static void Register(Action<QueryBuilderEditorOptions> configure) =>
        new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=(localdb)\\none;Database=none";
            configure(o);
        });

    [Fact]
    public void Not_configured_is_valid() => Register(_ => { });

    [Fact]
    public void Keys_and_resolver_is_valid() => Register(o =>
    {
        o.DataScope.Keys = ["ProgramId"];
        o.DataScope.Resolver = _ => DataScope.Unrestricted;
    });

    [Fact]
    public void Resolver_without_keys_throws() =>
        Assert.Throws<InvalidOperationException>(() => Register(o => o.DataScope.Resolver = _ => DataScope.Unrestricted));

    [Fact]
    public void Keys_without_resolver_throws() =>
        Assert.Throws<InvalidOperationException>(() => Register(o => o.DataScope.Keys = ["ProgramId"]));

    [Fact]
    public void Both_resolvers_throws() =>
        Assert.Throws<InvalidOperationException>(() => Register(o =>
        {
            o.DataScope.Keys = ["ProgramId"];
            o.DataScope.Resolver = _ => DataScope.Unrestricted;
            o.DataScope.ResolverAsync = _ => ValueTask.FromResult<DataScope?>(DataScope.Unrestricted);
        }));

    [Theory]
    [InlineData("Program Id")]
    [InlineData("")]
    public void Invalid_key_throws(string key) =>
        Assert.Throws<InvalidOperationException>(() => Register(o =>
        {
            o.DataScope.Keys = [key];
            o.DataScope.Resolver = _ => DataScope.Unrestricted;
        }));

    [Fact]
    public void Duplicate_keys_ignoring_case_throw() =>
        Assert.Throws<InvalidOperationException>(() => Register(o =>
        {
            o.DataScope.Keys = ["ProgramId", "programid"];
            o.DataScope.Resolver = _ => DataScope.Unrestricted;
        }));

    [Fact]
    public void DataScope_formats_values_invariantly_and_merges_keys()
    {
        var scope = DataScope.For("ProgramId", 10, 12, 10).And("programid", "14").And("Region", "EU", null);

        Assert.False(scope.IsUnrestricted);
        Assert.Equal(["10", "12", "14"], scope.Values["PROGRAMID"]);
        Assert.Equal(["EU"], scope.Values["Region"]);
    }

    [Fact]
    public void DataScope_flattens_collections_but_not_strings()
    {
        var programIds = new List<int> { 10, 12 };

        var scope = DataScope.For("ProgramId", programIds).And("Region", new[] { "EU", "US" }).And("Code", "AB");

        Assert.Equal(["10", "12"], scope.Values["ProgramId"]);
        Assert.Equal(["EU", "US"], scope.Values["Region"]);
        Assert.Equal(["AB"], scope.Values["Code"]);
    }

    [Fact]
    public void Unrestricted_cannot_be_narrowed() =>
        Assert.Throws<InvalidOperationException>(() => DataScope.Unrestricted.And("ProgramId", 1));

    private static async Task<ResolvedDataScope> Resolve(Action<DataScopeOptions> configure)
    {
        var options = new QueryBuilderEditorOptions { ConnectionString = "x" };
        configure(options.DataScope);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var service = new CurrentDataScopeService(accessor, options, NullLogger<CurrentDataScopeService>.Instance);
        return await service.GetAsync(default);
    }

    [Fact]
    public async Task Feature_off_resolves_unrestricted() =>
        Assert.True((await Resolve(_ => { })).IsUnrestricted);

    [Fact]
    public async Task Null_from_resolver_is_denied_not_unrestricted()
    {
        var scope = await Resolve(o => { o.Keys = ["ProgramId"]; o.Resolver = _ => null; });

        Assert.False(scope.IsUnrestricted);
        Assert.Empty(scope.Values);
    }

    [Fact]
    public async Task Throwing_resolver_is_denied_not_unrestricted()
    {
        var scope = await Resolve(o => { o.Keys = ["ProgramId"]; o.Resolver = _ => throw new InvalidOperationException("claim missing"); });

        Assert.False(scope.IsUnrestricted);
        Assert.Empty(scope.Values);
    }

    [Fact]
    public async Task Async_resolver_values_flow_through()
    {
        var scope = await Resolve(o =>
        {
            o.Keys = ["ProgramId"];
            o.ResolverAsync = _ => ValueTask.FromResult<DataScope?>(DataScope.For("ProgramId", 10));
        });

        Assert.Equal(["10"], scope.ValuesFor("programid"));
    }

    [Fact]
    public async Task Resolver_runs_once_per_request()
    {
        var calls = 0;
        var options = new QueryBuilderEditorOptions { ConnectionString = "x" };
        options.DataScope.Keys = ["ProgramId"];
        options.DataScope.Resolver = _ => { calls++; return DataScope.For("ProgramId", 1); };
        var service = new CurrentDataScopeService(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, options, NullLogger<CurrentDataScopeService>.Instance);

        await service.GetAsync(default);
        await service.GetAsync(default);

        Assert.Equal(1, calls);
    }
}
