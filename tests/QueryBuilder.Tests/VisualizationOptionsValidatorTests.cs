using QueryBuilder.Application.Common;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests;

public class VisualizationOptionsValidatorTests
{
    private static QueryDefinition Definition() => VizFixture.Definition();

    private static VisualizationOptions Bar(string x = "Region", params string[] measures) => new()
    {
        X = x,
        Measures = measures.Length == 0 ? ["Total"] : [.. measures]
    };

    private static IReadOnlyList<VisualizationIssue> Validate(VisualizationType type, VisualizationOptions options) =>
        VisualizationOptionsValidator.Validate(type, options, Definition());

    [Fact]
    public void Valid_bar_has_no_issues() =>
        Assert.Empty(Validate(VisualizationType.Bar, Bar()));

    [Fact]
    public void Unknown_column_is_reported_as_missing()
    {
        var issues = Validate(VisualizationType.Bar, Bar(x: "Nope"));

        var issue = Assert.Single(issues);
        Assert.Equal("Nope", issue.MissingColumn);
    }

    [Fact]
    public void Hidden_column_is_not_a_visible_output()
    {
        var issues = Validate(VisualizationType.Bar, Bar(x: "HiddenNote"));

        Assert.Contains(issues, i => i.MissingColumn == "HiddenNote");
    }

    [Fact]
    public void Non_numeric_measure_is_rejected()
    {
        var issues = Validate(VisualizationType.Bar, Bar(measures: "Region"));

        Assert.Contains(issues, i => i.Message.Contains("'Region' is not a number column"));
    }

    [Fact]
    public void Measures_must_be_one_to_five()
    {
        Assert.NotEmpty(Validate(VisualizationType.Bar, new VisualizationOptions { X = "Region", Measures = [] }));
        Assert.Empty(Validate(VisualizationType.Bar, Bar(measures: ["Total", "Orders", "Avg1", "Min1", "Max1"])));
        Assert.NotEmpty(Validate(VisualizationType.Bar, Bar(measures: ["Total", "Orders", "Avg1", "Min1", "Max1", "Extra"])));
    }

    [Fact]
    public void Series_requires_exactly_one_measure()
    {
        var options = Bar(measures: ["Total", "Orders"]);
        options.Series = "Channel";

        Assert.Contains(Validate(VisualizationType.Bar, options), i => i.Message.Contains("exactly one measure"));

        options.Measures = ["Total"];
        Assert.Empty(Validate(VisualizationType.Bar, options));
    }

    [Fact]
    public void Pie_needs_exactly_one_measure_and_no_series()
    {
        Assert.Contains(Validate(VisualizationType.Pie, Bar(measures: ["Total", "Orders"])),
            i => i.Message.Contains("exactly one measure"));

        var withSeries = Bar();
        withSeries.Series = "Channel";
        Assert.NotEmpty(Validate(VisualizationType.Pie, withSeries));

        Assert.Empty(Validate(VisualizationType.Pie, Bar()));
    }

    [Fact]
    public void Counter_requires_a_numeric_value_column()
    {
        Assert.NotEmpty(Validate(VisualizationType.Counter, new VisualizationOptions()));
        Assert.NotEmpty(Validate(VisualizationType.Counter,
            new VisualizationOptions { Counter = new CounterOptions { Value = "Region" } }));
        Assert.Empty(Validate(VisualizationType.Counter,
            new VisualizationOptions { Counter = new CounterOptions { Value = "Total", Decimals = 2 } }));
        Assert.NotEmpty(Validate(VisualizationType.Counter,
            new VisualizationOptions { Counter = new CounterOptions { Value = "Total", Decimals = 5 } }));
    }

    [Fact]
    public void Unknown_version_is_rejected()
    {
        var options = Bar();
        options.Version = 2;

        var issue = Assert.Single(Validate(VisualizationType.Bar, options));
        Assert.Contains("version", issue.Message);
    }

    [Fact]
    public void Only_bar_can_be_stacked()
    {
        var options = Bar();
        options.Stacked = true;

        Assert.Empty(Validate(VisualizationType.Bar, options));
        Assert.NotEmpty(Validate(VisualizationType.Line, options));
    }

    [Fact]
    public void Stale_chart_is_detected_when_column_is_removed_from_the_query()
    {
        var definition = Definition();
        definition.Columns.RemoveAll(c => c.EffectiveAlias == "Total");

        var issues = VisualizationOptionsValidator.Validate(VisualizationType.Bar, Bar(), definition);

        Assert.Contains(issues, i => i.MissingColumn == "Total");
    }

