using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Dtos;

public sealed record SavedQuerySummaryDto(
    Guid Id,
    string Name,
    string? Description,
    Guid DataSourceId,
    string DataSourceName,
    string OwnerId,
    string OwnerName,
    bool IsOwnedByCurrentUser,
    QueryAccessLevel MyAccessLevel,
    bool IsFavorite,
    bool IsDisabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record SavedQueryDetailDto(
    Guid Id,
    string Name,
    string? Description,
    Guid DataSourceId,
    string DataSourceName,
    string OwnerId,
    string OwnerName,
    bool IsOwnedByCurrentUser,
    QueryAccessLevel MyAccessLevel,
    bool IsFavorite,
    bool IsDisabled,
    QueryDefinition Definition,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record SaveQueryRequest(
    Guid? Id,
    string Name,
    string? Description,
    Guid DataSourceId,
    QueryDefinition Definition);

public sealed record DataSourceDto(Guid Id, string Name, string? Description, CatalogScope CatalogScope);

public sealed record QueryShareDto(
    Guid Id,
    string SharedWithUserId,
    string SharedWithUserEmail,
    QueryAccessLevel AccessLevel,
    DateTimeOffset CreatedAtUtc);

public sealed record ShareQueryRequest(
    string SharedWithUserId,
    string SharedWithUserEmail,
    QueryAccessLevel AccessLevel);

public sealed record RunQueryRequest(
    Guid DataSourceId,
    QueryDefinition Definition,
    Dictionary<string, string?> ParameterValues,
    int? MaxRows,
    Guid? SavedQueryId = null);

public sealed record QuerySqlPreviewDto(string Sql, List<QuerySqlPreviewParameterDto> Parameters);

public sealed record QuerySqlPreviewParameterDto(string Name, ColumnDataType DataType, string? DefaultValue, bool IsRuntimeParameter, string? RuntimeParameterName);

public sealed record QueryResultDto(
    List<QueryResultColumnDto> Columns,
    List<Dictionary<string, object?>> Rows,
    int RowCount,
    long ExecutionTimeMs,
    bool Truncated);

public sealed record QueryResultColumnDto(string Name, ColumnDataType DataType);

public sealed record ExportQueryRequest(
    Guid DataSourceId,
    QueryDefinition Definition,
    Dictionary<string, string?> ParameterValues,
    string Format,
    string FileName,
    Guid? SavedQueryId = null);
