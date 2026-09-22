using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Entities;

/// <summary>
/// Grants another user access to a saved query. <see cref="SharedWithUserId"/> must equal
/// whatever that person's own session resolves as their identity (the same value used for
/// <c>CreatedBy</c>/<c>ICurrentUserService.UserId</c>) — it's what access checks match against.
/// Only <see cref="QueryAccessLevel.Viewer"/> and <see cref="QueryAccessLevel.Editor"/> are valid
/// here; true ownership lives on <see cref="SavedQuery.OwnerId"/> instead.
/// </summary>
public sealed class QueryShare : AuditableEntity
{
    public Guid SavedQueryId { get; set; }
    public SavedQuery? SavedQuery { get; set; }
    public string SharedWithUserId { get; set; } = string.Empty;
    public string SharedWithUserEmail { get; set; } = string.Empty;
    public QueryAccessLevel AccessLevel { get; set; } = QueryAccessLevel.Viewer;
}