    [Fact]
    public void Referenced_columns_are_distinct_and_ordered()
    {
        var options = Bar(measures: ["Total", "Orders"]);

        Assert.Equal(["Region", "Total", "Orders"], VisualizationOptionsValidator.ReferencedColumns(VisualizationType.Bar, options));
    }

    public static TheoryData<VisualizationType, VisualizationOptions> ValidNewShapes => new()
    {
        { VisualizationType.Area, Bar() },
        { VisualizationType.HorizontalBar, Bar() },
        { VisualizationType.Area, new VisualizationOptions { X = "Region", Measures = ["Total"], Stacked = true, Percent = true } },
        { VisualizationType.HorizontalBar, new VisualizationOptions { X = "Region", Measures = ["Total"], Stacked = true, Percent = true } },
        { VisualizationType.Combo, new VisualizationOptions { X = "Region", Measures = ["Total", "Orders"], LineMeasures = ["Orders"] } },
        { VisualizationType.Table, new VisualizationOptions { Table = new TableOptions { Columns = ["Region", "Total"] } } },
        { VisualizationType.Pivot, new VisualizationOptions { Pivot = new PivotOptions { Rows = ["Region"], Column = "Channel", Values = ["Total"] } } },
        { VisualizationType.Funnel, Bar() },
        { VisualizationType.Waterfall, Bar() },
        { VisualizationType.Scatter, new VisualizationOptions { X = "Orders", Measures = ["Total"], Size = "Avg1", Label = "Region" } },
        { VisualizationType.Trend, new VisualizationOptions { X = "Orders", Measures = ["Total"] } },
        { VisualizationType.Counter, new VisualizationOptions { Counter = new CounterOptions { Value = "Total", Target = "Orders" } } },
    };

    [Theory]
    [MemberData(nameof(ValidNewShapes))]
    public void New_shapes_are_valid(VisualizationType type, VisualizationOptions options) =>
        Assert.Empty(Validate(type, options));

    [Fact]
    public void Percent_without_stacked_is_rejected()
    {
        var options = Bar();
        options.Percent = true;

        Assert.NotEmpty(Validate(VisualizationType.Bar, options));
    }

    [Fact]
    public void Line_still_cannot_be_stacked()
    {
        var options = Bar();
        options.Stacked = true;
        options.Percent = true;

        Assert.Contains(Validate(VisualizationType.Line, options), i => i.Message == "Only bar charts can be stacked.");
    }

    [Fact]
    public void Combo_needs_at_least_one_line_and_one_bar()
    {
        Assert.NotEmpty(Validate(VisualizationType.Combo, Bar(measures: ["Total", "Orders"])));
        Assert.NotEmpty(Validate(VisualizationType.Combo, new VisualizationOptions
        {
            X = "Region", Measures = ["Total", "Orders"], LineMeasures = ["Total", "Orders"]
        }));
    }

    [Fact]
    public void Combo_line_measure_must_be_a_measure()
    {
        var options = new VisualizationOptions { X = "Region", Measures = ["Total", "Orders"], LineMeasures = ["Avg1"] };

        Assert.NotEmpty(Validate(VisualizationType.Combo, options));
    }

    [Fact]
    public void Combo_cannot_have_a_series()
    {
        var options = new VisualizationOptions
        {
            X = "Region", Measures = ["Total", "Orders"], LineMeasures = ["Orders"], Series = "Channel"
        };

        Assert.NotEmpty(Validate(VisualizationType.Combo, options));
    }

    [Fact]
    public void Scatter_x_must_be_number_or_date()
    {
        var options = new VisualizationOptions { X = "Region", Measures = ["Total"] };

        Assert.NotEmpty(Validate(VisualizationType.Scatter, options));
    }

    [Fact]
    public void Scatter_size_must_be_number()
    {
        var options = new VisualizationOptions { X = "Orders", Measures = ["Total"], Size = "Region" };

        Assert.NotEmpty(Validate(VisualizationType.Scatter, options));
    }

    [Theory]
    [InlineData(VisualizationType.Funnel)]
    [InlineData(VisualizationType.Waterfall)]
    [InlineData(VisualizationType.Trend)]
    public void Funnel_waterfall_and_trend_need_exactly_one_measure_and_no_series(VisualizationType type)
    {
        var x = type == VisualizationType.Trend ? "Orders" : "Region";

        Assert.NotEmpty(Validate(type, new VisualizationOptions { X = x, Measures = ["Total", "Avg1"] }));
        Assert.NotEmpty(Validate(type, new VisualizationOptions { X = x, Measures = ["Total"], Series = "Channel" }));
        Assert.Empty(Validate(type, new VisualizationOptions { X = x, Measures = ["Total"] }));
    }

