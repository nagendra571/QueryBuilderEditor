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
    public static void Configure(CookieAuthenticationOptions cookie, HubOptions hub)
    {
        cookie.Cookie.Name = hub.CookieName;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.Path = "/";
        cookie.Cookie.IsEssential = true;
        // Never the host's /Account/Login: nothing here signs in through a form. Both events answer with a bare status.
        cookie.LoginPath = QueryBuilderRoutes.BasePath + "/";
        cookie.AccessDeniedPath = QueryBuilderRoutes.BasePath + "/";
        // The ticket's own expiry is the idle rule: it slides on activity. The 8 h cap cannot slide, so it is checked
        // from the issued claim in ValidateAsync.
        cookie.ExpireTimeSpan = hub.IdleTimeout;
        cookie.SlidingExpiration = true;

        cookie.Events.OnValidatePrincipal = context => ValidateAsync(context, hub);
        cookie.Events.OnRedirectToLogin = context => AnswerWith(context, StatusCodes.Status401Unauthorized);
        cookie.Events.OnRedirectToAccessDenied = context => AnswerWith(context, StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// A remote handler (OpenID Connect) cannot be authenticated on demand, so the host points its SignInScheme at
    /// this cookie and the complete endpoint reads and deletes it. Short-lived and never a session itself.
    /// </summary>
    public static void ConfigureExternal(CookieAuthenticationOptions cookie)
    {
        cookie.Cookie.Name = "__Host-qb-hub-ext";
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.Path = "/";
        cookie.Cookie.IsEssential = true;
        cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        cookie.SlidingExpiration = false;
    }

    private static Task AnswerWith(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }

    // Rounded up: a session issued in the same second as the stamp cannot be told apart from one issued just before it,
    // so it ends too (the holder launches again).
    private static long CeilingSeconds(DateTimeOffset at) =>
        at.ToUnixTimeSeconds() + (at.UtcTicks % TimeSpan.TicksPerSecond == 0 ? 0 : 1);

    private static async Task ValidateAsync(CookieValidatePrincipalContext context, HubOptions hub)
    {
        var services = context.HttpContext.RequestServices;
        var clock = services.GetRequiredService<TimeProvider>();

        var issuedAt = default(DateTimeOffset);
        var session = HubSession.TryRead(context.Principal);
        var valid = session is not null
            && context.Principal is not null
            && HubSession.TryReadIssued(context.Principal, out issuedAt)
            && clock.GetUtcNow() - issuedAt <= hub.AbsoluteLifetime;

        // A super admin is not tied to an app; an app session ends the moment its app is gone or turned off.
        if (valid && session is { IsSuperAdmin: false, AppId: { } appId })
        {
            var state = await services.GetRequiredService<HubAppEnabledCache>()
                .GetAsync(appId, context.HttpContext.RequestAborted);
            valid = state is { Enabled: true }
                && (state.SessionsValidAfterUtc is not { } after || issuedAt.ToUnixTimeSeconds() >= CeilingSeconds(after));
        }
        else if (valid && session is { IsSuperAdmin: true })
        {
            valid = HubSession.TryReadSuperMatch(context.Principal!, out var kind, out var value, out var tenant)
                && SuperAdminRecognizer.StillAllowed(kind, value, tenant, hub.SuperAdmins);
        }

        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(HubSession.Scheme);
            return;
        }

        // The handler's own sliding window only renews past half the timeout, which would make the idle limit
        // anywhere from 30 to 60 minutes; renewing on every request makes it exactly IdleTimeout.
        context.ShouldRenew = true;
    }
}

/// <summary>
/// Whether an app is still there and enabled, and when its sessions became invalid, remembered for at most 30 seconds so
/// a disabled app's (or a revoked key's) sessions end quickly without a database read on every request. The read goes
/// through a hub-wide scope of its own: using the request's context here would fix the request's app before its
/// session had been read.
/// </summary>
internal sealed class HubAppEnabledCache(IHubWideScopeFactory hubWide, IMemoryCache cache, TimeProvider clock)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    internal sealed record AppSessionState(bool Enabled, DateTimeOffset? SessionsValidAfterUtc);

    private sealed record Entry(AppSessionState State, DateTimeOffset CheckedAt);

    public async Task<AppSessionState> GetAsync(Guid appId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var key = $"qb:hub:app-state:{appId}";

        // The age is compared against the injected clock (not only the cache's own expiry) so tests, and any
        // host that substitutes TimeProvider, see the same 30 s bound.
        if (cache.TryGetValue(key, out Entry? entry) && entry is not null && now - entry.CheckedAt < MaxAge)
        {
            return entry.State;
        }

        AppSessionState state;
        await using (var scope = hubWide.Create())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            state = await db.Apps.AsNoTracking().Where(a => a.Id == appId)
                .Select(a => new AppSessionState(a.IsEnabled, a.SessionsValidAfterUtc))
                .FirstOrDefaultAsync(cancellationToken) ?? new AppSessionState(false, null);
        }

        cache.Set(key, new Entry(state, now), MaxAge);
        return state;
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

    // The SPA's files (hashed bundles, icons) never need a session; reading one would renew the cookie onto a response
    // that is cached publicly.
    private static readonly PathString AssetsPath = new(QueryBuilderRoutes.BasePath + "/assets");
    private static readonly PathString ApiPath = new(QueryBuilderRoutes.BasePath + "/api");

    private static bool IsStaticAsset(PathString path) =>
        path.StartsWithSegments(AssetsPath)
        || (!path.StartsWithSegments(ApiPath) && Path.HasExtension(path.Value));

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments(QueryBuilderRoutes.BasePath) && !IsStaticAsset(path))
            {
                // Registered before the cookie handler's own start-of-response work, so it runs after it: whatever
                // that adds, a publicly cacheable response must never carry a session cookie to a shared cache.
                context.Response.OnStarting(() =>
                {
                    if (context.Response.Headers.CacheControl.ToString().Contains("public", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.SetCookie);
                    }

                    return Task.CompletedTask;
                });

                try
                {
                    var result = await context.AuthenticateAsync(HubSession.Scheme);
                    if (result.Succeeded)
                    {
                        context.Items[PrincipalItemKey] = result.Principal;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Data Protection or database trouble: no session (and so no app), never a 500 and never a guess.
                    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<HubSessionStartupFilter>()
                        .LogWarning(ex, "The QueryBuilder hub session could not be read; treating the request as having no session.");
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
