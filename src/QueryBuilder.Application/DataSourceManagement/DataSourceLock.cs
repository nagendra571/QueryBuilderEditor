using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.DataSourceManagement;

/// <summary>
/// Hub-mode rules on who may change a data source. Embedded mode has no roles, so both checks do nothing there.
/// A locked source is visible to app admins (hiding it would be wrong) but only a super admin changes it.
/// </summary>
public static class DataSourceLock
{
    public const string LockedMessage = "A super admin locked this data source, so only a super admin can change it.";
    public const string ConnectionsMessage = "A super admin has not allowed app admins to manage data source connections for this app.";

    public static bool CanEdit(DataSource dataSource, AccessActor actor) =>
        !actor.InHub || actor.IsSuperAdmin || !dataSource.LockedBySuperAdmin;

    public static bool CanManageConnections(App? app, AccessActor actor) =>
        !actor.InHub || actor.IsSuperAdmin || app?.AppAdminsCanManageConnections == true;

    public static void EnsureCanChange(DataSource dataSource, AccessActor actor)
    {
        if (!CanEdit(dataSource, actor))
        {
            throw new ForbiddenException(LockedMessage);
        }
    }

    public static void EnsureCanManageConnections(App? app, AccessActor actor)
    {
        if (!CanManageConnections(app, actor))
        {
            throw new ForbiddenException(ConnectionsMessage);
        }
    }

    /// <summary>Reads the app row only when the answer could be no, so embedded mode never touches it.</summary>
    public static async Task EnsureCanManageConnectionsAsync(Abstractions.ICurrentAppReader apps, AccessActor actor, CancellationToken ct)
    {
        if (actor.InHub && !actor.IsSuperAdmin)
        {
            EnsureCanManageConnections(await apps.GetCurrentAsync(ct), actor);
        }
    }

    public static async Task<bool> CanManageConnectionsAsync(Abstractions.ICurrentAppReader apps, AccessActor actor, CancellationToken ct) =>
        !actor.InHub || actor.IsSuperAdmin || CanManageConnections(await apps.GetCurrentAsync(ct), actor);
}
