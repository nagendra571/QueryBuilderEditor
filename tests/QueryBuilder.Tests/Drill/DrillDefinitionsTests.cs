using QueryBuilder.Application.Common;
using QueryBuilder.Application.Drill;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests.Drill;

public class DrillDefinitionsTests
{
    private static readonly IReadOnlyList<string> NoDrill = [];

    private static QueryDefinition Saved()
    {
        var d = VizFixture.Definition();
        d.Filters.Conditions.Add(TestData.Condition("Status", FilterOperator.Equals, "Open"));
        d.GroupBy = [new QueryColumnRef { TableAlias = "t0", ColumnName = "RegionCode" }, new QueryColumnRef { TableAlias = "t0", ColumnName = "Channel" }];
        d.Having = new FilterGroup { Conditions = [new FilterCondition { TableAlias = "t0", ColumnName = "Amount", Aggregate = AggregateFunction.Sum, Operator = FilterOperator.GreaterThan, Value = "5" }] };
        d.Sorts = [new QuerySort { TableAlias = "t0", ColumnName = "RegionCode" }, new QuerySort { TableAlias = "t0", ColumnName = "Note" }];
        return d;
    }

    private static DrillStep Step(string column, string? value) => new(column, value);

    private static VisualizationOptions BarOptions() => new() { X = "Region", Measures = ["Total"] };

    [Fact]
    public void Rows_adds_each_step_as_an_equality_inside_the_filter_group()
    {
        var result = DrillDefinitions.ForRows(Saved(), [Step("Region", "EU")], NoDrill);

        Assert.Equal(FilterLogicalOperator.And, result.Filters.Operator);
        Assert.Empty(result.Filters.Conditions);
        Assert.Equal(2, result.Filters.Groups.Count);
        Assert.Equal("Status", Assert.Single(result.Filters.Groups[0].Conditions).ColumnName);
        var step = Assert.Single(result.Filters.Groups[1].Conditions);
        Assert.Equal(("t0", "RegionCode", FilterOperator.Equals, "EU", ColumnDataType.Text),
            (step.TableAlias, step.ColumnName, step.Operator, step.Value, step.DataType));
        Assert.False(step.IsParameterized);
    }

    [Fact]
    public void Null_step_becomes_is_null()
    {
        var result = DrillDefinitions.ForRows(Saved(), [Step("Region", null)], NoDrill);

        var step = Assert.Single(result.Filters.Groups[^1].Conditions);
        Assert.Equal(FilterOperator.IsNull, step.Operator);
        Assert.Null(step.Value);
    }

