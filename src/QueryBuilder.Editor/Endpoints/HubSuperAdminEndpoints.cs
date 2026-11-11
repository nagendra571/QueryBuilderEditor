using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor.Hub;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Editor.Endpoints;

/// <summary>
/// Super admin sign-in through the host's own scheme, and the switch that picks which app a super admin looks at.
/// Mapped outside the authorized group: the person arriving has no QueryBuilder session yet, and the host's
/// authorization options describe app users, not these.
/// </summary>
internal static class HubSuperAdminEndpoints
{
    public const string SignInPath = QueryBuilderRoutes.BasePath + "/signin";
    public const string CompletePath = QueryBuilderRoutes.BasePath + "/signin/complete";
    public const string ViewAppPath = QueryBuilderRoutes.BasePath + "/api/hub/view-app";
    private const string Home = QueryBuilderRoutes.BasePath + "/";

    public static void MapHubSuperAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(SignInPath, SignIn).AllowAnonymous();
        endpoints.MapGet(CompletePath, CompleteAsync).AllowAnonymous();
        endpoints.MapPost(ViewAppPath, ViewAppAsync).AllowAnonymous();
    }

    private static IResult SignIn(HttpContext context, QueryBuilderEditorOptions options)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Challenge(
            // The marker travels with the host handler's ticket, so a ticket someone else's handler left in the
            // external cookie is recognisable as not ours.
            new AuthenticationProperties(new Dictionary<string, string?> { [StartedMarker] = "1" }) { RedirectUri = CompletePath },
            [options.Hub.SuperAdminSignInScheme!]);
    }

    private const string StartedMarker = "qb:superadmin";
    // Set by ASP.NET Core's remote authentication handlers on the ticket they create.
    private const string AuthSchemeItem = ".AuthScheme";

    private static async Task RecordRefusalAsync(HttpContext context, SuperAdminRecognition recognition, SuperAdminRefusalReason reason, CancellationToken ct)
    {
        await using var scope = context.RequestServices.GetRequiredService<IHubWideScopeFactory>().Create();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = "anonymous",
            Action = AuditAction.SuperAdminSignInRefused,
            EntityType = "SuperAdmin",
            Summary = $"Super admin sign-in refused: {reason}",
            // Ids only, capped because they come from a token; never an email or a name.
            DetailsJson = JsonSerializer.Serialize(new
            {
                objectId = Cap(recognition.ObjectId), tenantId = Cap(recognition.TenantId), reason = reason.ToString(),
            }, QueryDefinitionSerializer.Options),
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            TimestampUtc = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? Cap(string? value) => value is { Length: > 100 } ? value[..100] : value;

    private static async Task<IResult> CompleteAsync(HttpContext context, QueryBuilderEditorOptions options, CancellationToken ct)
    {
        var services = context.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("QueryBuilder.Hub.SuperAdmin");
        context.Response.Headers.CacheControl = "no-store";

        var external = await context.AuthenticateAsync(HubSession.ExternalScheme);
        // Always, match or not: the external ticket exists only to be read once here.
        await context.SignOutAsync(HubSession.ExternalScheme);

        // A bare GET with no external ticket (a cross-site link, say) is refused quietly: no audit, and the
        // visitor's existing session is left alone.
        if (!external.Succeeded || external.Principal is not { } principal)
        {
            return Results.Content(HubLaunchEndpoints.RefusalPage, "text/html; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
        }

        var recognition = SuperAdminRecognizer.Recognize(principal, options.Hub.SuperAdmins);
        var items = external.Properties?.Items;
        var ours = items is not null
            && items.TryGetValue(StartedMarker, out var marker) && marker == "1"
            && items.TryGetValue(AuthSchemeItem, out var stampedBy)
            && string.Equals(stampedBy, options.Hub.SuperAdminSignInScheme, StringComparison.Ordinal);

        var match = ours ? recognition.Match : null;
        if (match is null)
        {
            var reason = ours ? recognition.Refusal ?? SuperAdminRefusalReason.NotAllowlisted : SuperAdminRefusalReason.WrongScheme;
            if (reason == SuperAdminRefusalReason.GroupsOverage)
            {
                logger.LogInformation("A host user was not recognised as a super admin; their token omitted the groups (too many), which are not looked up.");
            }

            // Signed out first: if the audit write fails, the visitor must not be left holding a session.
            await context.SignOutAsync(HubSession.Scheme);
            await RecordRefusalAsync(context, recognition, reason, ct);
            return Results.Content(HubLaunchEndpoints.RefusalPage, "text/html; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        // Audited before the session exists: if the write fails there is no sign-in rather than an unaudited one.
        await using (var scope = services.GetRequiredService<IHubWideScopeFactory>().Create())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogEntries.Add(new AuditLogEntry
            {
                Actor = $"super:{match.ObjectId}",
                Action = AuditAction.SuperAdminSignedIn,
                EntityType = "SuperAdmin",
                EntityName = match.ObjectId,
                Summary = "Super admin signed in",
                // The kind of entry that matched, never the matching value: an email or group id is not needed to explain it.
                DetailsJson = JsonSerializer.Serialize(new { objectId = match.ObjectId, tenantId = Cap(recognition.TenantId), matchedBy = match.Kind.ToString() }, QueryDefinitionSerializer.Options),
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                TimestampUtc = now,
            });
            await db.SaveChangesAsync(ct);
        }

        var session = HubSession.ForSuperAdmin(match.ObjectId, match.Name, match.Email, null, now, match.Kind, match.MatchedValue, match.TenantId ?? recognition.TenantId ?? "");
        await context.SignOutAsync(HubSession.Scheme);
        await context.SignInAsync(HubSession.Scheme, session, new AuthenticationProperties { IsPersistent = false });

        context.Response.Headers.Location = Home;
        return Results.StatusCode(StatusCodes.Status302Found);
    }

    private sealed record ViewAppRequest(Guid? AppId);

    private static async Task<IResult> ViewAppAsync(HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var principal = context.Items[HubSessionStartupFilter.PrincipalItemKey] as ClaimsPrincipal;
        var session = HubSession.TryRead(principal);
        if (session is null || principal is null)
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        if (!session.IsSuperAdmin)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        ViewAppRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ViewAppRequest>(QueryBuilderJson.Options, ct);
        }
        catch (JsonException)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        if (request is null || !HubSession.TryReadIssued(principal, out var issuedAt)
            || principal.FindFirst(HubSession.ObjectId)?.Value is not { Length: > 0 } objectId
            || !HubSession.TryReadSuperMatch(principal, out var matchKind, out var matchValue, out var tenantId))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        // Any app, enabled or not: turning an app off must not stop a super admin looking at it.
        if (request.AppId is { } appId)
        {
            await using var scope = context.RequestServices.GetRequiredService<IHubWideScopeFactory>().Create();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Default is the embedded-mode app and None the "no session" marker: neither is something to look at in hub mode.
            if (appId == App.DefaultId || appId == App.NoneId || !await db.Apps.AsNoTracking().AnyAsync(a => a.Id == appId, ct))
            {
                return Results.NotFound();
            }
        }

        // The original issue time is kept: switching apps must not stretch the 8 h absolute lifetime.
        var next = HubSession.ForSuperAdmin(objectId, principal.FindFirst(HubSession.Name)?.Value, session.Email, request.AppId, issuedAt, matchKind, matchValue, tenantId);
        await context.SignInAsync(HubSession.Scheme, next, new AuthenticationProperties { IsPersistent = false });
        return Results.Json(new { viewingAppId = request.AppId }, QueryBuilderJson.Options);
    }
}
