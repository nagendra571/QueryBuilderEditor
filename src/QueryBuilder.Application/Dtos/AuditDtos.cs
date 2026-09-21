using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Dtos;

public sealed record AuditLogEntryDto(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Actor,
    AuditAction Action,
    string EntityType,
    Guid? EntityId,
    string? EntityName,
    Guid? DataSourceId,
    string Summary,
    string? DetailsJson,
    string? IpAddress);