    [Fact]
    public void Boolean_step_becomes_is_true_or_is_false_and_rejects_other_values()
    {
        var d = Saved();
        d.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "Flag", DataType = ColumnDataType.Boolean });

        Assert.Equal(FilterOperator.IsTrue, DrillDefinitions.ForRows(d, [Step("Flag", "true")], NoDrill).Filters.Groups[^1].Conditions[0].Operator);
        Assert.Equal(FilterOperator.IsFalse, DrillDefinitions.ForRows(d, [Step("Flag", "false")], NoDrill).Filters.Groups[^1].Conditions[0].Operator);
        Assert.Throws<CatalogValidationException>(() => DrillDefinitions.ForRows(d, [Step("Flag", "yes")], NoDrill));
    }

    [Fact]
    public void Rows_removes_grouping_and_having_and_replaces_aggregates_with_base_columns()
    {
        var result = DrillDefinitions.ForRows(Saved(), [], NoDrill);

        Assert.Equal(["Region", "Channel", "Amount", "OrderId"], result.Columns.Select(c => c.EffectiveAlias));
        Assert.All(result.Columns, c => Assert.Equal(AggregateFunction.None, c.Aggregate));
        Assert.Empty(result.GroupBy);
        Assert.Null(result.Having);
        Assert.Equal("RegionCode", Assert.Single(result.Sorts).ColumnName);
    }

    [Fact]
    public void Rows_never_includes_hidden_columns_but_includes_drill_columns()
    {
        var result = DrillDefinitions.ForRows(Saved(), [], ["Site", "Site", "Channel"]);

        Assert.DoesNotContain(result.Columns, c => c.EffectiveAlias == "HiddenNote");
        Assert.Single(result.Columns, c => c.ColumnName == "Site");
        Assert.Single(result.Columns, c => c.ColumnName == "Channel");
        Assert.Equal("t0", result.Columns.Single(c => c.ColumnName == "Site").TableAlias);
    }

    [Theory]
    [InlineData("Total")]
    [InlineData("HiddenNote")]
    [InlineData("Nope")]
    public void Step_on_an_aggregated_or_unknown_column_is_refused(string column)
    {
        var ex = Assert.Throws<CatalogValidationException>(() => DrillDefinitions.ForRows(Saved(), [Step(column, "x")], NoDrill));
        Assert.Equal($"'{column}' can't be used to drill into this chart.", ex.Message);
    }

    [Fact]
    public void A_drill_column_can_be_a_step()
    {
        var result = DrillDefinitions.ForRows(Saved(), [Step("Site", "A")], ["Site"]);
        Assert.Equal("Site", result.Filters.Groups[^1].Conditions[0].ColumnName);
    }

    [Fact]
    public void Too_many_steps_or_long_value_is_refused()
    {
        var nine = Enumerable.Range(0, DrillLimits.MaxSteps + 1).Select(_ => Step("Region", "EU")).ToList();
        Assert.Throws<CatalogValidationException>(() => DrillDefinitions.ForRows(Saved(), nine, NoDrill));
        Assert.Throws<CatalogValidationException>(() =>
            DrillDefinitions.ForRows(Saved(), [Step("Region", new string('a', DrillLimits.MaxValueLength + 1))], NoDrill));
    }

    [Fact]
    public void Saved_definition_is_not_mutated()
    {
        var saved = Saved();
        var before = QueryDefinitionSerializer.Serialize(saved);

        DrillDefinitions.ForRows(saved, [Step("Region", "EU")], ["Site"]);
        DrillDefinitions.ApplySteps(saved, [Step("Region", "EU")], NoDrill);
        DrillDefinitions.ForBreakdown(saved, VisualizationType.Bar, BarOptions(), [Step("Region", "EU")], "Channel", NoDrill);

        Assert.Equal(before, QueryDefinitionSerializer.Serialize(saved));
    }

    [Fact]
    public void Breakdown_regroups_measures_by_the_new_column()
    {
        var (definition, options) = DrillDefinitions.ForBreakdown(
            Saved(), VisualizationType.Bar, BarOptions(), [Step("Region", "EU")], "Channel", NoDrill);

        Assert.Equal("Channel", Assert.Single(definition.GroupBy).ColumnName);
        Assert.Equal(["Channel", "Total"], definition.Columns.Select(c => c.EffectiveAlias));
        Assert.Equal(AggregateFunction.Sum, definition.Columns[1].Aggregate);
        Assert.Null(definition.Having);
        Assert.Equal("Channel", options.X);
        Assert.Equal(["Total"], options.Measures);
        Assert.Equal("RegionCode", Assert.Single(definition.Filters.Groups[^1].Conditions).ColumnName);
    }

    [Fact]
    public void Breakdown_keeps_a_different_series_column_and_clears_one_equal_to_the_breakdown()
    {
        var options = new VisualizationOptions { X = "Region", Series = "Channel", Measures = ["Total"] };
        var (definition, _) = DrillDefinitions.ForBreakdown(Saved(), VisualizationType.Bar, options, [], "Site", ["Site"]);
        Assert.Equal(["Site", "Channel", "Total"], definition.Columns.Select(c => c.EffectiveAlias));
        Assert.Equal(2, definition.GroupBy.Count);

        var (_, same) = DrillDefinitions.ForBreakdown(Saved(), VisualizationType.Bar, options, [], "Channel", NoDrill);
        Assert.Null(same.Series);
    }

    [Theory]
    [InlineData(VisualizationType.Trend)]
    [InlineData(VisualizationType.Pivot)]
    [InlineData(VisualizationType.Scatter)]
    [InlineData(VisualizationType.Counter)]
    [InlineData(VisualizationType.Table)]
    public void Breakdown_is_refused_for_unsupported_types(VisualizationType type)
    {
        var ex = Assert.Throws<CatalogValidationException>(() =>
            DrillDefinitions.ForBreakdown(Saved(), type, BarOptions(), [], "Channel", NoDrill));
        Assert.Equal("This chart can't be broken down.", ex.Message);
    }

    [Fact]
    public void Breakdown_is_refused_for_unaggregated_queries()
    {
        var saved = Saved();
        saved.Columns.RemoveAll(c => c.Aggregate != AggregateFunction.None);
        Assert.Throws<CatalogValidationException>(() =>
            DrillDefinitions.ForBreakdown(saved, VisualizationType.Bar, new VisualizationOptions { X = "Region" }, [], "Channel", NoDrill));
    }

    [Fact]
    public void Breakdown_on_a_column_already_in_the_trail_is_refused()
    {
        Assert.Throws<CatalogValidationException>(() =>
            DrillDefinitions.ForBreakdown(Saved(), VisualizationType.Bar, BarOptions(), [Step("Region", "EU")], "Region", NoDrill));
    }

    [Fact]
    public void Breakdown_on_an_aggregated_or_unknown_column_is_refused()
    {
        Assert.Throws<CatalogValidationException>(() =>
            DrillDefinitions.ForBreakdown(Saved(), VisualizationType.Bar, BarOptions(), [], "Total", NoDrill));
        Assert.Throws<CatalogValidationException>(() =>
            DrillDefinitions.ForBreakdown(Saved(), VisualizationType.Bar, BarOptions(), [], "Nope", NoDrill));
    }

    [Fact]
    public void AllowedStepColumns_lists_visible_plain_columns_then_drill_columns()
    {
        Assert.Equal(["Region", "Channel", "Site"], DrillDefinitions.AllowedStepColumns(Saved(), ["Site"]));
        Assert.True(DrillDefinitions.IsAggregated(Saved()));
    }
}
