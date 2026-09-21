using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Entities;

/// <summary>
/// An immutable record of a governance-worthy action. Never updated after being written —
/// corrections happen by writing a new entry, not editing history.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The resolved actor identity (see ICurrentUserService) — never "the current owner", always who actually did this.</summary>
    public string Actor { get; set; } = "anonymous";

    public AuditAction Action { get; set; }

    /// <summary>e.g. "SavedQuery". Kept as a string rather than a FK so history survives entity deletion.</summary>
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }

    /// <summary>Snapshot of the entity's name at the time of the action — the entity itself may since have been deleted or renamed.</summary>
    public string? EntityName { get; set; }

    public Guid? DataSourceId { get; set; }

    /// <summary>Short, human-readable line for display, e.g. "Ran query, returned 42 rows in 118ms".</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Action-specific structured detail (row count, export format, etc.), serialized as JSON.</summary>
    public string? DetailsJson { get; set; }

    public string? IpAddress { get; set; }
}
