using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests;

public class RenderVisualizationCommandTests
{
    private static Dictionary<string, object?> Row(string? region, decimal? total, string channel = "Web", decimal orders = 1) =>
        new() { ["Region"] = region, ["Channel"] = channel, ["Total"] = total, ["Orders"] = orders, ["HiddenNote"] = "secret" };

    private static RenderVisualizationCommandHandler Handler(VizFixture f)
    {
        var grant = new QueryBuilder.Application.Dashboards.DashboardGrant();
        return new(
            f.Queries, f.Visualizations, f.DataSources, f.Catalog, f.Builders, f.Executor, f.Guard,
            new VizFixture.FakeScope(), f.Limits, f.Audit, f.User,
            new QueryBuilder.Application.Parameters.ParameterBinder(null!, TimeProvider.System),
            grant, new QueryBuilder.Application.Dashboards.DashboardGrantResolver(null!, f.Queries, grant));
    }

    private static Task<QueryBuilder.Application.Dtos.VisualizationDataDto> Render(VizFixture f, QueryVisualization viz, Dictionary<string, string?>? parameters = null) =>
        Handler(f).Handle(new RenderVisualizationCommand(f.Query.Id, viz.Id, parameters ?? []), default);

    private static (VizFixture Fixture, QueryVisualization Viz) Arrange(string user = VizFixture.Owner, VisualizationType type = VisualizationType.Bar, VisualizationOptions? options = null)
    {
        var f = new VizFixture();
        f.User.UserId = user;
        var viz = f.AddVisualization(type, options ?? VizFixture.BarOptions());
        f.Executor.Result = VizFixture.Result([Row("EU", 10), Row("US", 20)]);
        return (f, viz);
    }

    [Fact]
    public async Task User_without_a_share_is_forbidden_and_nothing_runs()
    {
        var (f, viz) = Arrange(VizFixture.Stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => Render(f, viz));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Viewer_share_can_render()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser);

        var data = await Render(f, viz);

