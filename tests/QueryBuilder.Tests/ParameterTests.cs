using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Parameters;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Tests;

internal sealed class FixedClock(DateTime local) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(local, TimeSpan.Zero);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class FakeOptions(params string[] values) : IParameterOptionsService
{
    public Task<IReadOnlyList<ParameterOption>> GetOptionsAsync(QueryParameter parameter, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ParameterOption>>(values.Select(v => new ParameterOption(v, v)).ToList());
}

public class RelativeDatesTests
{
    // Wednesday 4 Nov 2026.
    private static readonly DateTime Now = new(2026, 11, 4, 13, 30, 0);

    [Theory]
    [InlineData("today", "2026-11-04", "2026-11-04")]
    [InlineData("yesterday", "2026-11-03", "2026-11-03")]
    [InlineData("this_week", "2026-11-02", "2026-11-08")]
    [InlineData("last_week", "2026-10-26", "2026-11-01")]
    [InlineData("this_month", "2026-11-01", "2026-11-30")]
    [InlineData("last_month", "2026-10-01", "2026-10-31")]
    [InlineData("this_year", "2026-01-01", "2026-12-31")]
    [InlineData("last_year", "2025-01-01", "2025-12-31")]
    [InlineData("last_7_days", "2026-10-29", "2026-11-04")]
    [InlineData("last_14_days", "2026-10-22", "2026-11-04")]
    [InlineData("last_30_days", "2026-10-06", "2026-11-04")]
    [InlineData("last_60_days", "2026-09-06", "2026-11-04")]
    [InlineData("last_90_days", "2026-08-07", "2026-11-04")]
    [InlineData("last_12_months", "2025-11-05", "2026-11-04")]
    public void Ranges_resolve_against_the_clock(string token, string start, string end)
    {
        Assert.True(RelativeDates.TryResolveRange(token, Now, out var s, out var e));
        Assert.Equal(start, s.ToString("yyyy-MM-dd"));
        Assert.Equal(end, e.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Every_listed_token_resolves_and_unknown_ones_do_not()
    {
        Assert.All(RelativeDates.RangeTokens, t => Assert.True(RelativeDates.TryResolveRange(t, Now, out _, out _)));
        Assert.False(RelativeDates.TryResolveRange("next_week", Now, out _, out _));
    }

    [Fact]
    public void Week_starts_on_monday_even_when_today_is_sunday()
    {
        var sunday = new DateTime(2026, 11, 8, 9, 0, 0);
        RelativeDates.TryResolveRange("this_week", sunday, out var s, out var e);
        Assert.Equal(new DateTime(2026, 11, 2), s);
        Assert.Equal(new DateTime(2026, 11, 8), e);
    }

    [Fact]
    public void Now_resolves_only_when_a_time_is_wanted()
    {
        Assert.True(RelativeDates.TryResolveSingle("now", Now, true, out var value));
        Assert.Equal(new DateTime(2026, 11, 4, 13, 30, 0), value);
        Assert.False(RelativeDates.TryResolveSingle("now", Now, false, out _));
    }
}

public class ParameterValidatorTests
{
    private static QueryDefinition Define(ColumnDataType column, FilterOperator op, QueryParameter parameter)
    {
        var definition = TestData.Definition();
        parameter.Name = "p";
        definition.Parameters.Add(parameter);
        definition.Filters.Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = "c", DataType = column, Operator = op, IsParameterized = true, ParameterName = "p"
        });
        return definition;
    }

    private static QueryParameter Of(ParameterType type, params string[] options) =>
        new() { Type = type, Options = [.. options], OptionsQueryId = type == ParameterType.QueryList ? Guid.NewGuid() : null };

