using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Entities;

/// <summary>
/// Reserved for the sharing feature (v2). Modeled now so the schema doesn't need
/// a breaking migration later; not yet surfaced in the UI.
/// </summary>
public sealed class QueryShare : AuditableEntity
{
    public Guid SavedQueryId { get; set; }
    public SavedQuery? SavedQuery { get; set; }
    public string SharedWithUserId { get; set; } = string.Empty;
    public string SharedWithUserEmail { get; set; } = string.Empty;
    public QueryAccessLevel AccessLevel { get; set; } = QueryAccessLevel.Viewer;
}
