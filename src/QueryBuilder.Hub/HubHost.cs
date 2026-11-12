using System.Net;
using Microsoft.AspNetCore.Authentication;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using QueryBuilder.Editor;

namespace QueryBuilder.Hub;

public static class HubHost
{
    public const string ProviderScheme = "provider";
    private const string HubExternalScheme = "QueryBuilder.HubExternal";
    private const string LaunchPattern = "/querybuilder/launch";
    private const string CodePattern = "/querybuilder/signin/code";

    /// <param name="configure">
    /// Runs first, before any setting is read, so a test can add configuration, swap the server for TestServer and
    /// replace the <c>provider</c> handler (a <c>PostConfigure&lt;AuthenticationOptions&gt;</c> works whatever the order).
    /// </param>
    /// <param name="forCommand">
    /// Builds the same services for a server command that never starts the host: migrations are forced off so nothing
    /// writes to the database except what the command itself does.
    /// </param>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null, bool forCommand = false)
    {
        // The command reads the app folder's appsettings whatever directory it was started from, and stays quiet so
        // stdout carries only the code and its expiry.
        var builder = forCommand
            ? WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory })
            : WebApplication.CreateBuilder(args);
        if (forCommand)
        {
            // Logging:LogLevel in appsettings.json outranks SetMinimumLevel, so the filter rules are replaced outright and
            // the console goes to stderr: stdout carries only the code and its expiry.
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.Configure<LoggerFilterOptions>(o =>
            {
                o.Rules.Clear();
                o.MinLevel = LogLevel.Warning;
            });
        }

        configure?.Invoke(builder);

        if (builder.Configuration.GetSection("Hub:Entra").Exists())
        {
            throw new InvalidOperationException("Hub:Entra is no longer used. Move those settings to Hub:SignIn:Provider and set Hub:SignIn:Provider:Kind to Entra.");
        }

        var settings = builder.Configuration.GetSection(HubHostOptions.SectionName).Get<HubHostOptions>() ?? new HubHostOptions();
        Require(settings.ConnectionString, "Hub:ConnectionString");
        var provider = settings.SignIn.Provider;
        var kind = ParseKind(provider.Kind);
        if (kind is not null)
        {
            Require(provider.ClientId, "Hub:SignIn:Provider:ClientId");
            Require(provider.ClientSecret, "Hub:SignIn:Provider:ClientSecret");
            if (provider.Scopes.Length > 0 && !provider.Scopes.Contains("openid", StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Hub:SignIn:Provider:Scopes must include openid.");
            }

            if (kind == SuperAdminProviderKind.Entra)
            {
                if (!Uri.TryCreate(provider.Instance, UriKind.Absolute, out var instance) || instance.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException("Hub:SignIn:Provider:Instance must be an absolute https:// address.");
                }

                Require(provider.TenantId, "Hub:SignIn:Provider:TenantId");
                if (provider.TenantId!.Trim().ToLowerInvariant() is "common" or "organizations" or "consumers")
                {
                    throw new InvalidOperationException("Hub:SignIn:Provider:TenantId must name one tenant, not common, organizations or consumers: those let users of any tenant in.");
                }
            }
            else
            {
                Require(provider.Authority, "Hub:SignIn:Provider:Authority");
                if (!Uri.TryCreate(provider.Authority, UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException("Hub:SignIn:Provider:Authority must be an absolute https:// address.");
                }
            }
        }

        if (!Uri.TryCreate(settings.Audience, UriKind.Absolute, out var audience)
            || audience.Scheme != Uri.UriSchemeHttps
            || settings.Audience!.EndsWith('/')
            || settings.Audience.Contains('?')
            || settings.Audience.Contains('#')
            || !string.IsNullOrEmpty(audience.AbsolutePath.Trim('/')))
        {
            throw new InvalidOperationException("Hub:Audience must be the hub's public https:// address only: scheme, host and optional port, with no path, query, fragment or trailing slash.");
        }

        builder.Services.AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = settings.ConnectionString!;
            o.ApplyMigrations = settings.ApplyMigrations && !forCommand;
            o.DefaultMaxRecords = settings.DefaultMaxRecords;
            o.Hub.Enabled = true;
            o.Hub.Audience = settings.Audience ?? string.Empty;
            if (kind is not null)
            {
                o.Hub.SuperAdminSignInScheme = ProviderScheme;
                if (!string.IsNullOrWhiteSpace(provider.DisplayName))
                {
                    o.Hub.ProviderDisplayName = provider.DisplayName;
                }
            }

            o.Hub.SignInCodes.Enabled = settings.SignIn.Codes.Enabled;
            o.Hub.SignInCodes.DefaultMinutes = settings.SignIn.Codes.DefaultMinutes;
            var admins = o.Hub.SuperAdmins;
            admins.ProviderKind = kind ?? SuperAdminProviderKind.Entra;
            admins.GroupsClaim = provider.GroupsClaim;
            admins.TenantIds = [.. settings.SuperAdmins.TenantIds];
            admins.ObjectIds = [.. settings.SuperAdmins.ObjectIds];
            admins.UserIds = [.. settings.SuperAdmins.UserIds];
            admins.Emails = [.. settings.SuperAdmins.Emails];
            admins.GroupIds = [.. settings.SuperAdmins.GroupIds];
            admins.CodeNames = [.. settings.SuperAdmins.CodeNames];
            o.DataSourceManagement.EncryptionKey = settings.DataSourceKey;
            o.DataSourceManagement.PreviousEncryptionKeys = [.. settings.PreviousDataSourceKeys];
        });

        // The package registers the cookie schemes and takes over Data Protection; the host adds only the remote handler.
        if (kind is not null)
        {
            builder.Services.AddAuthentication().AddOpenIdConnect(ProviderScheme, options =>
            {
                options.Authority = kind == SuperAdminProviderKind.Entra
                    ? $"{provider.Instance.TrimEnd('/')}/{provider.TenantId}/v2.0"
                    : provider.Authority;
                options.ClientId = provider.ClientId;
                options.ClientSecret = provider.ClientSecret;
                options.CallbackPath = provider.CallbackPath;
                options.SignInScheme = HubExternalScheme;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.MapInboundClaims = false;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = kind == SuperAdminProviderKind.OpenIdConnect;
                options.Scope.Clear();
                if (kind == SuperAdminProviderKind.OpenIdConnect)
                {
                    // Userinfo is read, but only claims mapped here reach the principal the allowlist checks.
                    options.ClaimActions.MapUniqueJsonKey("email_verified", "email_verified");
                    options.ClaimActions.MapJsonKey(provider.GroupsClaim, provider.GroupsClaim);
                }

                foreach (var scope in provider.Scopes.Length > 0 ? provider.Scopes : ["openid", "profile", "email"])
                {
                    options.Scope.Add(scope);
                }
            });
        }

        builder.Services.AddAuthorization();
        ConfigureForwardedHeaders(builder.Services, settings.ForwardedHeaders);
        ConfigureRateLimiter(builder.Services, settings.LaunchRateLimit, settings.SignIn.Codes);

        var app = builder.Build();

        app.UseForwardedHeaders();
        app.UseHubSecurityHeaders();
        app.UseExceptionHandler();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseQueryBuilderEditorUI();
        app.MapQueryBuilderEditor();
        app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();
        app.MapGet("/", () => Results.Redirect("/querybuilder/")).AllowAnonymous();
        return app;
    }

    private static SuperAdminProviderKind? ParseKind(string? kind) => string.IsNullOrWhiteSpace(kind)
        ? null
        : Enum.GetNames<SuperAdminProviderKind>().Contains(kind.Trim(), StringComparer.Ordinal) && Enum.TryParse<SuperAdminProviderKind>(kind.Trim(), ignoreCase: false, out var parsed)
            ? parsed
            : throw new InvalidOperationException("Hub:SignIn:Provider:Kind must be empty, Entra or OpenIdConnect.");

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }
    }

    private static void ConfigureForwardedHeaders(IServiceCollection services, ForwardedHeadersSettings settings)
    {
        // Checked here, not inside Configure: that callback only runs when the first request resolves the options.
        if (settings.ForwardLimit < 1)
        {
            throw new InvalidOperationException("Hub:ForwardedHeaders:ForwardLimit must be at least 1.");
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // X-Forwarded-Prefix is deliberately not honoured: the hub has no path base.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = settings.ForwardLimit;
            if (settings.KnownProxies.Length == 0 && settings.KnownNetworks.Length == 0)
            {
                return; // ASP.NET Core's default: loopback only.
            }

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in settings.KnownProxies)
            {
                options.KnownProxies.Add(IPAddress.TryParse(proxy, out var ip)
                    ? ip
                    : throw new InvalidOperationException("Hub:ForwardedHeaders:KnownProxies has an entry that is not an IP address."));
            }

            foreach (var network in settings.KnownNetworks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.TryParse(network, out var parsed)
                    ? parsed
                    : throw new InvalidOperationException("Hub:ForwardedHeaders:KnownNetworks has an entry that is not a CIDR range."));
            }
        });
    }

    // Routing has already run here (WebApplication puts it first), so the endpoint is known.
    private static bool IsPost(HttpContext context, string pattern) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: var raw }
        && raw == pattern;

    private static void ConfigureRateLimiter(IServiceCollection services, LaunchRateLimitOptions launch, CodeSettings codes)
    {
        if (launch.PermitLimit < 1 || launch.WindowSeconds < 1)
        {
            throw new InvalidOperationException("Hub:LaunchRateLimit:PermitLimit and WindowSeconds must be at least 1.");
        }

        if (codes.PermitLimit < 1)
        {
            throw new InvalidOperationException("Hub:SignIn:Codes:PermitLimit must be at least 1.");
        }

        if (codes.WindowSeconds < 1)
        {
            throw new InvalidOperationException("Hub:SignIn:Codes:WindowSeconds must be at least 1.");
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // The package maps these endpoints, so a named policy can't be attached to them: one global limiter that
            // limits those routes, each with its own allowance per address, and lets everything else through. Matched by
            // endpoint, not by path text: routing also serves a trailing slash and any letter case, which a path
            // comparison would miss.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (IsPost(context, LaunchPattern))
                {
                    return RateLimitPartition.GetFixedWindowLimiter("launch:" + ip, _ => Window(launch.PermitLimit, launch.WindowSeconds));
                }

                return IsPost(context, CodePattern)
                    ? RateLimitPartition.GetFixedWindowLimiter("code:" + ip, _ => Window(codes.PermitLimit, codes.WindowSeconds))
                    : RateLimitPartition.GetNoLimiter("other");
            });
            options.OnRejected = (rejected, _) =>
            {
                var seconds = IsPost(rejected.HttpContext, CodePattern) ? codes.WindowSeconds : launch.WindowSeconds;
                rejected.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
    }

    private static FixedWindowRateLimiterOptions Window(int permits, int seconds) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromSeconds(seconds),
        QueueLimit = 0,
    };
}