        Assert.Equal(2, data.RowCount);
    }

    [Fact]
    public async Task Disabled_query_is_refused()
    {
        var (f, viz) = Arrange();
        f.Query.IsDisabled = true;

        await Assert.ThrowsAsync<QueryDisabledException>(() => Render(f, viz));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Visualization_of_another_query_is_not_found()
    {
        var f = new VizFixture();
        var other = f.AddVisualization(VisualizationType.Bar, VizFixture.BarOptions(), queryId: Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => Render(f, other));
    }

    [Fact]
    public async Task Unknown_query_is_not_found()
    {
        var (f, viz) = Arrange();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler(f).Handle(new RenderVisualizationCommand(Guid.NewGuid(), viz.Id, []), default));
    }

    [Fact]
    public async Task Stale_chart_names_the_missing_column()
    {
        var (f, viz) = Arrange();
        var definition = VizFixture.Definition();
        definition.Columns.RemoveAll(c => c.EffectiveAlias == "Total");
        f.Query.DefinitionJson = QueryDefinitionSerializer.Serialize(definition);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("Total", ex.Message);
        Assert.Contains("needs updating", ex.Message);
        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Guard_predicates_reach_the_sql_builder()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser);
        var predicates = new[] { new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, ["10"]) };
        f.Guard.Predicates = predicates;

        await Render(f, viz);

        Assert.Equal(1, f.Guard.AuthorizeCalls);
        Assert.Equal(1, f.Catalog.ValidateCalls);
        Assert.Same(predicates, f.Builders.ReceivedScope);
        Assert.Contains("@__scope0", f.Builders.Generated!.Sql);
    }

    [Fact]
    public async Task Without_predicates_the_generated_sql_has_no_scope_filter()
    {
        var (f, viz) = Arrange();

        await Render(f, viz);

        Assert.DoesNotContain("@__scope", f.Builders.Generated!.Sql);
    }

    [Fact]
    public async Task Truncated_with_a_record_limit_reports_the_record_limit()
    {
        var (f, viz) = Arrange();
        f.Limits = new RecordLimitSettings(3);
        f.Executor.Result = VizFixture.Result([Row("A", 1), Row("B", 2), Row("C", 3)], truncated: true);

        var ex = await Assert.ThrowsAsync<RecordLimitExceededException>(() => Render(f, viz));

        Assert.Contains("more than 3 records", ex.Message);
        Assert.Contains("add filters or totals", ex.Message);
        Assert.Equal(3, f.Executor.ReceivedMaxRows);
    }

    [Fact]
    public async Task Data_source_limit_beats_the_host_default()
    {
        var (f, viz) = Arrange();
        f.DataSource.MaxRecords = 2;
        f.Limits = new RecordLimitSettings(500);
        f.Executor.Result = VizFixture.Result([Row("A", 1), Row("B", 2)], truncated: true);

        var ex = await Assert.ThrowsAsync<RecordLimitExceededException>(() => Render(f, viz));

        Assert.Contains("more than 2 records", ex.Message);
    }

    [Fact]
    public async Task Truncated_at_the_chart_cap_without_a_limit_reports_too_many_points()
    {
        var (f, viz) = Arrange();
        f.Executor.Result = VizFixture.Result(Enumerable.Range(0, 1000).Select(i => Row($"R{i}", i)), truncated: true);

        var ex = await Assert.ThrowsAsync<RecordLimitExceededException>(() => Render(f, viz));

        Assert.Equal("This chart has too many points to chart (max 1,000) — add filters or totals.", ex.Message);
        Assert.Equal(RecordLimits.ChartMaxRows, f.Executor.ReceivedMaxRows);
    }

    [Fact]
    public async Task A_record_limit_above_the_chart_cap_still_reports_the_chart_cap()
    {
        var (f, viz) = Arrange();
        f.Limits = new RecordLimitSettings(50_000);
        f.Executor.Result = VizFixture.Result([Row("A", 1)], truncated: true);

        var ex = await Assert.ThrowsAsync<RecordLimitExceededException>(() => Render(f, viz));

        Assert.Contains("too many points to chart (max 1,000)", ex.Message);
        Assert.Equal(1000, f.Executor.ReceivedMaxRows);
    }

    [Fact]
    public async Task Only_referenced_columns_are_returned()
    {
        var (f, viz) = Arrange();

        var data = await Render(f, viz);

        Assert.Equal(["Region", "Total"], data.Columns.Select(c => c.Name));
        Assert.All(data.Rows, r => Assert.Equal(["Region", "Total"], r.Keys.OrderBy(k => k == "Region" ? 0 : 1)));
        Assert.DoesNotContain(data.Rows, r => r.ContainsKey("HiddenNote") || r.ContainsKey("Channel"));
    }

    [Fact]
    public async Task Pie_with_thirteen_categories_is_refused()
    {
        var (f, viz) = Arrange(type: VisualizationType.Pie);
        f.Executor.Result = VizFixture.Result(Enumerable.Range(0, 13).Select(i => Row($"R{i}", i + 1)));

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("max 12", ex.Message);
    }

    [Fact]
    public async Task Pie_with_twelve_categories_renders()
    {
        var (f, viz) = Arrange(type: VisualizationType.Pie);
        f.Executor.Result = VizFixture.Result(Enumerable.Range(0, 12).Select(i => Row($"R{i}", i + 1)));

        var data = await Render(f, viz);

        Assert.Equal(12, data.RowCount);
    }

    [Fact]
    public async Task Pie_with_a_negative_value_is_refused()
    {
        var (f, viz) = Arrange(type: VisualizationType.Pie);
        f.Executor.Result = VizFixture.Result([Row("A", 5), Row("B", -1)]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("negative", ex.Message);
    }

    [Fact]
    public async Task Counter_with_no_rows_returns_empty_data()
    {
        var (f, viz) = Arrange(type: VisualizationType.Counter,
            options: new VisualizationOptions { Counter = new CounterOptions { Value = "Total" } });
        f.Executor.Result = VizFixture.Result([], false, ("Total", ColumnDataType.Number));

        var data = await Render(f, viz);

        Assert.Equal(0, data.RowCount);
        Assert.Empty(data.Rows);
    }

    [Fact]
    public async Task Counter_returns_only_its_value_column()
    {
        var (f, viz) = Arrange(type: VisualizationType.Counter,
            options: new VisualizationOptions { Counter = new CounterOptions { Value = "Total" } });

        var data = await Render(f, viz);

        var row = Assert.Single(data.Rows);
        Assert.Equal(["Total"], row.Keys);
    }

    [Fact]
    public async Task Null_measure_is_passed_through_not_replaced_by_zero()
    {
        var (f, viz) = Arrange();
        f.Executor.Result = VizFixture.Result([Row(null, null), Row("US", 4)]);

        var data = await Render(f, viz);

        Assert.Null(data.Rows[0]["Total"]);
        Assert.Null(data.Rows[0]["Region"]);
    }

    [Fact]
    public async Task Defaults_fill_missing_parameters_and_supplied_values_win()
    {
        var (f, viz) = Arrange();
        var definition = VizFixture.Definition();
        definition.Parameters =
        [
            new QueryParameter { Name = "pid", DataType = ColumnDataType.Number, DefaultValue = "10" },
            new QueryParameter { Name = "region", DataType = ColumnDataType.Text, DefaultValue = "EU" }
        ];
        definition.Filters.Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = "ProgramId", DataType = ColumnDataType.Number, IsParameterized = true, ParameterName = "pid"
        });
        definition.Filters.Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = "RegionCode", DataType = ColumnDataType.Text, IsParameterized = true, ParameterName = "region"
        });
        f.Query.DefinitionJson = QueryDefinitionSerializer.Serialize(definition);

        await Render(f, viz, new Dictionary<string, string?> { ["region"] = "US" });

        var bound = f.Builders.Generated!.Parameters;
        Assert.Equal(["10", "US"], bound.Select(p => p.LiteralValue));
        Assert.All(bound, p => Assert.False(p.IsRuntimeParameter));
        Assert.DoesNotContain("US", f.Builders.Generated.Sql);
    }

    [Fact]
    public async Task Render_is_audited_with_server_verified_query_id_and_visualization_id()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser);

        await Render(f, viz);

        var entry = Assert.Single(f.Audit.Entries);
        Assert.Equal(AuditAction.QueryRun, entry.Action);
        Assert.Equal(f.Query.Id, entry.EntityId);
        var details = entry.Details!;
        Assert.Equal(viz.Id, details.GetType().GetProperty("VisualizationId")!.GetValue(details));
        Assert.Equal(2, details.GetType().GetProperty("RowCount")!.GetValue(details));
        Assert.NotNull(details.GetType().GetProperty("Scope"));
    }

    private static VisualizationOptions PivotOptions() => new()
    {
        Pivot = new PivotOptions { Rows = ["Region"], Column = "Channel", Values = ["Total"] }
    };

    private static Dictionary<string, object?> ChannelRow(int i) => Row($"R{i % 3}", i, channel: $"C{i}");

    [Fact]
    public async Task Table_over_the_cap_returns_rows_and_truncated_flag()
    {
        var (f, viz) = Arrange(type: VisualizationType.Table,
            options: new VisualizationOptions { Table = new TableOptions { Columns = ["Region", "Total"] } });
        f.Limits = new RecordLimitSettings(2);
        f.Executor.Result = VizFixture.Result([Row("A", 1), Row("B", 2)], truncated: true);

        var data = await Render(f, viz);

        Assert.True(data.Truncated);
        Assert.Equal(2, data.RowCount);
        Assert.Equal(2, data.RowLimit);
    }

    [Fact]
    public async Task Table_within_the_cap_is_not_flagged_truncated()
    {
        var (f, viz) = Arrange(type: VisualizationType.Table,
            options: new VisualizationOptions { Table = new TableOptions { Columns = ["Region", "Total"] } });

        var data = await Render(f, viz);

        Assert.False(data.Truncated);
    }

    [Fact]
    public async Task Pivot_over_the_cap_is_refused()
    {
        var (f, viz) = Arrange(type: VisualizationType.Pivot, options: PivotOptions());
        f.Executor.Result = VizFixture.Result([Row("A", 1)], truncated: true);

        await Assert.ThrowsAsync<RecordLimitExceededException>(() => Render(f, viz));
    }

    [Fact]
    public async Task Bar_over_the_cap_is_still_refused()
    {
        var (f, viz) = Arrange();
        f.Executor.Result = VizFixture.Result([Row("A", 1)], truncated: true);

        await Assert.ThrowsAsync<RecordLimitExceededException>(() => Render(f, viz));
    }

    [Fact]
    public async Task Pivot_with_51_distinct_columns_is_refused()
    {
        var (f, viz) = Arrange(type: VisualizationType.Pivot, options: PivotOptions());
        f.Executor.Result = VizFixture.Result(Enumerable.Range(0, 51).Select(ChannelRow));

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("50", ex.Message);
    }

    [Fact]
    public async Task Pivot_with_50_distinct_columns_renders()
    {
        var (f, viz) = Arrange(type: VisualizationType.Pivot, options: PivotOptions());
        f.Executor.Result = VizFixture.Result(Enumerable.Range(0, 50).Select(ChannelRow));

        var data = await Render(f, viz);

        Assert.Equal(50, data.RowCount);
    }

    [Fact]
    public async Task Funnel_with_21_steps_is_refused()
    {
        var (f, viz) = Arrange(type: VisualizationType.Funnel);
        f.Executor.Result = VizFixture.Result(Enumerable.Range(0, 21).Select(i => Row($"S{i}", 100 - i)));

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("max 20", ex.Message);
    }

    private const string HugeNegativeNumericText = "-1000000000000000000000000000000000000000";

    [Fact]
    public async Task Funnel_refuses_huge_negative_numeric_text()
    {
        var (f, viz) = Arrange(type: VisualizationType.Funnel);
        var huge = Row("B", 1);
        huge["Total"] = HugeNegativeNumericText;
        f.Executor.Result = VizFixture.Result([Row("A", 5), huge]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("cannot show negative values", ex.Message);
    }

    [Fact]
    public async Task Funnel_refuses_tiny_negative_numeric_text()
    {
        var (f, viz) = Arrange(type: VisualizationType.Funnel);
        var tiny = Row("B", 1);
        tiny["Total"] = "-0.00000000000000000000000000000000001";
        f.Executor.Result = VizFixture.Result([Row("A", 5), tiny]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("cannot show negative values", ex.Message);
    }

    [Theory]
    [InlineData(-1e-35)]
    [InlineData(-1e-300)]
    public async Task Funnel_refuses_a_tiny_negative_double(double tinyNegative)
    {
        var (f, viz) = Arrange(type: VisualizationType.Funnel);
        var tiny = Row("B", 1);
        tiny["Total"] = tinyNegative;
        f.Executor.Result = VizFixture.Result([Row("A", 5), tiny]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("cannot show negative values", ex.Message);
    }

    [Theory]
    [InlineData("1000000000000000000000000000000000000000")]
    [InlineData("0.00000000000000000000000000000000001")]
    public async Task Funnel_accepts_huge_or_tiny_positive_numeric_text(string text)
    {
        var (f, viz) = Arrange(type: VisualizationType.Funnel);
        var positive = Row("B", 1);
        positive["Total"] = text;
        f.Executor.Result = VizFixture.Result([Row("A", 5), positive]);

        var data = await Render(f, viz);

        Assert.Equal(2, data.RowCount);
    }

    [Fact]
    public async Task Pie_refuses_huge_negative_numeric_text()
    {
        var (f, viz) = Arrange(type: VisualizationType.Pie);
        var huge = Row("B", 1);
        huge["Total"] = HugeNegativeNumericText;
        f.Executor.Result = VizFixture.Result([Row("A", 5), huge]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("cannot show negative values", ex.Message);
    }

    [Fact]
    public async Task Funnel_with_a_negative_value_is_refused()
    {
        var (f, viz) = Arrange(type: VisualizationType.Funnel);
        f.Executor.Result = VizFixture.Result([Row("A", 5), Row("B", -1)]);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("negative", ex.Message);
    }

    [Fact]
    public async Task Waterfall_with_31_bars_is_refused()
    {
        var (f, viz) = Arrange(type: VisualizationType.Waterfall);
        f.Executor.Result = VizFixture.Result(Enumerable.Range(0, 31).Select(i => Row($"S{i}", i)));

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("max 30", ex.Message);
    }

    [Fact]
    public async Task Waterfall_with_a_negative_amount_renders()
    {
        var (f, viz) = Arrange(type: VisualizationType.Waterfall);
        f.Executor.Result = VizFixture.Result([Row("A", 5), Row("B", -3)]);

        var data = await Render(f, viz);

        Assert.Equal(2, data.RowCount);
    }

    private static VisualizationOptions ScatterOptions() => new()
    {
        X = "Orders", Measures = ["Total"], Size = "Avg1", Label = "Region"
    };

    [Fact]
    public async Task Scatter_with_negative_size_is_refused()
    {
        var (f, viz) = Arrange(type: VisualizationType.Scatter, options: ScatterOptions());
        var bad = Row("A", 5);
        bad["Avg1"] = -2m;
        f.Executor.Result = VizFixture.Result([bad], false,
            ("Region", ColumnDataType.Text), ("Orders", ColumnDataType.Number),
            ("Total", ColumnDataType.Number), ("Avg1", ColumnDataType.Number));

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("Bubble size cannot be negative", ex.Message);
    }

    public static TheoryData<VisualizationType, string[]> NewTypeCases => new()
    {
        { VisualizationType.Combo, ["Region", "Total", "Orders"] },
        { VisualizationType.Scatter, ["Region", "Orders", "Total", "Avg1"] },
        { VisualizationType.Table, ["Region", "Total"] },
        { VisualizationType.Pivot, ["Region", "Channel", "Total"] },
        { VisualizationType.Funnel, ["Region", "Total"] },
        { VisualizationType.Waterfall, ["Region", "Total"] },
        { VisualizationType.Trend, ["Orders", "Total"] },
        { VisualizationType.HorizontalBar, ["Region", "Total"] },
        { VisualizationType.Area, ["Region", "Total"] }
    };

    private static VisualizationOptions OptionsFor(VisualizationType type) => type switch
    {
        VisualizationType.Combo => new() { X = "Region", Measures = ["Total", "Orders"], LineMeasures = ["Orders"] },
        VisualizationType.Scatter => ScatterOptions(),
        VisualizationType.Table => new() { Table = new TableOptions { Columns = ["Region", "Total"] } },
        VisualizationType.Pivot => PivotOptions(),
        VisualizationType.Trend => new() { X = "Orders", Measures = ["Total"] },
        _ => VizFixture.BarOptions()
    };

    private static Dictionary<string, object?> WideRow(decimal n) =>
        new()
        {
            ["Region"] = "EU", ["Channel"] = "Web", ["Total"] = n, ["Orders"] = n, ["Avg1"] = n,
            ["Min1"] = n, ["Max1"] = n, ["HiddenNote"] = "secret"
        };

    private static QueryResultSet WideResult(params decimal[] values) => VizFixture.Result(
        values.Select(WideRow), false,
        ("Region", ColumnDataType.Text), ("Channel", ColumnDataType.Text), ("Total", ColumnDataType.Number),
        ("Orders", ColumnDataType.Number), ("Avg1", ColumnDataType.Number), ("Min1", ColumnDataType.Number),
        ("Max1", ColumnDataType.Number), ("HiddenNote", ColumnDataType.Text));

    [Theory]
    [MemberData(nameof(NewTypeCases))]
    public async Task Render_projects_only_each_types_role_columns(VisualizationType type, string[] expected)
    {
        var (f, viz) = Arrange(type: type, options: OptionsFor(type));
        f.Executor.Result = WideResult(1, 2);

        var data = await Render(f, viz);

        Assert.Equal(expected.Order(), data.Columns.Select(c => c.Name).Order());
        Assert.All(data.Rows, r =>
        {
            Assert.Equal(expected.Order(), r.Keys.Order());
            Assert.DoesNotContain("HiddenNote", r.Keys);
            Assert.DoesNotContain("Min1", r.Keys);
        });
    }

    [Fact]
    public async Task Counter_with_target_returns_one_row()
    {
        var (f, viz) = Arrange(type: VisualizationType.Counter,
            options: new VisualizationOptions { Counter = new CounterOptions { Value = "Total", Target = "Orders" } });
        f.Executor.Result = WideResult(1, 2, 3);

        var data = await Render(f, viz);

        var row = Assert.Single(data.Rows);
        Assert.Equal(["Total", "Orders"], row.Keys);
    }

    [Fact]
    public async Task Trend_returns_all_rows()
    {
        var (f, viz) = Arrange(type: VisualizationType.Trend, options: OptionsFor(VisualizationType.Trend));
        f.Executor.Result = WideResult(1, 2, 3);

        var data = await Render(f, viz);

        Assert.Equal(3, data.RowCount);
    }

    [Fact]
    public async Task Columns_carry_the_aggregate_of_the_definition()
    {
        var (f, viz) = Arrange(type: VisualizationType.Table,
            options: new VisualizationOptions { Table = new TableOptions { Columns = ["Region", "Total", "Avg1"] } });
        f.Executor.Result = WideResult(1);

        var data = await Render(f, viz);

        Assert.Equal(AggregateFunction.None, data.Columns.Single(c => c.Name == "Region").Aggregate);
        Assert.Equal(AggregateFunction.Sum, data.Columns.Single(c => c.Name == "Total").Aggregate);
        Assert.Equal(AggregateFunction.Avg, data.Columns.Single(c => c.Name == "Avg1").Aggregate);
    }

    [Theory]
    [MemberData(nameof(NewTypeCases))]
    public async Task Scope_predicates_reach_build_for_every_new_type(VisualizationType type, string[] _)
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser, type, OptionsFor(type));
        f.Executor.Result = WideResult(1, 2);
        var predicates = new[] { new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, ["10"]) };
        f.Guard.Predicates = predicates;

        await Render(f, viz);

        Assert.Same(predicates, f.Builders.ReceivedScope);
    }

    [Fact]
    public async Task Scope_predicates_reach_build_for_a_counter_with_a_target()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser, VisualizationType.Counter,
            new VisualizationOptions { Counter = new CounterOptions { Value = "Total", Target = "Orders" } });
        f.Executor.Result = WideResult(1, 2);
        var predicates = new[] { new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, ["10"]) };
        f.Guard.Predicates = predicates;

        await Render(f, viz);

        Assert.Same(predicates, f.Builders.ReceivedScope);
    }

    [Theory]
    [InlineData("size")]
    [InlineData("label")]
    [InlineData("table")]
    [InlineData("pivotRow")]
    [InlineData("pivotColumn")]
    [InlineData("pivotValue")]
    [InlineData("counterTarget")]
    [InlineData("comboMeasure")]
    public async Task Removed_column_in_any_role_says_needs_updating(string role)
    {
        var (type, options, removed) = role switch
        {
            "size" => (VisualizationType.Scatter, ScatterOptions(), "Avg1"),
            "label" => (VisualizationType.Scatter, ScatterOptions(), "Region"),
            "table" => (VisualizationType.Table, OptionsFor(VisualizationType.Table), "Total"),
            "pivotRow" => (VisualizationType.Pivot, PivotOptions(), "Region"),
            "pivotColumn" => (VisualizationType.Pivot, PivotOptions(), "Channel"),
            "pivotValue" => (VisualizationType.Pivot, PivotOptions(), "Total"),
            "counterTarget" => (VisualizationType.Counter,
                new VisualizationOptions { Counter = new CounterOptions { Value = "Total", Target = "Orders" } }, "Orders"),
            _ => (VisualizationType.Combo, OptionsFor(VisualizationType.Combo), "Orders")
        };
        var (f, viz) = Arrange(type: type, options: options);
        var definition = VizFixture.Definition();
        definition.Columns.RemoveAll(c => c.EffectiveAlias == removed);
        f.Query.DefinitionJson = QueryDefinitionSerializer.Serialize(definition);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("needs updating", ex.Message);
        Assert.Contains(removed, ex.Message);
        Assert.Equal(0, f.Executor.Calls);
    }

    [Theory]
    [InlineData("barLabel")]
    [InlineData("barSize")]
    [InlineData("barStaleSize")]
    [InlineData("barLineMeasures")]
    [InlineData("tableMeasures")]
    public async Task Leftover_roles_of_another_type_are_neither_returned_nor_validated(string leftover)
    {
        var (type, options, expected) = leftover switch
        {
            "barLabel" => (VisualizationType.Bar,
                new VisualizationOptions { X = "Region", Measures = ["Total"], Label = "Channel" }, new[] { "Region", "Total" }),
            "barSize" => (VisualizationType.Bar,
                new VisualizationOptions { X = "Region", Measures = ["Total"], Size = "Channel" }, new[] { "Region", "Total" }),
            "barStaleSize" => (VisualizationType.Bar,
                new VisualizationOptions { X = "Region", Measures = ["Total"], Size = "Gone" }, new[] { "Region", "Total" }),
            "barLineMeasures" => (VisualizationType.Bar,
                new VisualizationOptions { X = "Region", Measures = ["Total"], LineMeasures = ["Orders"] }, new[] { "Region", "Total" }),
            _ => (VisualizationType.Table,
                new VisualizationOptions { Measures = ["Region"], Table = new TableOptions { Columns = ["Region", "Total"] } },
                new[] { "Region", "Total" })
        };
        var (f, viz) = Arrange(type: type, options: options);
        f.Executor.Result = WideResult(1, 2);

        var data = await Render(f, viz);

        Assert.Equal(expected, data.Columns.Select(c => c.Name));
        Assert.All(data.Rows, r => Assert.Equal(expected.Order(), r.Keys.Order()));
    }

    [Fact]
    public async Task A_referenced_name_shared_by_two_visible_columns_needs_updating()
    {
        var (f, viz) = Arrange(options: new VisualizationOptions { X = "Region", Measures = ["Value"] });
        var definition = VizFixture.Definition();
        definition.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Alias = "Value", Aggregate = AggregateFunction.Sum, DataType = ColumnDataType.Number });
        definition.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "Price", Alias = "Value", Aggregate = AggregateFunction.Avg, DataType = ColumnDataType.Number });
        f.Query.DefinitionJson = QueryDefinitionSerializer.Serialize(definition);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(f, viz));

        Assert.Contains("needs updating", ex.Message);
        Assert.Contains("Value", ex.Message);
        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Scatter_with_an_empty_size_name_renders_without_a_size_column()
    {
        var (f, viz) = Arrange(type: VisualizationType.Scatter,
            options: new VisualizationOptions { X = "Orders", Measures = ["Total"], Size = "" });
        f.Executor.Result = WideResult(1, 2);

        var data = await Render(f, viz);

        Assert.Equal(2, data.RowCount);
    }

    [Fact]
    public async Task Pivot_with_an_empty_column_name_renders()
    {
        var (f, viz) = Arrange(type: VisualizationType.Pivot, options: new VisualizationOptions
        {
            Pivot = new PivotOptions { Rows = ["Region"], Column = "", Values = ["Total"] }
        });
        f.Executor.Result = WideResult(1, 2);

        var data = await Render(f, viz);

        Assert.Equal(2, data.RowCount);
    }

    [Fact]
    public async Task Forbidden_render_is_not_audited()
    {
        var (f, viz) = Arrange(VizFixture.Stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => Render(f, viz));

        Assert.Empty(f.Audit.Entries);
    }
}
