using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Abstractions;

public sealed record AuditEntry(
    AuditAction Action,
    string EntityType,
    Guid? EntityId,
    string? EntityName,
    Guid? DataSourceId,
    string Summary,
    object? Details = null);

/// <summary>Writes governance events to the audit trail. Actor and IP are resolved internally, never passed in.</summary>
public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken);
}
