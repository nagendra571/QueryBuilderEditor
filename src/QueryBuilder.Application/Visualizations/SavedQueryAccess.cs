using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Visualizations;

/// <summary>Who may see / change a saved query's charts. Mirrors the rules in
/// GetSavedQueryByIdQuery (view = owner or any share) and SaveQueryCommand (edit = owner or Editor).</summary>
public static class SavedQueryAccess
{
    public static bool CanView(SavedQuery query, string userId) =>
        query.OwnerId == userId || query.Shares.Any(s => s.SharedWithUserId == userId);

    public static bool CanEdit(SavedQuery query, string userId) =>
        query.OwnerId == userId
        || query.Shares.Any(s => s.SharedWithUserId == userId && s.AccessLevel == QueryAccessLevel.Editor);
}