    [Fact]
    public void Trend_x_must_be_number_or_date()
    {
        Assert.NotEmpty(Validate(VisualizationType.Trend, Bar()));
    }

    [Fact]
    public void Table_needs_1_to_30_columns()
    {
        Assert.NotEmpty(Validate(VisualizationType.Table, new VisualizationOptions()));
        Assert.NotEmpty(Validate(VisualizationType.Table, new VisualizationOptions { Table = new TableOptions() }));

        var definition = Definition();
        definition.Columns = Enumerable.Range(0, 31).Select(i => new QueryColumn
        {
            TableAlias = "t0", ColumnName = $"C{i}", Alias = $"C{i}", DataType = ColumnDataType.Text
        }).ToList();
        var names = definition.Columns.Select(c => c.EffectiveAlias).ToList();

        Assert.Empty(VisualizationOptionsValidator.Validate(VisualizationType.Table,
            new VisualizationOptions { Table = new TableOptions { Columns = names.Take(30).ToList() } }, definition));
        Assert.NotEmpty(VisualizationOptionsValidator.Validate(VisualizationType.Table,
            new VisualizationOptions { Table = new TableOptions { Columns = names } }, definition));
    }

    [Fact]
    public void Table_column_must_be_visible()
    {
        var options = new VisualizationOptions { Table = new TableOptions { Columns = ["Region", "HiddenNote"] } };

        Assert.Contains(Validate(VisualizationType.Table, options), i => i.MissingColumn == "HiddenNote");
    }

    [Fact]
    public void Pivot_limits()
    {
        static VisualizationOptions Pivot(string[] rows, string[] values) => new()
        {
            Pivot = new PivotOptions { Rows = [.. rows], Values = [.. values] }
        };

        Assert.NotEmpty(Validate(VisualizationType.Pivot, Pivot([], ["Total"])));
        Assert.NotEmpty(Validate(VisualizationType.Pivot, Pivot(["Region", "Channel", "HiddenNote", "Region"], ["Total"])));
        Assert.Empty(Validate(VisualizationType.Pivot, Pivot(["Region", "Channel"], ["Total"])));
        Assert.NotEmpty(Validate(VisualizationType.Pivot, Pivot(["Region"], [])));
        Assert.NotEmpty(Validate(VisualizationType.Pivot, Pivot(["Region"], ["Total", "Orders", "Avg1", "Min1", "Max1", "Extra"])));
        Assert.Empty(Validate(VisualizationType.Pivot, Pivot(["Region"], ["Total", "Orders", "Avg1", "Min1", "Max1"])));
    }

    [Fact]
    public void Pivot_value_must_be_number()
    {
        var options = new VisualizationOptions { Pivot = new PivotOptions { Rows = ["Region"], Values = ["Channel"] } };

        Assert.Contains(Validate(VisualizationType.Pivot, options), i => i.Message.Contains("'Channel' is not a number column"));
    }

    [Fact]
    public void Pivot_column_cannot_have_two_roles()
    {
        var options = new VisualizationOptions { Pivot = new PivotOptions { Rows = ["Region"], Column = "Region", Values = ["Total"] } };

        Assert.NotEmpty(Validate(VisualizationType.Pivot, options));
    }

    [Fact]
    public void Counter_target_must_be_number()
    {
        var options = new VisualizationOptions { Counter = new CounterOptions { Value = "Total", Target = "Region" } };

        Assert.NotEmpty(Validate(VisualizationType.Counter, options));
    }

