using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Dtos;

public sealed record VisualizationDto(
    Guid Id,
    Guid SavedQueryId,
    string Name,
    VisualizationType Type,
    VisualizationOptions Options,
    int DisplayOrder,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAtUtc);

public sealed record SaveVisualizationRequest(string Name, VisualizationType Type, VisualizationOptions Options);

public sealed record RenderVisualizationRequest(Dictionary<string, string?>? ParameterValues, Guid? DashboardId = null, Guid? TileId = null);

/// <summary>Only the columns the chart references — never the query's full output.</summary>
public sealed record VisualizationDataDto(
    List<QueryResultColumnDto> Columns,
    List<Dictionary<string, object?>> Rows,
    int RowCount,
    long ExecutionTimeMs,
    int RowLimit,
    bool Truncated = false);
