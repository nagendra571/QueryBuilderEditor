using QueryBuilder.Application.Common;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Visualizations;

/// <summary>Who may see / change a saved query's charts. A Viewer has no query access at all, even through a share
/// (they reach charts only through a dashboard); Admins and super admins see and manage every query of the app.</summary>
public static class SavedQueryAccess
{
    public static bool IsOwner(SavedQuery query, AccessActor actor) => query.OwnerId == actor.UserId;

    public static bool CanView(SavedQuery query, AccessActor actor) =>
        !actor.IsViewer
        && (actor.SeesWholeApp || query.OwnerId == actor.UserId || query.Shares.Any(s => s.SharedWithUserId == actor.UserId));

    public static bool CanEdit(SavedQuery query, AccessActor actor) =>
        !actor.IsViewer
        && (actor.SeesWholeApp
            || query.OwnerId == actor.UserId
            || query.Shares.Any(s => s.SharedWithUserId == actor.UserId && s.AccessLevel == QueryAccessLevel.Editor));

    public static bool CanManage(SavedQuery query, AccessActor actor) =>
        !actor.IsViewer && (actor.SeesWholeApp || query.OwnerId == actor.UserId);

    public static QueryAccessLevel AccessLevelFor(SavedQuery query, AccessActor actor) =>
        actor.SeesWholeApp || query.OwnerId == actor.UserId
            ? QueryAccessLevel.Owner
            : query.Shares.FirstOrDefault(s => s.SharedWithUserId == actor.UserId)?.AccessLevel ?? QueryAccessLevel.Viewer;
}
