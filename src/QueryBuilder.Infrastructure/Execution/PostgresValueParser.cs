using System.Globalization;
using System.Text.RegularExpressions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Execution;

/// <summary>
/// Converts Postgres text-format cells to CLR values. Results are read as text (so no driver decides types per
/// provider version) and the session is pinned to ISO dates and UTC; anything that cannot be represented exactly
/// stays as the original text rather than being rounded or clamped.
/// </summary>
public static class PostgresValueParser
{
    private static readonly Regex Modifiers = new(@"\(\d+(,\s*\d+)?\)", RegexOptions.Compiled);

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFF",
    ];

    private static readonly string[] DateTimeOffsetFormats =
    [
        "yyyy-MM-dd HH:mm:sszz",
        "yyyy-MM-dd HH:mm:sszzz",
        "yyyy-MM-dd HH:mm:ss.FFFFFFzz",
        "yyyy-MM-dd HH:mm:ss.FFFFFFzzz",
    ];

    public enum Kind { Text, Integer, Decimal, Float, Boolean, Date, Timestamp, TimestampTz, Uuid, Character }

    public static Kind Resolve(string dataTypeName) => Classify(dataTypeName);

    public static ColumnDataType ColumnType(string dataTypeName) => Classify(dataTypeName) switch
    {
        Kind.Integer or Kind.Decimal or Kind.Float => ColumnDataType.Number,
        Kind.Boolean => ColumnDataType.Boolean,
        Kind.Date => ColumnDataType.Date,
        Kind.Timestamp or Kind.TimestampTz => ColumnDataType.DateTime,
        Kind.Uuid => ColumnDataType.Guid,
        _ => ColumnDataType.Text,
    };

    public static object? Parse(string dataTypeName, string text) => Parse(Classify(dataTypeName), text);

    public static object? Parse(Kind kind, string text)
    {
        switch (kind)
        {
            case Kind.Integer:
                return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l) ? l : text;

            case Kind.Decimal:
                return ParseNumeric(text);

            case Kind.Float:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    return text;
                }
                return double.IsFinite(d) ? d : null;

            case Kind.Boolean:
                return text switch { "t" => true, "f" => false, _ => text };

            case Kind.Date:
                return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : text;

            case Kind.Timestamp:
                return DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)
                    ? ts
                    : text;

            case Kind.TimestampTz:
                return DateTime.TryParseExact(text, DateTimeOffsetFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out var tz)
                    ? tz
                    : text;

            case Kind.Uuid:
                return Guid.TryParse(text, out var g) ? g : text;

            case Kind.Character:
                return text.TrimEnd(' ');

            default:
                return text;
        }
    }

    private static object? ParseNumeric(string text)
    {
        if (text is "NaN" or "Infinity" or "-Infinity")
        {
            return null;
        }

        // decimal silently rounds past 28 digits, so keep it only when it reproduces the text exactly.
        if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)
            && value.ToString(CultureInfo.InvariantCulture) == text)
        {
            return value;
        }

        return text;
    }

    private static Kind Classify(string dataTypeName)
    {
        var name = Modifiers.Replace(dataTypeName, "").Trim().ToLowerInvariant();
        if (name.EndsWith("[]", StringComparison.Ordinal))
        {
            return Kind.Text;
        }

        return name switch
        {
            "smallint" or "integer" or "bigint" or "oid" => Kind.Integer,
            "numeric" => Kind.Decimal,
            "real" or "double precision" => Kind.Float,
            "boolean" => Kind.Boolean,
            "date" => Kind.Date,
            "timestamp without time zone" or "timestamp" => Kind.Timestamp,
            "timestamp with time zone" or "timestamptz" => Kind.TimestampTz,
            "uuid" => Kind.Uuid,
            "character" or "bpchar" => Kind.Character,
            _ => Kind.Text,
        };
    }
}