    [Theory]
    [InlineData(ColumnDataType.Text, FilterOperator.Equals, ParameterType.Text)]
    [InlineData(ColumnDataType.Text, FilterOperator.Contains, ParameterType.Text)]
    [InlineData(ColumnDataType.Number, FilterOperator.GreaterThan, ParameterType.Number)]
    [InlineData(ColumnDataType.Date, FilterOperator.Equals, ParameterType.Date)]
    [InlineData(ColumnDataType.DateTime, FilterOperator.LessThan, ParameterType.DateTime)]
    [InlineData(ColumnDataType.Boolean, FilterOperator.Equals, ParameterType.Boolean)]
    [InlineData(ColumnDataType.Date, FilterOperator.Between, ParameterType.DateRange)]
    [InlineData(ColumnDataType.DateTime, FilterOperator.Between, ParameterType.DateTimeRange)]
    [InlineData(ColumnDataType.Number, FilterOperator.In, ParameterType.List)]
    [InlineData(ColumnDataType.Text, FilterOperator.NotIn, ParameterType.QueryList)]
    [InlineData(ColumnDataType.Text, FilterOperator.Equals, ParameterType.List)]
    public void Compatible_combinations_pass(ColumnDataType column, FilterOperator op, ParameterType type) =>
        Assert.Empty(ParameterValidator.Validate(Define(column, op, Of(type, "a"))));

    [Theory]
    [InlineData(ColumnDataType.Text, FilterOperator.Equals, ParameterType.Number)]
    [InlineData(ColumnDataType.Number, FilterOperator.Equals, ParameterType.Text)]
    [InlineData(ColumnDataType.Text, FilterOperator.Between, ParameterType.DateRange)]
    [InlineData(ColumnDataType.Number, FilterOperator.Between, ParameterType.DateRange)]
    [InlineData(ColumnDataType.Date, FilterOperator.Equals, ParameterType.DateRange)]
    [InlineData(ColumnDataType.Date, FilterOperator.In, ParameterType.Date)]
    [InlineData(ColumnDataType.Text, FilterOperator.In, ParameterType.Text)]
    [InlineData(ColumnDataType.Boolean, FilterOperator.In, ParameterType.List)]
    [InlineData(ColumnDataType.Text, FilterOperator.IsNull, ParameterType.Text)]
    public void Incompatible_combinations_are_rejected(ColumnDataType column, FilterOperator op, ParameterType type) =>
        Assert.NotEmpty(ParameterValidator.Validate(Define(column, op, Of(type, "a"))));

    [Fact]
    public void Types_are_derived_from_the_data_type_for_old_definitions()
    {
        var legacy = new QueryParameter { Name = "p", DataType = ColumnDataType.Number };
        Assert.Equal(ParameterType.Number, ParameterTypes.Effective(legacy));
        Assert.Empty(ParameterValidator.Validate(Define(ColumnDataType.Number, FilterOperator.Equals, legacy)));
    }

    [Fact]
    public void Legacy_json_without_the_new_fields_still_loads()
    {
        var json = """{"source":{"schemaName":"s","objectName":"o","alias":"t0"},"parameters":[{"name":"p","label":"P","dataType":"number","defaultValue":"3","isRequired":true}]}""";
        var parameter = Assert.Single(QueryDefinitionSerializer.Deserialize(json).Parameters);
        Assert.Null(parameter.Type);
        Assert.Empty(parameter.Options);
        Assert.False(parameter.AllowMultiple);
        Assert.Equal(ParameterType.Number, ParameterTypes.Effective(parameter));
    }

    [Fact]
    public void Allow_multiple_needs_an_in_filter()
    {
        var list = Of(ParameterType.List, "a");
        list.AllowMultiple = true;
        Assert.NotEmpty(ParameterValidator.Validate(Define(ColumnDataType.Text, FilterOperator.Equals, list)));

        var multi = Of(ParameterType.List, "a");
        multi.AllowMultiple = true;
        Assert.Empty(ParameterValidator.Validate(Define(ColumnDataType.Text, FilterOperator.In, multi)));
    }

    [Fact]
    public void Allow_multiple_on_a_non_list_type_is_rejected()
    {
        var text = Of(ParameterType.Text);
        text.AllowMultiple = true;
        Assert.NotEmpty(ParameterValidator.Validate(Define(ColumnDataType.Text, FilterOperator.Equals, text)));
    }

