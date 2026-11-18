using System.Globalization;
using System.Text.RegularExpressions;

namespace QueryBuilder.Application.Common;

/// <summary>
/// Reads the literal a filter, parameter or drill step carries. A drill step is copied from chart JSON, so this accepts what
/// System.Text.Json writes for a value (exponent numbers like <c>1E-05</c>; date-times with <c>Z</c> or an offset) and never
/// touches the host's time zone, which would otherwise move the value by the server's offset and match other rows.
/// </summary>
public static class QueryLiteral
{
    private static readonly Regex ZoneSuffix = new(@"(?:[Zz]|[+-]\d{2}:\d{2})\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParseNumber(string value, out decimal result)
    {
        if (!decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out result))
            return false;

        // decimal silently rounds a tiny exponent number (a float's 1E-300) to 0, which would then match the wrong rows.
        if (result == 0 && HasNonZeroDigit(value))
            return false;
        return true;
    }

    /// <summary>A value with a zone (<c>Z</c> or <c>+05:30</c>) becomes that instant in UTC, as a UTC date-time; one without
    /// stays exactly as written, with no kind. Neither depends on the machine's local time zone.</summary>
    public static bool TryParseDateTime(string value, out DateTime result)
    {
        if (ZoneSuffix.IsMatch(value) && value.Contains(':'))
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset))
            {
                result = offset.UtcDateTime;
                return true;
            }
            result = default;
            return false;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    private static bool HasNonZeroDigit(string value)
    {
        foreach (var c in value)
        {
            if (c is 'e' or 'E') return false;
            if (c is >= '1' and <= '9') return true;
        }
        return false;
    }
}
