using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Dtos;

public sealed record DashboardSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string OwnerId,
    string OwnerName,
    bool IsOwner,
    QueryAccessLevel AccessLevel,
    int TileCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    bool Published = false);

public sealed record DashboardTileChartDto(
    Guid SavedQueryId,
    string? SavedQueryName,
    Guid VisualizationId,
    string VisualizationName,
    VisualizationType Type);

/// <summary>A restricted tile carries only Id, Kind, Order, position, size and Restricted = true.</summary>
public sealed record DashboardTileDto(
    Guid Id,
    DashboardTileKind Kind,
    int Order,
    int X,
    int Y,
    int Width,
    int Height,
    bool AutoHeight,
    bool Restricted,
    bool Missing,
    string? Title,
    string? Text,
    DashboardTileChartDto? Chart,
    Dictionary<string, ParameterMappingDto>? ParameterMappings = null);

public sealed record DashboardDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string OwnerId,
    string OwnerName,
    bool IsOwner,
    QueryAccessLevel AccessLevel,
    int Version,
    List<DashboardTileDto> Tiles,
    List<DashboardParameterDto>? Parameters = null,
    bool Published = false);

public sealed record PublishDashboardRequest(bool? Published);

public sealed record SaveDashboardRequest(string Name, string? Description, int? Version);

public sealed record SaveDashboardTileRequest(
    Guid? Id,
    DashboardTileKind Kind,
    int Order,
    int X,
    int Y,
    int Width,
    int Height,
    bool AutoHeight,
    string? Title,
    string? Text,
    Guid? VisualizationId,
    Dictionary<string, ParameterMappingDto>? ParameterMappings = null);

/// <summary><see cref="Parameters"/> null leaves the stored list unchanged; an empty list clears it.
/// The same holds for a tile's <c>ParameterMappings</c>.</summary>
public sealed record SaveDashboardTilesRequest(
    int Version,
    List<SaveDashboardTileRequest> Tiles,
    List<DashboardParameterDto>? Parameters = null);

/// <summary>A parameter shared by several tiles. Type, options and source query are copied from the first
/// query parameter it was created for; <see cref="Value"/> is the saved default for viewers.</summary>
public sealed record DashboardParameterDto(
    string Name,
    string? Title,
    ParameterType Type,
    List<string>? Options,
    Guid? OptionsQueryId,
    string? OptionsLabelColumn,
    string? OptionsValueColumn,
    bool AllowMultiple,
    string? Value);

/// <summary>Where one query parameter of a tile gets its value. <c>Default</c> is never stored.</summary>
public sealed record ParameterMappingDto(
    ParameterMappingSource Source,
    string? DashboardParameter,
    string? Value,
    string? Title);

public sealed record AddChartToDashboardRequest(Guid VisualizationId);

public sealed record ShareDashboardRequest(
    string SharedWithUserId,
    string SharedWithUserEmail,
    QueryAccessLevel AccessLevel,
    List<Guid>? AlsoShareQueryIds);

public sealed record InaccessibleQueryDto(Guid Id, string Name, bool CanShare);

public sealed record DashboardSharePreviewDto(List<InaccessibleQueryDto> InaccessibleQueries);

public sealed record PickableVisualizationDto(
    Guid VisualizationId,
    string VisualizationName,
    VisualizationType Type,
    Guid SavedQueryId,
    string SavedQueryName);

/// <summary>What a granted tile needs to draw its chart and show its parameter controls, and nothing of the query itself:
/// no definition or SQL, no source-query ids for choice lists. The query's name only goes to someone who could open it.</summary>
public sealed record TileChartDto(
    Guid SavedQueryId,
    string? SavedQueryName,
    bool IsDisabled,
    TileChartVisualizationDto Visualization,
    List<TileChartParameterDto> Parameters);

public sealed record TileChartVisualizationDto(Guid Id, string Name, VisualizationType Type, QueryBuilder.Domain.Model.VisualizationOptions Options);

public sealed record TileChartParameterDto(
    string Name,
    string Label,
    ColumnDataType DataType,
    ParameterType Type,
    string? DefaultValue,
    bool IsRequired,
    List<string> Options,
    bool AllowMultiple);
