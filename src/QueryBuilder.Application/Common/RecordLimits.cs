namespace QueryBuilder.Application.Common;

/// <summary>The host's package-wide default (<c>options.DefaultMaxRecords</c>), null when unset.</summary>
public sealed record RecordLimitSettings(int? DefaultMaxRecords);

/// <summary>
/// Resolves how many records a query may return: the data source's own admin-set limit, else the
/// host's package-wide default. When neither is set there is no limit and the pre-1.0.13 behavior
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

    public static int? Effective(int? dataSourceMaxRecords, RecordLimitSettings settings) =>
        dataSourceMaxRecords ?? settings.DefaultMaxRecords;

    /// <summary>With a limit set the browser's requested row count is ignored — the limit is the cap.</summary>
    public static int GridRows(int? effectiveLimit, int? requestedRows) =>
        effectiveLimit ?? (requestedRows is > 0 and <= LegacyGridMaxRows ? requestedRows.Value : LegacyGridDefaultRows);

    public static bool IsValid(int value) => value is >= Min and <= Max;
}
