using QueryBuilder.Editor.Hub;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Editor.Endpoints;

public static class MeEndpoints
{
    /// <summary>The caller's own identity and row-level scope, for the shell's "your zone" chip. Only
    /// ever describes the requester, so it reveals nothing they couldn't already infer from their data.
    /// In hub mode the same answer also says which app, role and return address the session has; embedded mode's
    /// JSON is exactly what it always was.</summary>
    public static void MapMeEndpoints(this IEndpointRouteBuilder app) => app.MapMeEndpoints(false);

    internal static void MapMeEndpoints(this IEndpointRouteBuilder app, bool hub)
    {
        // Two handlers rather than one with optional services: IAppSessionSource exists only in hub mode, and an
        // unregistered service parameter would fail an embedded host's endpoint.
        if (!hub)
        {
            app.MapGet("/api/me", async (ICurrentUserService user, ICurrentDataScope scope, CancellationToken ct) =>
                Results.Json(new { userId = user.UserId, displayName = user.DisplayName, zone = await ZoneAsync(scope, ct) }, QueryBuilderJson.Options))
                .WithTags("Me");
            return;
        }

        app.MapGet("/api/me", async (ICurrentUserService user, ICurrentDataScope scope, IAppSessionSource session, AppDbContext db, CancellationToken ct) =>
        {
            var zone = await ZoneAsync(scope, ct);
            var current = session.Current!;
            var actor = AccessActor.From(user);

            // The app the session works in: an app session's own, or the one a super admin is viewing. Null for a super
            // admin who has not picked one, which is how the shell knows to show the picker.
            object? appInfo = null;
            if (current.AppId is { } appId)
            {
                appInfo = await db.Apps.AsNoTracking().Where(a => a.Id == appId)
                    .Select(a => new { id = a.Id, key = a.Key, name = a.Name }).FirstOrDefaultAsync(ct);
            }

            return Results.Json(new
            {
                userId = user.UserId,
                displayName = user.DisplayName,
                zone,
                app = appInfo,
                role = current.Role.ToString(),
                isSuperAdmin = current.IsSuperAdmin,
                returnUrl = current.ReturnUrl,
                viewingApp = current.IsSuperAdmin ? appInfo : null,
                permissions = new
                {
                    canBuildQueries = !actor.IsViewer,
                    canManageDashboards = !actor.IsViewer,
                    canPublish = actor.SeesWholeApp,
                    canAdmin = actor.SeesWholeApp,
                    isViewer = actor.IsViewer,
                },
            }, QueryBuilderJson.Options);
        }).WithTags("Me").AllowViewers();
    }

    private static async Task<object?> ZoneAsync(ICurrentDataScope scope, CancellationToken ct)
    {
        if (!scope.IsEnabled)
        {
            return null;
        }

        var resolved = await scope.GetAsync(ct);
        return new
        {
            unrestricted = resolved.IsUnrestricted,
            values = resolved.IsUnrestricted
                ? new Dictionary<string, IReadOnlyList<string>>()
                : resolved.Values.ToDictionary(kv => kv.Key, kv => kv.Value)
        };
    }
}