    [Fact]
    public void A_list_needs_one_to_500_non_blank_short_options()
    {
        Assert.NotEmpty(ParameterValidator.Validate(Define(ColumnDataType.Text, FilterOperator.Equals, Of(ParameterType.List))));
        Assert.NotEmpty(ParameterValidator.Validate(Define(ColumnDataType.Text, FilterOperator.Equals, Of(ParameterType.List, " "))));
        Assert.NotEmpty(ParameterValidator.Validate(Define(ColumnDataType.Text, FilterOperator.Equals, Of(ParameterType.List, new string('x', 201)))));
        Assert.NotEmpty(ParameterValidator.Validate(Define(ColumnDataType.Text, FilterOperator.Equals,
            Of(ParameterType.List, Enumerable.Range(0, 501).Select(i => $"o{i}").ToArray()))));
        Assert.Empty(ParameterValidator.Validate(Define(ColumnDataType.Text, FilterOperator.Equals,
            Of(ParameterType.List, Enumerable.Range(0, 500).Select(i => $"o{i}").ToArray()))));
    }

    [Fact]
    public void A_query_list_needs_a_source_query()
    {
        var parameter = Of(ParameterType.QueryList);
        parameter.OptionsQueryId = null;
        Assert.NotEmpty(ParameterValidator.Validate(Define(ColumnDataType.Text, FilterOperator.Equals, parameter)));
    }

