namespace QueryBuilder.Application.Common;

/// <summary>The host's package-wide default (<c>options.DefaultMaxRecords</c>), null when unset.</summary>
public sealed record RecordLimitSettings(int? DefaultMaxRecords);

/// <summary>
/// Resolves how many records a query may return: the lowest of the app ceiling (hub), the data source's own
/// admin-set limit and the host's package-wide default. When neither is set there is no limit and the pre-1.0.13 behavior
/// is kept exactly (grid capped at the client's requested rows, export at 100,000, both silently)
/// so upgrading the package can never start refusing exports on its own.
/// </summary>
public static class RecordLimits
{
    public const int Min = 1;
    public const int Max = 100_000;

    public const int LegacyGridDefaultRows = 1_000;
    public const int LegacyGridMaxRows = 10_000;
    public const int LegacyExportRows = 100_000;

    /// <summary>Hard ceiling on points a chart will draw, whatever the record limit says.</summary>
    public const int ChartMaxRows = 1_000;

    /// <summary>The lowest limit that is set: a hub app's ceiling, the data source's own limit and the host default can
    /// only tighten each other, never widen.</summary>
    public static int? Effective(int? dataSourceMaxRecords, int? appCeiling, RecordLimitSettings settings)
    {
        int? lowest = null;
        foreach (var value in new[] { dataSourceMaxRecords, appCeiling, settings.DefaultMaxRecords })
        {
            if (value is { } v && (lowest is null || v < lowest))
            {
                lowest = v;
            }
        }

        return lowest;
    }

    /// <summary>With a limit set the browser's requested row count is ignored — the limit is the cap.</summary>
    public static int GridRows(int? effectiveLimit, int? requestedRows) =>
        effectiveLimit ?? (requestedRows is > 0 and <= LegacyGridMaxRows ? requestedRows.Value : LegacyGridDefaultRows);

    /// <summary>A chart is never given more rows than <see cref="ChartMaxRows"/>; a lower record limit wins.</summary>
    public static int ChartRows(int? effectiveLimit) =>
        effectiveLimit is { } limit && limit < ChartMaxRows ? limit : ChartMaxRows;

    public static bool IsValid(int value) => value is >= Min and <= Max;
}
