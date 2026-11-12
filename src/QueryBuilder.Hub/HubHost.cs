using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using QueryBuilder.Editor;

namespace QueryBuilder.Hub;

public static class HubHost
{
    public const string EntraScheme = "entra";
    private const string HubExternalScheme = "QueryBuilder.HubExternal";
    private const string LaunchPattern = "/querybuilder/launch";

    /// <param name="configure">
    /// Runs first, before any setting is read, so a test can add configuration, swap the server for TestServer and
    /// replace the <c>entra</c> handler (a <c>PostConfigure&lt;AuthenticationOptions&gt;</c> works whatever the order).
    /// </param>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        var settings = builder.Configuration.GetSection(HubHostOptions.SectionName).Get<HubHostOptions>() ?? new HubHostOptions();
        Require(settings.ConnectionString, "Hub:ConnectionString");
        Require(settings.Entra.TenantId, "Hub:Entra:TenantId");
        Require(settings.Entra.ClientId, "Hub:Entra:ClientId");
        Require(settings.Entra.ClientSecret, "Hub:Entra:ClientSecret");
        if (!Uri.TryCreate(settings.Audience, UriKind.Absolute, out var audience) || audience.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Hub:Audience must be the hub's public https:// URL.");
        }

        builder.Services.AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = settings.ConnectionString!;
            o.ApplyMigrations = settings.ApplyMigrations;
            o.DefaultMaxRecords = settings.DefaultMaxRecords;
            o.Hub.Enabled = true;
            o.Hub.Audience = settings.Audience ?? string.Empty;
            o.Hub.SuperAdminSignInScheme = EntraScheme;
            o.Hub.SuperAdmins.TenantIds = [.. settings.SuperAdmins.TenantIds];
            o.Hub.SuperAdmins.ObjectIds = [.. settings.SuperAdmins.ObjectIds];
            o.Hub.SuperAdmins.Emails = [.. settings.SuperAdmins.Emails];
            o.Hub.SuperAdmins.GroupIds = [.. settings.SuperAdmins.GroupIds];
            o.DataSourceManagement.EncryptionKey = settings.DataSourceKey;
            o.DataSourceManagement.PreviousEncryptionKeys = [.. settings.PreviousDataSourceKeys];
        });

        // The package registers the cookie schemes and takes over Data Protection; the host adds only the remote handler.
        builder.Services.AddAuthentication().AddOpenIdConnect(EntraScheme, options =>
        {
            var entra = settings.Entra;
            options.Authority = $"{entra.Instance.TrimEnd('/')}/{entra.TenantId}/v2.0";
            options.ClientId = entra.ClientId;
            options.ClientSecret = entra.ClientSecret;
            options.CallbackPath = entra.CallbackPath;
            options.SignInScheme = HubExternalScheme;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
        });

        ConfigureForwardedHeaders(builder.Services, settings.ForwardedHeaders);
        ConfigureRateLimiter(builder.Services, settings.LaunchRateLimit);

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
        app.UseQueryBuilderEditorUI();
        app.MapQueryBuilderEditor();
        app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();
        app.MapGet("/", () => Results.Redirect("/querybuilder/")).AllowAnonymous();
        return app;
    }

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
    private static bool IsLaunch(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: LaunchPattern };

    private static void ConfigureRateLimiter(IServiceCollection services, LaunchRateLimitOptions settings)
    {
        if (settings.PermitLimit < 1 || settings.WindowSeconds < 1)
        {
            throw new InvalidOperationException("Hub:LaunchRateLimit:PermitLimit and WindowSeconds must be at least 1.");
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // The package maps the launch endpoint, so a named policy can't be attached to it: one global limiter
            // that limits that route and lets everything else through. Matched by endpoint, not by path text:
            // routing also serves /querybuilder/launch/ and any letter case, which a path comparison would miss.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                IsLaunch(context)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = settings.PermitLimit,
                            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                            QueueLimit = 0,
                        })
                    : RateLimitPartition.GetNoLimiter("other"));
            options.OnRejected = (rejected, _) =>
            {
                rejected.HttpContext.Response.Headers.RetryAfter = settings.WindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
    }
}