    [Fact]
    public void Names_must_be_valid_and_unique()
    {
        var definition = Define(ColumnDataType.Text, FilterOperator.Equals, Of(ParameterType.Text));
        definition.Parameters.Add(new QueryParameter { Name = "P", Type = ParameterType.Text });
        Assert.NotEmpty(ParameterValidator.Validate(definition));

        definition = Define(ColumnDataType.Text, FilterOperator.Equals, Of(ParameterType.Text));
        definition.Parameters[0].Name = "x) OR (1=1";
        Assert.NotEmpty(ParameterValidator.Validate(definition));
    }
}

public class ParameterBinderTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 13, 30, 0);

    private static ParameterBinder Binder(params string[] queryOptions) => new(new FakeOptions(queryOptions), new FixedClock(Now));

    private static QueryDefinition Define(ColumnDataType column, FilterOperator op, QueryParameter parameter, FilterGroup? target = null)
    {
        var definition = TestData.Definition();
        parameter.Name = "p";
        parameter.Label = "My filter";
        definition.Parameters.Add(parameter);
        (target ?? definition.Filters).Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = "c", DataType = column, Operator = op, IsParameterized = true, ParameterName = "p"
        });
        return definition;
    }

    private static Dictionary<string, string?> Values(string? value) => new() { ["p"] = value };

    private static async Task<string?> Bind(QueryDefinition definition, string? value, ParameterBinder? binder = null)
    {
        var result = await (binder ?? Binder()).MaterializeAsync(definition, Values(value), default);
        var condition = result.Filters.AllConditions().Single();
        Assert.False(condition.IsParameterized);
        return condition.Value;
    }

    private static async Task<CatalogValidationException> Rejects(QueryDefinition definition, string? value, ParameterBinder? binder = null) =>
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            (binder ?? Binder()).MaterializeAsync(definition, Values(value), default));

    [Fact]
    public async Task Text_value_is_written_into_a_copy_and_the_original_is_untouched()
    {
        var definition = Define(ColumnDataType.Text, FilterOperator.Contains, new QueryParameter { Type = ParameterType.Text });

        var value = await Bind(definition, "north");

        Assert.Equal("north", value);
        Assert.True(definition.Filters.Conditions[0].IsParameterized);
    }

    [Theory]
    [InlineData("12.50", "12.50")]
    [InlineData("7", "7")]
    [InlineData("-3.1", "-3.1")]
    public async Task Number_is_canonicalised(string input, string expected) =>
        Assert.Equal(expected, await Bind(Define(ColumnDataType.Number, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Number }), input));

    [Theory]
    [InlineData("abc")]
    [InlineData("1e5")]
    [InlineData("1,5")]
    public async Task Bad_number_is_rejected_naming_the_parameter(string input)
    {
        var ex = await Rejects(Define(ColumnDataType.Number, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Number }), input);
        Assert.Contains("My filter", ex.Message);
    }

    [Theory]
    [InlineData("2026-01-05", "2026-01-05")]
    [InlineData("today", "2026-11-04")]
    [InlineData("yesterday", "2026-11-03")]
    public async Task Date_accepts_iso_and_relative_tokens(string input, string expected) =>
        Assert.Equal(expected, await Bind(Define(ColumnDataType.Date, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Date }), input));

    [Theory]
    [InlineData("05/01/2026")]
    [InlineData("2026-13-45")]
    [InlineData("last_7_days")]
    public async Task Bad_date_is_rejected(string input) =>
        await Rejects(Define(ColumnDataType.Date, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Date }), input);

    [Fact]
    public async Task Datetime_accepts_a_browser_value_and_now()
    {
        var definition = Define(ColumnDataType.DateTime, FilterOperator.GreaterOrEqual, new QueryParameter { Type = ParameterType.DateTime });
        Assert.Equal("2026-01-05T08:15:00", await Bind(definition, "2026-01-05T08:15"));
        Assert.Equal("2026-11-04T13:30:00", await Bind(definition, "now"));
    }

    [Fact]
    public async Task Boolean_is_normalised()
    {
        var definition = Define(ColumnDataType.Boolean, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Boolean });
        Assert.Equal("true", await Bind(definition, "1"));
        Assert.Equal("false", await Bind(definition, "False"));
        await Rejects(definition, "maybe");
    }

    [Fact]
    public async Task Date_range_over_a_date_column_uses_plain_dates()
    {
        var value = await Bind(Define(ColumnDataType.Date, FilterOperator.Between, new QueryParameter { Type = ParameterType.DateRange }), "2026-01-01~2026-01-31");
        Assert.Equal("""["2026-01-01","2026-01-31"]""", value);
    }

    [Fact]
    public async Task Date_range_over_a_datetime_column_includes_the_whole_end_day()
    {
        var value = await Bind(Define(ColumnDataType.DateTime, FilterOperator.Between, new QueryParameter { Type = ParameterType.DateRange }), "2026-01-01~2026-01-31");
        Assert.Equal("""["2026-01-01T00:00:00","2026-01-31T23:59:59.997"]""", value);
    }

    [Fact]
    public async Task Relative_range_token_resolves_with_the_server_clock()
    {
        var definition = Define(ColumnDataType.Date, FilterOperator.Between, new QueryParameter { Type = ParameterType.DateRange });
        Assert.Equal("""["2026-10-06","2026-11-04"]""", await Bind(definition, "last_30_days"));
        Assert.Equal("""["2026-11-02","2026-11-08"]""", await Bind(definition, "this_week"));
    }

    [Fact]
    public async Task Relative_default_is_used_when_nothing_is_supplied()
    {
        var definition = Define(ColumnDataType.Date, FilterOperator.Between,
            new QueryParameter { Type = ParameterType.DateRange, DefaultValue = "last_7_days" });
        Assert.Equal("""["2026-10-29","2026-11-04"]""", await Bind(definition, null));
    }

    [Fact]
    public async Task Datetime_range_keeps_explicit_times()
    {
        var value = await Bind(Define(ColumnDataType.DateTime, FilterOperator.Between, new QueryParameter { Type = ParameterType.DateTimeRange }),
            "2026-01-01T09:00~2026-01-01T17:30");
        Assert.Equal("""["2026-01-01T09:00:00","2026-01-01T17:30:00"]""", value);
    }

    [Theory]
    [InlineData("2026-02-01~2026-01-01")]
    [InlineData("2026-01-01")]
    [InlineData("2026-01-01~")]
    [InlineData("a~b")]
    [InlineData("2026-01-01~2026-01-02~2026-01-03")]
    public async Task Bad_range_is_rejected(string input) =>
        await Rejects(Define(ColumnDataType.Date, FilterOperator.Between, new QueryParameter { Type = ParameterType.DateRange }), input);

    [Fact]
    public async Task Range_type_is_refused_on_a_non_between_filter_and_vice_versa()
    {
        await Rejects(Define(ColumnDataType.Date, FilterOperator.Equals, new QueryParameter { Type = ParameterType.DateRange }), "last_7_days");
        await Rejects(Define(ColumnDataType.Date, FilterOperator.Between, new QueryParameter { Type = ParameterType.Date }), "2026-01-01");
    }

    [Fact]
    public async Task List_value_must_be_one_of_the_options()
    {
        var definition = Define(ColumnDataType.Text, FilterOperator.Equals, new QueryParameter { Type = ParameterType.List, Options = ["a", "b", "c"] });
        Assert.Equal("b", await Bind(definition, "b"));

        var ex = await Rejects(definition, "z");
        Assert.Contains("My filter", ex.Message);
        await Rejects(definition, "B");
    }

    [Fact]
    public async Task List_option_must_also_fit_the_column_type()
    {
        var definition = Define(ColumnDataType.Number, FilterOperator.Equals, new QueryParameter { Type = ParameterType.List, Options = ["10", "ten"] });
        Assert.Equal("10", await Bind(definition, "10"));
        await Rejects(definition, "ten");
    }

    [Fact]
    public async Task Multiple_values_become_an_in_list()
    {
        var parameter = new QueryParameter { Type = ParameterType.List, Options = ["10", "20", "30"], AllowMultiple = true };
        var definition = Define(ColumnDataType.Number, FilterOperator.In, parameter);

        Assert.Equal("""["10","30"]""", await Bind(definition, """["10","30"]"""));
        Assert.Equal("""["20"]""", await Bind(definition, "20"));
    }

    [Fact]
    public async Task Every_multiple_value_is_checked_against_the_options()
    {
        var parameter = new QueryParameter { Type = ParameterType.List, Options = ["10", "20"], AllowMultiple = true };
        await Rejects(Define(ColumnDataType.Number, FilterOperator.In, parameter), """["10","99"]""");
    }

    [Fact]
    public async Task More_than_one_value_is_refused_when_multiple_is_not_allowed()
    {
        var single = new QueryParameter { Type = ParameterType.List, Options = ["10", "20"], AllowMultiple = false };
        var ex = await Rejects(Define(ColumnDataType.Number, FilterOperator.In, single), """["10","20"]""");
        Assert.Contains("single value", ex.Message);

        var scalar = new QueryParameter { Type = ParameterType.List, Options = ["10", "20"], AllowMultiple = true };
        await Rejects(Define(ColumnDataType.Number, FilterOperator.Equals, scalar), """["10","20"]""");
    }

    [Fact]
    public async Task More_than_1000_values_are_refused()
    {
        var options = Enumerable.Range(0, 1001).Select(i => i.ToString()).ToList();
        var parameter = new QueryParameter { Type = ParameterType.List, Options = options, AllowMultiple = true };
        var ex = await Rejects(Define(ColumnDataType.Number, FilterOperator.In, parameter), JsonSerializer.Serialize(options));
        Assert.Contains("1,000", ex.Message);
    }

    [Fact]
    public async Task Query_list_value_must_be_one_of_the_servers_options()
    {
        var definition = Define(ColumnDataType.Number, FilterOperator.Equals,
            new QueryParameter { Type = ParameterType.QueryList, OptionsQueryId = Guid.NewGuid() });
        var binder = Binder("10");

        Assert.Equal("10", await Bind(definition, "10", binder));
        await Rejects(definition, "20", binder);
    }

    [Fact]
    public async Task A_required_parameter_without_value_or_default_is_an_error()
    {
        var definition = Define(ColumnDataType.Text, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Text, IsRequired = true });
        var ex = await Rejects(definition, null);
        Assert.Contains("My filter is required", ex.Message);
        await Rejects(definition, "   ");
    }

    [Fact]
    public async Task Default_fills_in_and_a_supplied_value_wins()
    {
        var definition = Define(ColumnDataType.Text, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Text, DefaultValue = "EU" });
        Assert.Equal("EU", await Bind(definition, null));
        Assert.Equal("US", await Bind(definition, "US"));
    }

    [Fact]
    public async Task An_optional_parameter_without_a_value_drops_its_filter_and_empty_groups()
    {
        var definition = TestData.Definition();
        definition.Parameters.Add(new QueryParameter { Name = "p", Type = ParameterType.Text, IsRequired = false });
        definition.Filters.Operator = FilterLogicalOperator.Or;
        var nested = new FilterGroup();
        nested.Conditions.Add(new FilterCondition { TableAlias = "t0", ColumnName = "c", Operator = FilterOperator.Equals, IsParameterized = true, ParameterName = "p" });
        definition.Filters.Groups.Add(nested);
        definition.Having = new FilterGroup();
        definition.Having.Conditions.Add(new FilterCondition { TableAlias = "t0", ColumnName = "c", Operator = FilterOperator.Equals, IsParameterized = true, ParameterName = "p" });

        var result = await Binder().MaterializeAsync(definition, null, default);

        Assert.Empty(result.Filters.Groups);
        Assert.Empty(result.Filters.Conditions);
        Assert.Null(result.Having);
    }

    [Fact]
    public async Task Unknown_parameter_names_are_rejected()
    {
        var definition = Define(ColumnDataType.Text, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Text });
        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            Binder().MaterializeAsync(definition, new Dictionary<string, string?> { ["p"] = "x", ["nope"] = "1" }, default));
        Assert.Contains("Unknown parameter 'nope'", ex.Message);
    }

    [Fact]
    public async Task Every_invalid_parameter_is_listed_in_one_error()
    {
        var definition = Define(ColumnDataType.Number, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Number });
        definition.Parameters.Add(new QueryParameter { Name = "q", Label = "Other filter", Type = ParameterType.Date });
        definition.Filters.Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = "d", DataType = ColumnDataType.Date, Operator = FilterOperator.Equals, IsParameterized = true, ParameterName = "q"
        });

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            Binder().MaterializeAsync(definition, new Dictionary<string, string?> { ["p"] = "x", ["q"] = "y" }, default));

        Assert.Contains("My filter", ex.Message);
        Assert.Contains("Other filter", ex.Message);
    }

    [Fact]
    public async Task Preview_leaves_a_missing_value_parameterized_instead_of_failing()
    {
        var definition = Define(ColumnDataType.Text, FilterOperator.Equals, new QueryParameter { Type = ParameterType.Text });
        var result = await Binder().MaterializeAsync(definition, null, default, allowMissing: true);
        Assert.True(result.Filters.Conditions[0].IsParameterized);
    }

    [Fact]
    public async Task Hostile_text_stays_data_and_never_reaches_the_sql_text()
    {
        const string hostile = "x'; DROP TABLE t;--";
        var definition = Define(ColumnDataType.Text, FilterOperator.Contains, new QueryParameter { Type = ParameterType.Text });

        var bound = await Binder().MaterializeAsync(definition, Values(hostile), default);
        var generated = new SqlServerQuerySqlBuilder().Build(bound, []);

        Assert.DoesNotContain("DROP", generated.Sql);
        Assert.DoesNotContain("x'", generated.Sql);
        var parameter = Assert.Single(generated.Parameters);
        Assert.Equal($"%{hostile}%", parameter.LiteralValue);
        Assert.False(parameter.IsRuntimeParameter);
    }

    [Fact]
    public async Task Bound_dates_and_lists_reach_the_builder_as_parameters()
    {
        var definition = Define(ColumnDataType.Date, FilterOperator.Between, new QueryParameter { Type = ParameterType.DateRange });
        definition.Parameters.Add(new QueryParameter { Name = "ids", Type = ParameterType.List, Options = ["10", "30"], AllowMultiple = true });
        definition.Filters.Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = "ProgramId", DataType = ColumnDataType.Number, Operator = FilterOperator.In, IsParameterized = true, ParameterName = "ids"
        });

        var bound = await Binder().MaterializeAsync(definition,
            new Dictionary<string, string?> { ["p"] = "last_7_days", ["ids"] = """["10","30"]""" }, default);
        var generated = new SqlServerQuerySqlBuilder().Build(bound, []);

        Assert.Equal(["2026-10-29", "2026-11-04", "10", "30"], generated.Parameters.Select(p => p.LiteralValue));
        Assert.Contains("BETWEEN @p0 AND @p1", generated.Sql);
        Assert.Contains("IN (@p2, @p3)", generated.Sql);
    }
}