    [Theory]
    [InlineData("us", false)]
    [InlineData("US", false)]
    [InlineData(null, false)]
    [InlineData("USD", true)]
    public void Currency_style_needs_three_uppercase_letters(string? currency, bool valid)
    {
        var options = Bar();
        options.Format = new ValueFormat { Style = ValueFormatStyle.Currency, Currency = currency };

        Assert.Equal(valid, Validate(VisualizationType.Bar, options).Count == 0);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Format_decimals_are_0_to_4(int decimals, bool valid)
    {
        var options = Bar();
        options.Format = new ValueFormat { Style = ValueFormatStyle.Number, Decimals = decimals };

        Assert.Equal(valid, Validate(VisualizationType.Bar, options).Count == 0);
    }

    public static TheoryData<VisualizationType, VisualizationOptions, string[]> RoleColumns => new()
    {
        { VisualizationType.Bar, new VisualizationOptions { X = "Region", Measures = ["Total"], Size = "Avg1", Label = "Channel", LineMeasures = ["Total"] }, ["Region", "Total"] },
        { VisualizationType.Table, new VisualizationOptions { Measures = ["Orders"], X = "Region", Table = new TableOptions { Columns = ["Total"] } }, ["Total"] },
        { VisualizationType.Bar, new VisualizationOptions { X = "Region", Series = "Channel", Measures = ["Total"] }, ["Region", "Channel", "Total"] },
        { VisualizationType.Combo, new VisualizationOptions { X = "Region", Measures = ["Total", "Orders"], LineMeasures = ["Orders"] }, ["Region", "Total", "Orders"] },
        { VisualizationType.Scatter, new VisualizationOptions { X = "Orders", Measures = ["Total"], Size = "Avg1", Label = "Region" }, ["Orders", "Total", "Avg1", "Region"] },
        { VisualizationType.Table, new VisualizationOptions { Table = new TableOptions { Columns = ["Region", "Total"] } }, ["Region", "Total"] },
        { VisualizationType.Pivot, new VisualizationOptions { Pivot = new PivotOptions { Rows = ["Region"], Column = "Channel", Values = ["Total"] } }, ["Region", "Channel", "Total"] },
        { VisualizationType.Counter, new VisualizationOptions { Counter = new CounterOptions { Value = "Total", Target = "Orders" } }, ["Total", "Orders"] },
        { VisualizationType.Trend, new VisualizationOptions { X = "Orders", Measures = ["Total"] }, ["Orders", "Total"] },
    };

    [Theory]
    [MemberData(nameof(RoleColumns))]
    public void ReferencedColumns_lists_every_role(VisualizationType type, VisualizationOptions options, string[] expected) =>
        Assert.Equal(expected, VisualizationOptionsValidator.ReferencedColumns(type, options));

    [Fact]
    public void A_name_shared_by_two_visible_columns_is_refused_at_validation()
    {
        var definition = Definition();
        definition.Columns.Add(new QueryColumn
        {
            TableAlias = "t0", ColumnName = "Freight", Alias = "Total", Aggregate = AggregateFunction.Sum, DataType = ColumnDataType.Number
        });

        var issues = VisualizationOptionsValidator.Validate(VisualizationType.Bar, Bar(), definition);

        Assert.Contains(issues, i => i.Message.Contains("More than one column is named Total"));
    }

    [Theory]
    [InlineData(VisualizationType.Pie)]
    [InlineData(VisualizationType.Funnel)]
    [InlineData(VisualizationType.Bar)]
    public void A_blank_measure_name_is_refused(VisualizationType type) =>
        Assert.NotEmpty(Validate(type, new VisualizationOptions { X = "Region", Measures = [""] }));

    [Fact]
    public void Blank_table_and_pivot_columns_are_refused()
    {
        Assert.NotEmpty(Validate(VisualizationType.Table, new VisualizationOptions { Table = new TableOptions { Columns = ["Region", ""] } }));
        Assert.NotEmpty(Validate(VisualizationType.Pivot, new VisualizationOptions { Pivot = new PivotOptions { Rows = [" "], Values = ["Total"] } }));
        Assert.NotEmpty(Validate(VisualizationType.Pivot, new VisualizationOptions { Pivot = new PivotOptions { Rows = ["Region"], Values = [""] } }));
    }

    [Fact]
    public void Null_lists_are_treated_as_empty_not_a_crash()
    {
        Assert.NotEmpty(Validate(VisualizationType.Pivot, new VisualizationOptions { Pivot = new PivotOptions { Rows = null!, Values = null! } }));
        Assert.NotEmpty(Validate(VisualizationType.Combo, new VisualizationOptions { X = "Region", Measures = ["Total", "Orders"], LineMeasures = null! }));
        Assert.NotEmpty(Validate(VisualizationType.Table, new VisualizationOptions { Table = new TableOptions { Columns = null! } }));
        Assert.NotEmpty(Validate(VisualizationType.Bar, new VisualizationOptions { X = "Region", Measures = null! }));
    }

    [Fact]
    public void Scatter_needs_exactly_one_measure()
    {
        var two = new VisualizationOptions { X = "Orders", Measures = ["Total", "Avg1"] };

        Assert.Contains(Validate(VisualizationType.Scatter, two), i => i.Message.Contains("exactly one measure"));
        Assert.Empty(Validate(VisualizationType.Scatter, new VisualizationOptions { X = "Orders", Measures = ["Total"] }));
    }

    [Fact]
    public void Options_saved_before_this_work_still_validate()
    {
        var options = VisualizationOptionsSerializer.Deserialize(
            """{"version":1,"x":"Region","measures":["Total"],"stacked":false,"sortX":"none"}""");

        Assert.Empty(Validate(VisualizationType.Bar, options));
    }
}
