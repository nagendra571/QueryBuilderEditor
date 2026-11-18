using System.Data;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Drill;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Execution;

namespace QueryBuilder.Tests.Drill;

/// <summary>A drill step is copied out of chart JSON, so the literal readers must accept what System.Text.Json writes, and must
/// not move a date-time by the host's offset. None of this code reads <see cref="TimeZoneInfo.Local"/>, so every expectation
/// below is an absolute value that holds on any host (the development machine is on US Eastern time).</summary>
public class QueryLiteralTests
{
    private static readonly IReadOnlyDictionary<string, string?> NoRuntime = new Dictionary<string, string?>();

    private static object Bind(ColumnDataType type, string? value) =>
        QueryParameterValues.Resolve(new GeneratedQueryParameter("@p", type, value, false, null), NoRuntime);

    [Theory]
    [InlineData("1E-05", "0.00001")]
    [InlineData("1.5e-5", "0.000015")]
    [InlineData("12345.678901234567", "12345.678901234567")]
    [InlineData("-2E+3", "-2000")]
    [InlineData("1,000.5", "1000.5")]
    [InlineData("  7 ", "7")]
    public void Numbers_written_by_json_or_by_hand_parse(string text, string expected)
    {
        Assert.True(QueryLiteral.TryParseNumber(text, out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("1E-300")]
    [InlineData("1E+300")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1e")]
    public void A_number_decimal_cannot_hold_is_refused_rather_than_rounded_to_another_value(string text) =>
        Assert.False(QueryLiteral.TryParseNumber(text, out _));

    [Fact]
    public void Zero_with_an_exponent_is_still_zero() => Assert.True(QueryLiteral.TryParseNumber("0E-10", out var zero) && zero == 0);

    [Theory]
    [InlineData("2024-03-01T10:30:00.1234567Z", 2024, 3, 1, 10, 30, 0, 1234567L, DateTimeKind.Utc)]
    [InlineData("2024-03-01T10:30:00.1234567+05:30", 2024, 3, 1, 5, 0, 0, 1234567L, DateTimeKind.Utc)]
    [InlineData("2024-02-29T22:00:00-08:00", 2024, 3, 1, 6, 0, 0, 0L, DateTimeKind.Utc)]
    [InlineData("2024-03-01T10:30:00.1234567", 2024, 3, 1, 10, 30, 0, 1234567L, DateTimeKind.Unspecified)]
    [InlineData("2024-03-01 10:30:00", 2024, 3, 1, 10, 30, 0, 0L, DateTimeKind.Unspecified)]
    [InlineData("2024-03-01", 2024, 3, 1, 0, 0, 0, 0L, DateTimeKind.Unspecified)]
    public void Date_times_keep_their_wall_clock_or_become_the_same_instant_in_utc(
        string text, int y, int mo, int d, int h, int mi, int s, long extraTicks, DateTimeKind kind)
    {
        Assert.True(QueryLiteral.TryParseDateTime(text, out var value));
        Assert.Equal(new DateTime(y, mo, d, h, mi, s, kind).AddTicks(extraTicks), value);
        Assert.Equal(kind, value.Kind);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("2024-13-45T10:30:00Z")]
    public void A_bad_date_time_is_refused(string text) => Assert.False(QueryLiteral.TryParseDateTime(text, out _));

    [Fact]
    public void A_utc_value_binds_as_utc_so_npgsql_accepts_it_for_timestamptz()
    {
        var bound = Assert.IsType<DateTime>(Bind(ColumnDataType.DateTime, "2024-03-01T10:30:00.123456Z"));

        Assert.Equal(DateTimeKind.Utc, bound.Kind);
        Assert.Equal(new DateTime(2024, 3, 1, 10, 30, 0, DateTimeKind.Utc).AddTicks(1234560), bound);
    }

    [Fact]
    public void An_exponent_number_binds_for_a_float_column()
    {
        Assert.Equal(0.00001m, Bind(ColumnDataType.Number, "1E-05"));
        Assert.Equal(15L, QueryParameterValues.ResolveForPostgres(
            new GeneratedQueryParameter("@p", ColumnDataType.Number, "1.5E+1", false, null), NoRuntime));
    }

    [Fact]
    public void A_date_column_drops_the_time_after_converting_to_utc() =>
        Assert.Equal(new DateTime(2024, 3, 1), Bind(ColumnDataType.Date, "2024-02-29T22:00:00-08:00"));

    [Fact]
    public void An_empty_value_is_null_unless_a_drill_step_says_it_is_the_empty_string()
    {
        Assert.Equal(DBNull.Value, Bind(ColumnDataType.Text, ""));
        Assert.Equal("", QueryParameterValues.Resolve(
            new GeneratedQueryParameter("@p", ColumnDataType.Text, "", false, null, null, EmptyTextIsValue: true), NoRuntime));
        Assert.Equal(DBNull.Value, QueryParameterValues.Resolve(
            new GeneratedQueryParameter("@p", ColumnDataType.Number, "", false, null, null, EmptyTextIsValue: true), NoRuntime));
        Assert.Equal(DBNull.Value, Bind(ColumnDataType.Number, ""));
        Assert.Equal(DBNull.Value, Bind(ColumnDataType.DateTime, ""));
        Assert.Equal(DBNull.Value, Bind(ColumnDataType.Text, null));
    }

    [Fact]
    public void A_bad_literal_is_a_clear_error_at_execution()
    {
        var ex = Assert.Throws<QueryExecutionException>(() => Bind(ColumnDataType.Number, "1E-300"));
        Assert.Contains("not a valid Number", ex.Message);
    }

    [Theory]
    [InlineData(ColumnDataType.Number, "1E-05", true)]
    [InlineData(ColumnDataType.Number, "1E-300", false)]
    [InlineData(ColumnDataType.Number, "twelve", false)]
    [InlineData(ColumnDataType.DateTime, "2024-03-01T10:30:00.1234567Z", true)]
    [InlineData(ColumnDataType.DateTime, "2024-03-01T10:30:00.1234567+05:30", true)]
    [InlineData(ColumnDataType.DateTime, "soon", false)]
    [InlineData(ColumnDataType.Date, "2024-03-01", true)]
    [InlineData(ColumnDataType.Date, "yesterday?", false)]
    [InlineData(ColumnDataType.Guid, "not-a-guid", false)]
    [InlineData(ColumnDataType.Text, "anything at all", true)]
    public void The_drill_check_and_the_executor_agree_on_every_literal(ColumnDataType type, string value, bool valid)
    {
        Assert.Equal(valid, DrillDefinitions.IsValidValue(type, value));

        if (valid)
            Bind(type, value);
        else
            Assert.Throws<QueryExecutionException>(() => Bind(type, value));
    }

    [Fact]
    public void A_null_drill_value_is_always_valid() =>
        Assert.All(Enum.GetValues<ColumnDataType>(), t => Assert.True(DrillDefinitions.IsValidValue(t, null)));

    [Fact]
    public void The_columns_native_type_decides_how_a_fractional_date_time_is_bound()
    {
        var fraction = new DateTime(2024, 3, 1, 10, 30, 0).AddTicks(1234567);
        var whole = SqlServerQueryExecutionService.CreateParameter("@p", new DateTime(2024, 3, 1, 10, 30, 0), "datetime2");
        var number = SqlServerQueryExecutionService.CreateParameter("@p", 5m, null);

        Assert.Equal(SqlDbType.DateTime2, SqlServerQueryExecutionService.CreateParameter("@p", fraction, "datetime2").SqlDbType);
        Assert.Equal(SqlDbType.DateTime2, SqlServerQueryExecutionService.CreateParameter("@p", fraction, "datetimeoffset").SqlDbType);
        Assert.Equal(SqlDbType.DateTime, SqlServerQueryExecutionService.CreateParameter("@p", fraction, "datetime").SqlDbType);
        Assert.Equal(SqlDbType.DateTime, SqlServerQueryExecutionService.CreateParameter("@p", fraction, null).SqlDbType);
        Assert.Equal(SqlDbType.DateTime, whole.SqlDbType);
        Assert.Equal(SqlDbType.Decimal, number.SqlDbType);
    }
}
