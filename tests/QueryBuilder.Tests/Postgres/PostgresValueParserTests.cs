using System.Globalization;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Execution;

namespace QueryBuilder.Tests.Postgres;

public class PostgresValueParserTests
{
    [Theory]
    [InlineData("integer")]
    [InlineData("smallint")]
    [InlineData("bigint")]
    [InlineData("oid")]
    public void Integers_parse_as_long(string type)
    {
        Assert.Equal(ColumnDataType.Number, PostgresValueParser.ColumnType(type));
        Assert.Equal(7L, PostgresValueParser.Parse(type, "7"));
    }

    [Fact]
    public void Numeric_keeps_its_scale()
    {
        Assert.Equal(ColumnDataType.Number, PostgresValueParser.ColumnType("numeric(10, 2)"));
        var value = Assert.IsType<decimal>(PostgresValueParser.Parse("numeric(10, 2)", "12.50"));
        Assert.Equal(12.50m, value);
        Assert.Equal("12.50", value.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("79228162514264337593543950335")]
    [InlineData("0.1234567890123456789012345678")]
    public void Numeric_that_fits_decimal_is_decimal(string text)
    {
        Assert.IsType<decimal>(PostgresValueParser.Parse("numeric", text));
    }

    [Theory]
    [InlineData("79228162514264337593543950336")]
    [InlineData("0.12345678901234567890123456789")]
    [InlineData("0.1234567890123456789012345678901")]
    [InlineData("0.00000000000000000000000000000000001")]
    [InlineData("1.0000000000000000000000000000000000")]
    public void Numeric_that_decimal_cannot_hold_exactly_stays_text(string text)
    {
        Assert.Equal(text, PostgresValueParser.Parse("numeric", text));
    }

    [Theory]
    [InlineData("numeric", "NaN")]
    [InlineData("numeric", "Infinity")]
    [InlineData("numeric", "-Infinity")]
    [InlineData("double precision", "NaN")]
    [InlineData("double precision", "Infinity")]
    [InlineData("double precision", "-Infinity")]
    [InlineData("real", "NaN")]
    public void Non_finite_numbers_are_null(string type, string text)
    {
        Assert.Null(PostgresValueParser.Parse(type, text));
    }

    [Theory]
    [InlineData("real")]
    [InlineData("double precision")]
    public void Floats_parse_as_double(string type)
    {
        Assert.Equal(ColumnDataType.Number, PostgresValueParser.ColumnType(type));
        Assert.Equal(1.5d, PostgresValueParser.Parse(type, "1.5"));
    }

    [Theory]
    [InlineData("t", true)]
    [InlineData("f", false)]
    public void Booleans(string text, bool expected)
    {
        Assert.Equal(ColumnDataType.Boolean, PostgresValueParser.ColumnType("boolean"));
        Assert.Equal(expected, PostgresValueParser.Parse("boolean", text));
    }

    [Fact]
    public void Date_parses()
    {
        Assert.Equal(ColumnDataType.Date, PostgresValueParser.ColumnType("date"));
        Assert.Equal(new DateTime(2026, 1, 15), PostgresValueParser.Parse("date", "2026-01-15"));
    }

    [Theory]
    [InlineData("date", "infinity")]
    [InlineData("date", "-infinity")]
    [InlineData("date", "0044-03-15 BC")]
    [InlineData("date", "12000-01-01")]
    [InlineData("timestamp with time zone", "infinity")]
    [InlineData("timestamp with time zone", "0044-03-15 00:00:00 BC")]
    public void Dates_that_cannot_be_represented_stay_text(string type, string text)
    {
        Assert.Equal(text, PostgresValueParser.Parse(type, text));
    }

    [Theory]
    [InlineData("timestamp without time zone")]
    [InlineData("timestamp(3) without time zone")]
    public void Timestamp_is_unspecified_kind(string type)
    {
        Assert.Equal(ColumnDataType.DateTime, PostgresValueParser.ColumnType(type));
        var value = Assert.IsType<DateTime>(PostgresValueParser.Parse(type, "2026-01-15 10:00:00.5"));
        Assert.Equal(DateTimeKind.Unspecified, value.Kind);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, 500), value);
    }

    [Theory]
    [InlineData("timestamp with time zone")]
    [InlineData("timestamp(3) with time zone")]
    public void Timestamptz_is_utc(string type)
    {
        Assert.Equal(ColumnDataType.DateTime, PostgresValueParser.ColumnType(type));
        var value = Assert.IsType<DateTime>(PostgresValueParser.Parse(type, "2026-01-15 10:00:00.1+00"));
        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, 100, DateTimeKind.Utc), value);
    }

    [Fact]
    public void Uuid_parses()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ColumnDataType.Guid, PostgresValueParser.ColumnType("uuid"));
        Assert.Equal(id, PostgresValueParser.Parse("uuid", id.ToString()));
    }

    [Fact]
    public void Character_is_trimmed()
    {
        Assert.Equal(ColumnDataType.Text, PostgresValueParser.ColumnType("character(5)"));
        Assert.Equal("ab", PostgresValueParser.Parse("character(5)", "ab   "));
    }

    [Theory]
    [InlineData("character varying(50)")]
    [InlineData("text")]
    [InlineData("spike.mood")]
    [InlineData("public.citext")]
    [InlineData("money")]
    [InlineData("interval")]
    [InlineData("inet")]
    [InlineData("integer[]")]
    [InlineData("json")]
    [InlineData("-.-")]
    public void Everything_else_is_text(string type)
    {
        Assert.Equal(ColumnDataType.Text, PostgresValueParser.ColumnType(type));
        Assert.Equal("a 1,5 b", PostgresValueParser.Parse(type, "a 1,5 b"));
    }

    [Fact]
    public void Parsing_ignores_host_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var money = Assert.IsType<decimal>(PostgresValueParser.Parse("numeric", "12.50"));
            Assert.Equal(12.50m, money);
            Assert.Equal("12.50", money.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(1.5d, PostgresValueParser.Parse("double precision", "1.5"));
            Assert.Equal(new DateTime(2026, 1, 15), PostgresValueParser.Parse("date", "2026-01-15"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("2024-03-05 10:20:30", 2024, 3, 5, 10, 20, 30)]
    [InlineData("2024-03-05 00:00:00", 2024, 3, 5, 0, 0, 0)]
    public void Timestamp_without_fraction_parses(string text, int y, int mo, int d, int h, int mi, int sec)
    {
        Assert.Equal(new DateTime(y, mo, d, h, mi, sec), PostgresValueParser.Parse("timestamp without time zone", text));
    }

    [Fact]
    public void Timestamptz_without_fraction_parses_to_utc()
    {
        var value = Assert.IsType<DateTime>(PostgresValueParser.Parse("timestamp with time zone", "2024-03-05 10:20:30+00"));
        Assert.Equal(new DateTime(2024, 3, 5, 10, 20, 30, DateTimeKind.Utc), value);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Theory]
    [InlineData("2024-03-05 10:20:30+05:30", 4, 50, 30)]
    [InlineData("2024-03-05 10:20:30.5-03:30", 13, 50, 30)]
    public void Timestamptz_with_half_hour_offset_is_converted_to_utc(string text, int hour, int minute, int second)
    {
        var value = Assert.IsType<DateTime>(PostgresValueParser.Parse("timestamp with time zone", text));
        Assert.Equal(new DateTime(2024, 3, 5, hour, minute, second).AddMilliseconds(text.Contains(".5") ? 500 : 0), value);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Theory]
    [InlineData("timestamp without time zone")]
    [InlineData("timestamp with time zone")]
    public void Infinity_timestamps_stay_text(string type)
    {
        Assert.Equal("infinity", PostgresValueParser.Parse(type, "infinity"));
        Assert.Equal("-infinity", PostgresValueParser.Parse(type, "-infinity"));
    }

    [Fact]
    public void Plain_timestamp_name_falls_back_to_text_when_unparseable()
    {
        Assert.Equal(ColumnDataType.DateTime, PostgresValueParser.ColumnType("timestamp"));
        Assert.Equal("infinity", PostgresValueParser.Parse("timestamp", "infinity"));
        Assert.Equal(new DateTime(2024, 3, 5, 1, 2, 3), PostgresValueParser.Parse("timestamp", "2024-03-05 01:02:03"));
    }

    [Fact]
    public void Numeric_value_is_asserted_not_only_its_type()
    {
        Assert.Equal(79228162514264337593543950335m, PostgresValueParser.Parse("numeric", "79228162514264337593543950335"));
        Assert.Equal(0.1234567890123456789012345678m, PostgresValueParser.Parse("numeric", "0.1234567890123456789012345678"));
    }

    [Fact]
    public void A_resolved_kind_parses_like_the_type_name()
    {
        var kind = PostgresValueParser.Resolve("numeric(10, 2)");

        Assert.Equal(PostgresValueParser.Parse("numeric(10, 2)", "12.50"), PostgresValueParser.Parse(kind, "12.50"));
    }
}
