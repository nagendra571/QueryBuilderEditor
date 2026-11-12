using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using QueryBuilder.Infrastructure.Hub;
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
    public const string ProviderPath = QueryBuilderRoutes.BasePath + "/signin/provider";
    public const string CodePath = QueryBuilderRoutes.BasePath + "/signin/code";
    public const string ViewAppPath = QueryBuilderRoutes.BasePath + "/api/hub/view-app";
    private const string Home = QueryBuilderRoutes.BasePath + "/";

    public static void MapHubSuperAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(SignInPath, SignIn).AllowAnonymous();
        endpoints.MapPost(ProviderPath, StartProvider).AllowAnonymous();
        endpoints.MapPost(CodePath, SignInWithCodeAsync).AllowAnonymous();
        endpoints.MapGet(CompletePath, CompleteAsync).AllowAnonymous();
        endpoints.MapPost(ViewAppPath, ViewAppAsync).AllowAnonymous();
    }

    // Fixed text with no reason, like the launch refusal page, but with a way back: the person is a staff member with a
    // mistyped or used code, not a probing caller who needs telling which check failed.
    private static string RefusalPageFor(string heading, string text) =>
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<meta name=\"robots\" content=\"noindex\"><title>QueryBuilder</title>" +
        "<style>body{font-family:system-ui,sans-serif;margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#f4f5f7;color:#1b2430}" +
        "main{max-width:28rem;padding:2rem;text-align:center}</style></head><body><main>" +
        $"<h1>{heading}</h1><p>{text}</p><p><a href=\"{SignInPath}\">Back to sign-in</a></p></main></body></html>";

    private static readonly string CodeRefusalPage = RefusalPageFor("Sign-in code not accepted",
        "That code didn't work. Codes work once and expire after a few minutes. Ask for a new code and try again.");

    private static readonly string ProviderStartRefusalPage = RefusalPageFor("Sign-in didn't start from this site",
        "Open the sign-in page and try again.");

    private static IResult SignIn(HttpContext context, QueryBuilderEditorOptions options)
    {
        context.Response.Headers.CacheControl = "no-store";
        var hub = options.Hub;
        if (!hub.SignInCodes.Enabled)
        {
            return Challenge(options);
        }

        var provider = !string.IsNullOrWhiteSpace(hub.SuperAdminSignInScheme);
        return Results.Content(SignInPage.Render(hub.ProviderDisplayName, provider, true, ProviderPath, CodePath), "text/html; charset=utf-8");
    }

    private static IResult StartProvider(HttpContext context, QueryBuilderEditorOptions options)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(options.Hub.SuperAdminSignInScheme))
        {
            return Results.NotFound();
        }

        return OriginMatches(context.Request)
            ? Challenge(options)
            : Results.Content(ProviderStartRefusalPage, "text/html; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
    }

    private static IResult Challenge(QueryBuilderEditorOptions options) =>
        Results.Challenge(
            // The marker travels with the host handler's ticket, so a ticket someone else's handler left in the
            // external cookie is recognisable as not ours.
            new AuthenticationProperties(new Dictionary<string, string?> { [StartedMarker] = "1" }) { RedirectUri = CompletePath },
            [options.Hub.SuperAdminSignInScheme!]);

    private static async Task<IResult> SignInWithCodeAsync(HttpContext context, QueryBuilderEditorOptions options, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!options.Hub.SignInCodes.Enabled)
        {
            return Results.NotFound();
        }

        var request = context.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var services = context.RequestServices;
        string? reason = null;
        CodeCheck? check = null;

        // A form posted from another site carries that site's origin, not ours; a missing header is refused too.
        if (!OriginMatches(request))
        {
            reason = "OriginMismatch";
        }
        else
        {
            var code = await ReadCodeAsync(context, ct);
            if (code is null)
            {
                reason = nameof(CodeRefusal.Malformed);
            }
            else
            {
                check = services.GetRequiredService<SuperAdminSignInCodes>().Verify(code);
                if (check.Refusal is { } refusal)
                {
                    reason = refusal.ToString();
                }
            }
        }

        var clock = services.GetRequiredService<TimeProvider>();
        if (reason is null && check is { Jti: { } jti, ExpiresUtc: { } expires })
        {
            // Single use: the id is recorded before anything else happens, and a second try finds it there.
            bool first;
            await using (var scope = services.GetRequiredService<IHubWideScopeFactory>().Create())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var replays = services.GetRequiredService<LaunchReplayStore>();
                await replays.CleanUpAsync(db, clock.GetUtcNow(), ct);
                first = await replays.TryRecordAsync(db, App.NoneId, jti, expires.AddSeconds(SuperAdminSignInCodes.SkewSeconds), ct);
            }

            if (!first)
            {
                reason = "Replayed";
            }
        }

        if (reason is not null || check?.Name is null)
        {
            // The visitor's existing session is left alone: a cross-site form must not be able to end anyone's session,
            // and no refusal path here creates one.
            await RecordCodeRefusalAsync(context, reason ?? nameof(CodeRefusal.Malformed), clock, ct);
            return Results.Content(CodeRefusalPage, "text/html; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
        }

        var codeName = check.Name;
        var now = clock.GetUtcNow();
        var userId = "code:" + codeName;
        await using (var scope = services.GetRequiredService<IHubWideScopeFactory>().Create())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogEntries.Add(new AuditLogEntry
            {
                Actor = $"super:{userId}",
                Action = AuditAction.SuperAdminSignedIn,
                EntityType = "SuperAdmin",
                EntityName = userId,
                Summary = "Super admin signed in",
                DetailsJson = JsonSerializer.Serialize(new { objectId = userId, matchedBy = SuperAdminMatchKind.Code.ToString(), method = "code", jti = check.Jti }, QueryDefinitionSerializer.Options),
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                TimestampUtc = now,
            });
            await db.SaveChangesAsync(ct);
        }

        var session = HubSession.ForSuperAdmin(userId, codeName, null, null, now, SuperAdminMatchKind.Code, codeName, "", SuperAdminRecognizer.CodeProvider);
        await context.SignOutAsync(HubSession.Scheme);
        await context.SignInAsync(HubSession.Scheme, session, new AuthenticationProperties { IsPersistent = false });

        context.Response.Headers.Location = Home;
        return Results.StatusCode(StatusCodes.Status303SeeOther);
    }

    private static bool OriginMatches(HttpRequest request) =>
        request.Headers.TryGetValue("Origin", out var origin) && origin.Count == 1
        && string.Equals(origin[0], $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> ReadCodeAsync(HttpContext context, CancellationToken ct)
    {
        var request = context.Request;
        var limit = SuperAdminSignInCodes.MaxCodeLength;
        if (request.ContentLength is { } length && length > limit + 64)
        {
            return null;
        }

        var sizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = limit + 1024;
        }

        try
        {
            var form = await new FormFeature(request, new FormOptions
            {
                ValueLengthLimit = limit, KeyLengthLimit = 64, ValueCountLimit = 4, BufferBody = false,
            }).ReadFormAsync(ct);
            return form.TryGetValue("code", out var values) && values.Count == 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0]!.Trim() : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            return null;
        }
    }

    private static async Task RecordCodeRefusalAsync(HttpContext context, string reason, TimeProvider clock, CancellationToken ct)
    {
        await using var scope = context.RequestServices.GetRequiredService<IHubWideScopeFactory>().Create();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = "anonymous",
            Action = AuditAction.SuperAdminSignInRefused,
            EntityType = "SuperAdmin",
            // The reason is a fixed name; the code itself is never written anywhere.
            Summary = $"Super admin sign-in refused: {reason}",
            DetailsJson = JsonSerializer.Serialize(new { method = "code", reason }, QueryDefinitionSerializer.Options),
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            TimestampUtc = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
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

    private static string? Cap(string? value) => value is { Length: > 200 } ? value[..200] : value;

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

        var session = HubSession.ForSuperAdmin(match.ObjectId, match.Name, match.Email, null, now, match.Kind, match.MatchedValue, match.TenantId ?? recognition.TenantId ?? "", options.Hub.SuperAdmins.ProviderKind.ToString());
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
            var target = appId == App.DefaultId || appId == App.NoneId ? null : await db.Apps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appId, ct);
            if (target is null)
            {
                return Results.NotFound();
            }

            // Written inside the target app (AppId = that app) so its own admins can see that staff looked, and before the
            // switch, so there is no view without a record. The actor is masked for them when they read it.
            db.AuditLogEntries.Add(new AuditLogEntry
            {
                Actor = $"super:{objectId}",
                Action = AuditAction.SuperAdminViewedApp,
                EntityType = nameof(App),
                EntityId = target.Id,
                EntityName = target.Name,
                AppId = target.Id,
                Summary = $"A super admin viewed '{target.Name}'",
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                TimestampUtc = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
        }

        // The original issue time is kept: switching apps must not stretch the 8 h absolute lifetime.
        var next = HubSession.ForSuperAdmin(objectId, principal.FindFirst(HubSession.Name)?.Value, session.Email, request.AppId, issuedAt, matchKind, matchValue, tenantId, HubSession.ReadProvider(principal) ?? "");
        await context.SignInAsync(HubSession.Scheme, next, new AuthenticationProperties { IsPersistent = false });
        return Results.Json(new { viewingAppId = request.AppId }, QueryBuilderJson.Options);
    }
}
