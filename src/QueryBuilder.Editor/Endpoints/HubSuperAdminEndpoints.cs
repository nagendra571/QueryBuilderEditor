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
            new AuthenticationProperties { RedirectUri = CompletePath },
            [options.Hub.SuperAdminSignInScheme!]);
    }

    private static async Task<IResult> CompleteAsync(HttpContext context, QueryBuilderEditorOptions options, CancellationToken ct)
    {
        var services = context.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("QueryBuilder.Hub.SuperAdmin");
        context.Response.Headers.CacheControl = "no-store";

        var external = await context.AuthenticateAsync(HubSession.ExternalScheme);
        // Always, match or not: the external ticket exists only to be read once here.
        await context.SignOutAsync(HubSession.ExternalScheme);

        var match = external.Succeeded && external.Principal is { } principal
            ? SuperAdminRecognizer.Recognize(principal, options.Hub.SuperAdmins)
            : null;

        if (match is null)
        {
            if (external.Succeeded && external.Principal is { } refused && SuperAdminRecognizer.HasGroupsOverage(refused))
            {
                logger.LogInformation("A host user was not recognised as a super admin; their token omitted the groups (too many), which are not looked up.");
            }

            await context.SignOutAsync(HubSession.Scheme);
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
                DetailsJson = JsonSerializer.Serialize(new { objectId = match.ObjectId, matchedBy = match.Kind.ToString() }, QueryDefinitionSerializer.Options),
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                TimestampUtc = now,
            });
            await db.SaveChangesAsync(ct);
        }

        var session = HubSession.ForSuperAdmin(match.ObjectId, match.Name, match.Email, null, now);
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
            || principal.FindFirst(HubSession.ObjectId)?.Value is not { Length: > 0 } objectId)
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        // Any app, enabled or not: turning an app off must not stop a super admin looking at it.
        if (request.AppId is { } appId)
        {
            await using var scope = context.RequestServices.GetRequiredService<IHubWideScopeFactory>().Create();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Apps.AsNoTracking().AnyAsync(a => a.Id == appId, ct))
            {
                return Results.NotFound();
            }
        }

        // The original issue time is kept: switching apps must not stretch the 8 h absolute lifetime.
        var next = HubSession.ForSuperAdmin(objectId, principal.FindFirst(HubSession.Name)?.Value, session.Email, request.AppId, issuedAt);
        await context.SignInAsync(HubSession.Scheme, next, new AuthenticationProperties { IsPersistent = false });
        return Results.Json(new { viewingAppId = request.AppId }, QueryBuilderJson.Options);
    }
}
