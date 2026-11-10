using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace QueryBuilder.Editor.Hub;

/// <summary>The hub's session cookie: its settings and the per-request checks that end a session early.</summary>
internal static class HubSessionCookie
{
    private static readonly PathString ApiPath = new(QueryBuilderRoutes.BasePath + "/api");

    public static void Configure(CookieAuthenticationOptions cookie, HubOptions hub)
    {
        cookie.Cookie.Name = hub.CookieName;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.Path = "/";
        cookie.Cookie.IsEssential = true;
        // The ticket's own expiry is the idle rule: it slides on activity. The 8 h cap cannot slide, so it is checked
        // from the issued claim in ValidateAsync.
        cookie.ExpireTimeSpan = hub.IdleTimeout;
        cookie.SlidingExpiration = true;

        cookie.Events.OnValidatePrincipal = context => ValidateAsync(context, hub);
        cookie.Events.OnRedirectToLogin = context => ApiOrRedirect(context, StatusCodes.Status401Unauthorized);
        cookie.Events.OnRedirectToAccessDenied = context => ApiOrRedirect(context, StatusCodes.Status403Forbidden);
    }

    private static Task ApiOrRedirect(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        if (context.Request.Path.StartsWithSegments(ApiPath))
        {
            context.Response.StatusCode = status;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }

    private static async Task ValidateAsync(CookieValidatePrincipalContext context, HubOptions hub)
    {
        var services = context.HttpContext.RequestServices;
        var clock = services.GetRequiredService<TimeProvider>();

        var session = HubSession.TryRead(context.Principal);
        var valid = session is not null
            && context.Principal is not null
            && HubSession.TryReadIssued(context.Principal, out var issuedAt)
            && clock.GetUtcNow() - issuedAt <= hub.AbsoluteLifetime;

        // A super admin is not tied to an app; an app session ends the moment its app is gone or turned off.
        if (valid && session is { IsSuperAdmin: false, AppId: { } appId })
        {
            valid = await services.GetRequiredService<HubAppEnabledCache>()
                .IsEnabledAsync(appId, context.HttpContext.RequestAborted);
        }

        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(HubSession.Scheme);
        }
    }
}

/// <summary>
/// Whether an app is still there and enabled, remembered for at most 30 seconds so a disabled app's sessions end
/// quickly without a database read on every request. The read goes through a hub-wide scope of its own: using the
/// request's context here would fix the request's app before its session had been read.
/// </summary>
internal sealed class HubAppEnabledCache(IHubWideScopeFactory hubWide, IMemoryCache cache, TimeProvider clock)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    private sealed record Entry(bool Enabled, DateTimeOffset CheckedAt);

    public async Task<bool> IsEnabledAsync(Guid appId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var key = $"qb:hub:app-enabled:{appId}";

        // The age is compared against the injected clock (not only the cache's own expiry) so tests, and any
        // host that substitutes TimeProvider, see the same 30 s bound.
        if (cache.TryGetValue(key, out Entry? entry) && entry is not null && now - entry.CheckedAt < MaxAge)
        {
            return entry.Enabled;
        }

        bool enabled;
        await using (var scope = hubWide.Create())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            enabled = await db.Apps.AsNoTracking().AnyAsync(a => a.Id == appId && a.IsEnabled, cancellationToken);
        }

        cache.Set(key, new Entry(enabled, now), MaxAge);
        return enabled;
    }
}

/// <summary>
/// Authenticates the hub session at the very start of the pipeline, for QueryBuilder's own paths only, and parks
/// the result on the request. The app is resolved from this and not from <c>HttpContext.User</c>, so it works
/// whatever default scheme (or none) the host configured and no matter where the host calls UseAuthentication.
/// </summary>
internal sealed class HubSessionStartupFilter : IStartupFilter
{
    public const string PrincipalItemKey = "QueryBuilder.Hub.Principal";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Path.StartsWithSegments(QueryBuilderRoutes.BasePath))
            {
                var result = await context.AuthenticateAsync(HubSession.Scheme);
                if (result.Succeeded)
                {
                    context.Items[PrincipalItemKey] = result.Principal;
                }
            }

            await nextMiddleware();
        });
        next(app);
    };
}

internal sealed class HubAppSessionSource(IHttpContextAccessor httpContextAccessor) : IAppSessionSource
{
    private bool _read;
    private HubSessionInfo? _current;

    public HubSessionInfo? Current
    {
        get
        {
            if (!_read)
            {
                _current = HubSession.TryRead(
                    httpContextAccessor.HttpContext?.Items[HubSessionStartupFilter.PrincipalItemKey] as ClaimsPrincipal);
                _read = true;
            }

            return _current;
        }
    }
}
