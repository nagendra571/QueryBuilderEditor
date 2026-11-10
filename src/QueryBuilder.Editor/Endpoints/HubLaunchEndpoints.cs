using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Hub;
using QueryBuilder.Editor.Hub;
using QueryBuilder.Infrastructure.Hub;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Editor.Endpoints;

/// <summary>
/// Launch (a signed token becomes a hub session) and logout. Mapped outside the authorized group: the person
/// arriving has no session yet, and these are never part of the JSON API.
/// </summary>
internal static class HubLaunchEndpoints
{
    public const string LaunchPath = QueryBuilderRoutes.BasePath + "/launch";
    public const string LogoutPath = QueryBuilderRoutes.BasePath + "/logout";
    private const string Home = QueryBuilderRoutes.BasePath + "/";

    // No reason is shown: a refusal that explains itself tells a probing caller which check to get past next.
    internal const string RefusalPage =
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<meta name=\"robots\" content=\"noindex\"><title>QueryBuilder</title>" +
        "<style>body{font-family:system-ui,sans-serif;margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#f4f5f7;color:#1b2430}" +
        "main{max-width:28rem;padding:2rem;text-align:center}</style></head><body><main>" +
        "<p>This link to QueryBuilder isn't valid or has expired. Go back and try again.</p></main></body></html>";

    public static void MapHubLaunchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Any method is routed here (and checked inside) so a GET is a 405, not the single-page app's fallback page.
        endpoints.Map(LaunchPath, LaunchAsync).AllowAnonymous();
        endpoints.MapGet(LogoutPath, LogoutAsync).AllowAnonymous();
    }

    private static async Task<IResult> LaunchAsync(HttpContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (!HttpMethods.IsPost(request.Method))
        {
            context.Response.Headers.Allow = "POST";
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var services = context.RequestServices;
        var token = await ReadTokenAsync(context, ct);
        var result = token is null
            ? LaunchResult.Refused(LaunchRefusalReason.Malformed)
            : await services.GetRequiredService<ILaunchTokenValidator>().ValidateAsync(token, ct);

        if (result.Claims is not { } claims)
        {
            // Hub-wide because an unidentified app has no app of its own to write the entry under.
            await using (var scope = services.GetRequiredService<IHubWideScopeFactory>().Create())
            {
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordLaunchCommand(result), ct);
            }

            await context.SignOutAsync(HubSession.Scheme);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Content(RefusalPage, "text/html; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
        }

        // A new scope, with its app chosen first: the request's own scope must not read (and so freeze) an app
        // from whatever session the browser arrived with.
        await using (var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IAppContextScope>().UseApp(claims.AppId);
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordLaunchCommand(result), ct);
        }

        var principal = HubSession.ForApp(claims.AppId, claims.UserId, claims.Name, claims.Email, claims.Role, claims.Scope,
            claims.ReturnUrl, services.GetRequiredService<TimeProvider>().GetUtcNow());
        await context.SignOutAsync(HubSession.Scheme);
        await context.SignInAsync(HubSession.Scheme, principal, new AuthenticationProperties { IsPersistent = false });

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Location = Home;
        return Results.StatusCode(StatusCodes.Status303SeeOther);
    }

    private static async Task<string?> ReadTokenAsync(HttpContext context, CancellationToken ct)
    {
        var request = context.Request;
        var limit = LaunchTokenValidator.MaxTokenLength;

        // Form encoding can only make the token longer, so the same cap plus the field name bounds the whole body.
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
            return form.TryGetValue("token", out var values) && values.Count == 1 && !string.IsNullOrEmpty(values[0]) ? values[0] : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            return null;
        }
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, CancellationToken ct)
    {
        var session = HubSession.TryRead(context.Items[HubSessionStartupFilter.PrincipalItemKey] as System.Security.Claims.ClaimsPrincipal);
        await context.SignOutAsync(HubSession.Scheme);
        context.Response.Headers.CacheControl = "no-store";

        // Only a URL the app still has registered: the session's copy is a snapshot and may be stale or removed.
        if (session is { IsSuperAdmin: false, ReturnUrl: { } returnUrl, AppId: { } appId })
        {
            await using var scope = context.RequestServices.GetRequiredService<IHubWideScopeFactory>().Create();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var registered = await db.AppReturnUrls.AsNoTracking()
                .Where(u => u.AppId == appId && u.Url == returnUrl)
                .Select(u => u.Url)
                .ToListAsync(ct);
            if (registered.Any(u => string.Equals(u, returnUrl, StringComparison.Ordinal)))
            {
                return Results.Redirect(returnUrl);
            }
        }

        return Results.Redirect(Home);
    }
}
