namespace QueryBuilder.Application.Abstractions;

/// <summary>Per-app settings that cap what a request may do. Embedded mode has no app row, so nothing is capped.</summary>
public interface ICurrentAppSettings
{
    /// <summary>The resolved app's record ceiling, or null when there is none (always null in embedded mode).</summary>
    Task<int?> MaxRecordsAsync(CancellationToken ct);
}
