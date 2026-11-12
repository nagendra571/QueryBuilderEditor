namespace QueryBuilder.Application.Parameters;

/// <summary>
/// Relative-date tokens ("last_30_days") stored in a parameter's value and resolved on the server at
/// run time, so the same saved default keeps meaning "the last 30 days" tomorrow — and works for a
/// future scheduler, which a browser-computed date could not. Weeks start on Monday and ranges are
/// inclusive of both end dates.
/// </summary>
public static class RelativeDates
{
    public static readonly IReadOnlyList<string> RangeTokens =
    [
        "today", "yesterday", "this_week", "this_month", "this_year",
        "last_week", "last_month", "last_year",
        "last_7_days", "last_14_days", "last_30_days", "last_60_days", "last_90_days", "last_12_months"
    ];

    public static bool IsRangeToken(string? token) =>
        token is not null && RangeTokens.Contains(token, StringComparer.Ordinal);

    /// <summary>"now" is only meaningful for a date-time value, never a date.</summary>
    public static bool TryResolveSingle(string? token, DateTime now, bool includeTime, out DateTime value)
    {
        value = default;
        switch (token)
        {
            case "today": value = now.Date; return true;
            case "yesterday": value = now.Date.AddDays(-1); return true;
            case "now" when includeTime: value = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second); return true;
            default: return false;
        }
    }

    public static bool TryResolveRange(string? token, DateTime now, out DateTime start, out DateTime end)
    {
        var today = now.Date;
        start = end = today;
        switch (token)
        {
            case "today":
                return true;
            case "yesterday":
                start = end = today.AddDays(-1);
                return true;
            case "this_week":
                start = StartOfWeek(today);
                end = start.AddDays(6);
                return true;
            case "last_week":
                start = StartOfWeek(today).AddDays(-7);
                end = start.AddDays(6);
                return true;
            case "this_month":
                start = new DateTime(today.Year, today.Month, 1);
                end = start.AddMonths(1).AddDays(-1);
                return true;
            case "last_month":
                start = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                end = start.AddMonths(1).AddDays(-1);
                return true;
            case "this_year":
                start = new DateTime(today.Year, 1, 1);
                end = new DateTime(today.Year, 12, 31);
                return true;
            case "last_year":
                start = new DateTime(today.Year - 1, 1, 1);
                end = new DateTime(today.Year - 1, 12, 31);
                return true;
            case "last_7_days": start = today.AddDays(-6); return true;
            case "last_14_days": start = today.AddDays(-13); return true;
            case "last_30_days": start = today.AddDays(-29); return true;
            case "last_60_days": start = today.AddDays(-59); return true;
            case "last_90_days": start = today.AddDays(-89); return true;
            case "last_12_months":
                start = today.AddMonths(-12).AddDays(1);
                return true;
            default:
                return false;
        }
    }

    private static DateTime StartOfWeek(DateTime date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
