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

public sealed record RenderVisualizationRequest(
    Dictionary<string, string?>? ParameterValues, Guid? DashboardId = null, Guid? TileId = null, List<Drill.CrossFilterDto>? CrossFilters = null);

public sealed record ExportVisualizationRequest(string? Format, Dictionary<string, string?>? ParameterValues, Guid? DashboardId = null, Guid? TileId = null);

/// <summary>The columns the chart references plus, on a draw, the query's visible plain columns as row keys (<see cref="KeyColumns"/>) — never hidden columns or the query's full output.</summary>
public sealed record VisualizationDataDto(
    List<QueryResultColumnDto> Columns,
    List<Dictionary<string, object?>> Rows,
    int RowCount,
    long ExecutionTimeMs,
    int RowLimit,
    bool Truncated = false,
    List<string>? KeyColumns = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    CrossFilterResultDto? CrossFilter = null);

/// <summary>Which of a dashboard's click-filters a tile took (<see cref="Applied"/>) and which it could not, because its query
/// has no such column or is on another data source (<see cref="NotApplied"/>); both hold the source column labels.</summary>
public sealed record CrossFilterResultDto(List<string> Applied, List<string> NotApplied);

/// <summary>The browser sends only the click trail and which kind of drill; the definition is always rebuilt from the saved
/// query, and drill columns come from the saved chart, never from here.</summary>
public sealed record DrillRequest(
    DrillAction Action,
    List<DrillStep>? Steps,
    string? BreakdownColumn,
    List<Drill.CrossFilterDto>? CrossFilters,
    Dictionary<string, string?>? ParameterValues,
    Guid? DashboardId,
    Guid? TileId);

public sealed record DrillExportRequest(
    DrillAction Action,
    List<DrillStep>? Steps,
    string? BreakdownColumn,
    List<Drill.CrossFilterDto>? CrossFilters,
    Dictionary<string, string?>? ParameterValues,
    Guid? DashboardId,
    Guid? TileId,
    string? Format);

public sealed record DrillQueryRequest(List<DrillStep>? Steps, Dictionary<string, string?>? ParameterValues);

public sealed record DrillQueryExportRequest(List<DrillStep>? Steps, Dictionary<string, string?>? ParameterValues, string? Format);

/// <summary><see cref="Options"/> is the rewritten chart options for a breakdown, null for rows.</summary>
public sealed record DrillResultDto(
    DrillAction Action,
    List<QueryResultColumnDto> Columns,
    List<Dictionary<string, object?>> Rows,
    int RowCount,
    long ExecutionTimeMs,
    int RowLimit,
    bool Truncated,
    VisualizationType Type,
    VisualizationOptions? Options);