public class ParameterOptionsServiceTests
{
    private static (ParameterOptionsService Service, VizFixture Fixture) Arrange(string user = VizFixture.Owner)
    {
        var f = new VizFixture();
        f.User.UserId = user;
        var provider = new ServiceCollection()
            .AddSingleton<IParameterBinder>(new ParameterBinder(new FakeOptions(), TimeProvider.System))
            .BuildServiceProvider();
        var service = new ParameterOptionsService(
            f.Queries, f.DataSources, f.Catalog, f.Builders, f.Executor, f.Guard, f.Limits, new FakeAppSettings(), f.User, new QueryBuilder.Application.Dashboards.DashboardGrant(), provider);
        return (service, f);
    }

    private static QueryParameter For(VizFixture f, string? label = null, string? value = null) => new()
    {
        Name = "p", Type = ParameterType.QueryList, OptionsQueryId = f.Query.Id, OptionsLabelColumn = label, OptionsValueColumn = value
    };

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Key, c => c.Value);

    [Fact]
    public async Task A_caller_who_cannot_view_the_source_query_is_forbidden_and_nothing_runs()
    {
        var (service, f) = Arrange(VizFixture.Stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetOptionsAsync(For(f), default));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task A_missing_source_query_gives_the_same_forbidden_answer()
    {
        var (service, f) = Arrange();
        var parameter = For(f);
        parameter.OptionsQueryId = Guid.NewGuid();

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetOptionsAsync(parameter, default));
    }

    [Fact]
    public async Task A_disabled_source_query_is_refused()
    {
        var (service, f) = Arrange();
        f.Query.IsDisabled = true;

        await Assert.ThrowsAsync<QueryDisabledException>(() => service.GetOptionsAsync(For(f), default));
    }

    [Fact]
    public async Task The_callers_row_level_scope_is_applied_to_the_source_query()
    {
        var (service, f) = Arrange();
        f.Guard.Predicates = [new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, ["10"])];
        f.Executor.Result = VizFixture.Result([Row(("ProgramId", 10L))]);

        await service.GetOptionsAsync(For(f), default);

        Assert.Equal(1, f.Guard.AuthorizeCalls);
        Assert.Equal(f.Guard.Predicates, f.Builders.ReceivedScope);
        Assert.Contains("__scope0", f.Builders.Generated!.Sql);
    }

    [Fact]
    public async Task The_record_limit_caps_what_is_fetched()
    {
        var (service, f) = Arrange();
        f.DataSource.MaxRecords = 25;
        f.Executor.Result = VizFixture.Result([Row(("v", "a"))]);

        await service.GetOptionsAsync(For(f), default);

        Assert.Equal(25, f.Executor.ReceivedMaxRows);
    }

    [Fact]
    public async Task Options_are_capped_at_1000_distinct_values()
    {
        var (service, f) = Arrange();
        f.Executor.Result = VizFixture.Result(
            Enumerable.Range(0, 3000).Select(i => Row(("v", $"item{i % 1500}"))), columns: [("v", ColumnDataType.Text)]);

        var options = await service.GetOptionsAsync(For(f), default);

        Assert.Equal(1000, options.Count);
        Assert.Equal(1000, options.Select(o => o.Value).Distinct().Count());
        Assert.Equal("item0", options[0].Value);
    }

    [Fact]
    public async Task Name_and_value_columns_are_used_when_present()
    {
        var (service, f) = Arrange();
        f.Executor.Result = VizFixture.Result([Row(("id", 1), ("value", 10L), ("name", "Program A")), Row(("id", 2), ("value", 20L), ("name", "Program B"))],
            columns: [("id", ColumnDataType.Number), ("value", ColumnDataType.Number), ("name", ColumnDataType.Text)]);

        var options = await service.GetOptionsAsync(For(f), default);

        Assert.Equal([new ParameterOption("Program A", "10"), new ParameterOption("Program B", "20")], options);
    }

    [Fact]
    public async Task Conventional_name_and_value_columns_match_by_exact_case_first_then_ignoring_case()
    {
        var (service, f) = Arrange();
        f.Executor.Result = VizFixture.Result([Row(("Id", 1), ("Name", "Program A"), ("Value", 10L))],
            columns: [("Id", ColumnDataType.Number), ("Name", ColumnDataType.Text), ("Value", ColumnDataType.Number)]);

        Assert.Equal([new ParameterOption("Program A", "10")], await service.GetOptionsAsync(For(f), default));

        var (service2, f2) = Arrange();
        f2.Executor.Result = VizFixture.Result([Row(("x", 1), ("name", "lower"), ("NAME", "upper"), ("value", 5L))],
            columns: [("x", ColumnDataType.Number), ("name", ColumnDataType.Text), ("NAME", ColumnDataType.Text), ("value", ColumnDataType.Number)]);

        Assert.Equal([new ParameterOption("lower", "5")], await service2.GetOptionsAsync(For(f2), default));
    }

    [Fact]
    public async Task The_first_column_is_label_and_value_otherwise()
    {
        var (service, f) = Arrange();
        f.Executor.Result = VizFixture.Result([Row(("Region", "EU"), ("Total", 5L)), Row(("Region", "US"), ("Total", 6L))],
            columns: [("Region", ColumnDataType.Text), ("Total", ColumnDataType.Number)]);

        var options = await service.GetOptionsAsync(For(f), default);

        Assert.Equal([new ParameterOption("EU", "EU"), new ParameterOption("US", "US")], options);
    }

    [Fact]
    public async Task Configured_columns_win_and_a_missing_one_is_an_error()
    {
        var (service, f) = Arrange();
        f.Executor.Result = VizFixture.Result([Row(("Region", "EU"), ("Code", "e"))],
            columns: [("Region", ColumnDataType.Text), ("Code", ColumnDataType.Text)]);

        var options = await service.GetOptionsAsync(For(f, label: "Region", value: "Code"), default);
        Assert.Equal([new ParameterOption("EU", "e")], options);

        var (service2, f2) = Arrange();
        f2.Executor.Result = f.Executor.Result;
        await Assert.ThrowsAsync<CatalogValidationException>(() => service2.GetOptionsAsync(For(f2, value: "Nope"), default));
    }

    [Fact]
    public async Task Null_values_are_skipped_and_dates_are_formatted_as_iso()
    {
        var (service, f) = Arrange();
        f.Executor.Result = VizFixture.Result([Row(("d", null)), Row(("d", new DateTime(2026, 1, 5)))],
            columns: [("d", ColumnDataType.Date)]);

        var options = await service.GetOptionsAsync(For(f), default);

        Assert.Equal([new ParameterOption("2026-01-05", "2026-01-05")], options);
    }

    [Fact]
    public async Task Within_one_request_the_same_source_runs_once()
    {
        var (service, f) = Arrange();
        f.Executor.Result = VizFixture.Result([Row(("v", "a"))], columns: [("v", ColumnDataType.Text)]);

        await service.GetOptionsAsync(For(f), default);
        await service.GetOptionsAsync(For(f), default);

        Assert.Equal(1, f.Executor.Calls);
    }
}
