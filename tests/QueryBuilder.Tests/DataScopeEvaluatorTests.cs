using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataScoping;

namespace QueryBuilder.Tests;

public class DataScopeEvaluatorTests
{
    private static readonly string[] Keys = ["ProgramId", "Region"];
    private const string Orders = "sales.vw_Orders";

    [Fact]
    public void Unrestricted_sees_everything_unfiltered_even_undecided_objects()
    {
        var access = DataScopeEvaluator.Evaluate(Orders, [], ResolvedDataScope.Unrestricted, Keys);

        Assert.True(access.IsVisible);
        Assert.Empty(access.Filters);
    }

    [Fact]
    public void Undecided_object_is_hidden_from_scoped_user()
    {
        var access = DataScopeEvaluator.Evaluate(Orders, [TestData.Rule("sales.vw_Programs")], TestData.Scoped(("ProgramId", ["10"])), Keys);

        Assert.False(access.IsVisible);
    }

    [Fact]
    public void Not_scoped_object_is_visible_unfiltered_even_when_denied()
    {
        var access = DataScopeEvaluator.Evaluate(Orders, [TestData.Rule(Orders)], ResolvedDataScope.Denied, Keys);

        Assert.True(access.IsVisible);
        Assert.Empty(access.Filters);
    }

    [Fact]
    public void Scoped_object_is_filtered_by_the_users_values()
    {
        var access = DataScopeEvaluator.Evaluate(
            Orders, [TestData.Rule(Orders, "ProgramId", "ProgramId")], TestData.Scoped(("programid", ["10", "12"])), Keys);

        Assert.True(access.IsVisible);
        var filter = Assert.Single(access.Filters);
        Assert.Equal("ProgramId", filter.ColumnName);
        Assert.Equal(["10", "12"], filter.Values);
    }

    [Fact]
    public void Scoped_object_is_hidden_when_user_has_no_values_for_a_mapped_key()
    {
        var rules = new[] { TestData.Rule(Orders, "ProgramId", "ProgramId"), TestData.Rule(Orders, "Region", "RegionCode") };

        var access = DataScopeEvaluator.Evaluate(Orders, rules, TestData.Scoped(("ProgramId", ["10"])), Keys);

        Assert.False(access.IsVisible);
    }

    [Fact]
    public void Scoped_object_is_hidden_from_denied_user()
    {
        var access = DataScopeEvaluator.Evaluate(Orders, [TestData.Rule(Orders, "ProgramId", "ProgramId")], ResolvedDataScope.Denied, Keys);

        Assert.False(access.IsVisible);
    }

    [Fact]
    public void Two_mapped_keys_produce_two_filters()
    {
        var rules = new[] { TestData.Rule(Orders, "ProgramId", "ProgramId"), TestData.Rule(Orders, "Region", "RegionCode") };

        var access = DataScopeEvaluator.Evaluate(Orders, rules, TestData.Scoped(("ProgramId", ["10"]), ("Region", ["EU"])), Keys);

        Assert.True(access.IsVisible);
        Assert.Equal(2, access.Filters.Count);
    }

    [Fact]
    public void Rule_for_a_key_the_host_no_longer_declares_hides_the_object()
    {
        var access = DataScopeEvaluator.Evaluate(
            Orders, [TestData.Rule(Orders, "ModuleId", "ProgramId")], TestData.Scoped(("ModuleId", ["1"])), Keys);

        Assert.False(access.IsVisible);
    }

    [Fact]
    public void Key_rules_win_over_a_not_scoped_marker()
    {
        var rules = new[] { TestData.Rule(Orders), TestData.Rule(Orders, "ProgramId", "ProgramId") };

        var access = DataScopeEvaluator.Evaluate(Orders, rules, TestData.Scoped(("ProgramId", ["10"])), Keys);

        Assert.Single(access.Filters);
    }

    [Fact]
    public void Object_name_matching_ignores_case()
    {
        var access = DataScopeEvaluator.Evaluate("SALES.VW_ORDERS", [TestData.Rule(Orders)], ResolvedDataScope.Denied, Keys);

        Assert.True(access.IsVisible);
    }
}
