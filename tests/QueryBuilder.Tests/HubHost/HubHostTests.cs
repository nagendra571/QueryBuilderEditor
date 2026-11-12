using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using HubHostApp = global::QueryBuilder.Hub.HubHost;
using QueryBuilder.Tests.Hub;

namespace QueryBuilder.Tests.HubHostSuite;

/// <summary>Stands in for Entra: a challenge signs a super admin into the hub's external cookie, stamped the way a real remote handler stamps its ticket.</summary>
internal sealed class FakeEntraHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", "admin-oid"), new Claim("tid", "tenant-1"), new Claim("name", "Ada Admin"), new Claim("email", "ada@example.com")], "fake"));
        var stamped = new AuthenticationProperties(new Dictionary<string, string?>(properties.Items)) { RedirectUri = properties.RedirectUri };
        stamped.Items[".AuthScheme"] = Scheme.Name;
        await Context.SignInAsync("QueryBuilder.HubExternal", principal, stamped);
        Context.Response.Redirect(properties.RedirectUri ?? "/");
    }
}

/// <summary>TestServer has no remote address; a header lets a test pick one, ahead of the host's own forwarded-headers middleware.</summary>
internal sealed class RemoteAddressFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Remote", out var value) && IPAddress.TryParse(value, out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }

            return nextMiddleware();
        });
        next(app);
    };
}

[Collection(MetadataDatabaseCollection.Name)]
public class HubHostTests
{
    private static Dictionary<string, string?> Settings(string connection) => new()
    {
        ["Hub:ConnectionString"] = connection,
        ["Hub:Audience"] = "https://hub.test",
        ["Hub:SignIn:Provider:Kind"] = "Entra",
        ["Hub:SignIn:Provider:TenantId"] = "tenant-1",
        ["Hub:SignIn:Provider:ClientId"] = "client-1",
        ["Hub:SignIn:Provider:ClientSecret"] = "not-a-real-secret",
        ["Hub:SuperAdmins:TenantIds:0"] = "tenant-1",
        ["Hub:SuperAdmins:ObjectIds:0"] = "admin-oid",
        ["Hub:DataSourceKey"] = HubApiHost.Key,
    };

    private static WebApplication Build(Dictionary<string, string?> settings, bool fakeEntra = true) =>
        HubHostApp.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.AddSingleton<IStartupFilter, RemoteAddressFilter>();
            if (fakeEntra)
            {
                // Swaps only the handler type: the real OpenIdConnectOptions stay registered for the options test.
                builder.Services.PostConfigure<AuthenticationOptions>(o =>
                    o.Schemes.Single(s => s.Name == "provider").HandlerType = typeof(FakeEntraHandler));
            }
        });

    private static async Task RunAsync(Func<WebApplication, HttpClient, Task> body, Dictionary<string, string?>? extra = null, bool fakeEntra = true) =>
        await MetadataDb.WithDatabaseAsync(async (_, connection) =>
        {
            var settings = Settings(connection);
            foreach (var pair in extra ?? [])
            {
                settings[pair.Key] = pair.Value;
            }

            await using var app = Build(settings, fakeEntra);
            await app.StartAsync();
            try
            {
                using var client = app.GetTestClient();
                await body(app, client);
            }
            finally
            {
                await app.StopAsync();
            }
        });

    private static HttpRequestMessage Request(HttpMethod method, string url, string? remote = null, params (string, string)[] headers)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Host = "hub.test";
        if (remote is not null)
        {
            request.Headers.Add("X-Test-Remote", remote);
        }

        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return request;
    }

    [MetadataSqlFact]
    public Task Security_headers_on_every_response() => RunAsync(async (_, client) =>
    {
        foreach (var url in new[] { "/healthz", "/", "/querybuilder/api/me", "/nothing-here" })
        {
            var response = await client.SendAsync(Request(HttpMethod.Get, url));
            Assert.Equal("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        }
    });

    [MetadataSqlFact]
    public Task Launch_is_rate_limited_per_ip() => RunAsync(async (_, client) =>
    {
        async Task<HttpResponseMessage> Launch(string ip)
        {
            var request = Request(HttpMethod.Post, "/querybuilder/launch", ip);
            request.Content = new FormUrlEncodedContent([]);
            return await client.SendAsync(request);
        }

        for (var i = 0; i < 30; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Launch("10.1.1.1")).StatusCode);
        }

        var limited = await Launch("10.1.1.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("frame-ancestors 'self'", limited.Headers.GetValues("Content-Security-Policy").Single());

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Launch("10.2.2.2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request(HttpMethod.Get, "/healthz", "10.1.1.1"))).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.SendAsync(Request(HttpMethod.Get, "/querybuilder/launch", "10.1.1.1"))).StatusCode);
    });

    [MetadataSqlFact]
    public Task Launch_limit_also_covers_trailing_slash_and_letter_case() => RunAsync(async (_, client) =>
    {
        async Task<HttpResponseMessage> Launch(string path, string ip)
        {
            var request = Request(HttpMethod.Post, path, ip);
            request.Content = new FormUrlEncodedContent([]);
            return await client.SendAsync(request);
        }

        foreach (var (path, ip) in new[] { ("/querybuilder/launch/", "10.3.3.3"), ("/QueryBuilder/Launch", "10.4.4.4") })
        {
            for (var i = 0; i < 30; i++)
            {
                Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Launch(path, ip)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, (await Launch(path, ip)).StatusCode);
        }
    });

    [Theory]
    [InlineData("")]
    [InlineData("http://hub.test")]
    [InlineData("<set in environment>")]
    [InlineData("https://hub.test/")]
    [InlineData("https://hub.test/x")]
    [InlineData("https://hub.test?a=1")]
    public void Audience_must_be_an_https_url(string audience)
    {
        var settings = Settings("Server=unused");
        settings["Hub:Audience"] = audience;

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Hub:Audience", ex.Message);
        if (audience.Length > 0)
        {
            Assert.DoesNotContain(audience, ex.Message);
        }
    }

    [Theory]
    [InlineData("https://hub.test")]
    [InlineData("https://hub.test:8443")]
    public void Audience_with_only_scheme_host_and_port_is_accepted(string audience)
    {
        var settings = Settings("Server=unused");
        settings["Hub:Audience"] = audience;

        Build(settings);
    }

    [Theory]
    [InlineData("Hub:ConnectionString")]
    [InlineData("Hub:SignIn:Provider:TenantId")]
    [InlineData("Hub:SignIn:Provider:ClientId")]
    [InlineData("Hub:SignIn:Provider:ClientSecret")]
    public void Missing_settings_refuse_startup_naming_the_setting(string missing)
    {
        var settings = Settings("Server=unused");
        settings.Remove(missing);

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains(missing, ex.Message);
        Assert.DoesNotContain("not-a-real-secret", ex.Message);
    }

    [MetadataSqlFact]
    public Task Oidc_scheme_signs_into_the_hub_external_scheme() => RunAsync(async (app, _) =>
    {
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("provider");

        Assert.Equal("QueryBuilder.HubExternal", options.SignInScheme);
        Assert.True(options.UsePkce);
        Assert.False(options.MapInboundClaims);
        Assert.Equal("code", options.ResponseType);
        Assert.Equal("https://login.microsoftonline.com/tenant-1/v2.0", options.Authority);
        Assert.Equal(["openid", "profile", "email"], options.Scope);
        Assert.False(options.SaveTokens);
        Assert.False(options.GetClaimsFromUserInfoEndpoint);
        await Task.CompletedTask;
    }, fakeEntra: false);

    [MetadataSqlFact]
    public Task Super_admin_sign_in_round_trip_with_fake_entra() => RunAsync(async (_, client) =>
    {
        var browser = new HubApiHost.Browser(client);

        var start = await browser.GetAsync("/querybuilder/signin");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);

        var complete = await browser.GetAsync("/querybuilder/signin/complete");
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        Assert.Contains("__Host-qb-hub", browser.Cookies.Keys);

        var me = await browser.GetAsync("/querybuilder/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    });

    [MetadataSqlFact]
    public Task Forwarded_proto_from_a_known_proxy_makes_the_request_https() => RunAsync(async (_, client) =>
    {
        var trusted = await client.SendAsync(Request(HttpMethod.Get, "/healthz", "10.9.9.9", ("X-Forwarded-Proto", "https")));
        Assert.True(trusted.Headers.Contains("Strict-Transport-Security"));

        var unknown = await client.SendAsync(Request(HttpMethod.Get, "/healthz", "10.8.8.8", ("X-Forwarded-Proto", "https")));
        Assert.False(unknown.Headers.Contains("Strict-Transport-Security"));
    }, new() { ["Hub:ForwardedHeaders:KnownProxies:0"] = "10.9.9.9" });

    [MetadataSqlFact]
    public Task Forward_limit_two_reads_the_client_two_hops_back() => RunAsync(async (_, client) =>
    {
        async Task<HttpResponseMessage> Launch(string forwardedFor)
        {
            var request = Request(HttpMethod.Post, "/querybuilder/launch", "10.9.9.9", ("X-Forwarded-For", forwardedFor));
            request.Content = new FormUrlEncodedContent([]);
            return await client.SendAsync(request);
        }

        for (var i = 0; i < 2; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Launch("203.0.113.5, 10.7.7.7")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Launch("203.0.113.5, 10.7.7.7")).StatusCode);
        // A different client behind the same two proxies has its own allowance: the partition is the client, not the first proxy.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Launch("203.0.113.6, 10.7.7.7")).StatusCode);
    }, new()
    {
        ["Hub:ForwardedHeaders:KnownProxies:0"] = "10.9.9.9",
        ["Hub:ForwardedHeaders:KnownProxies:1"] = "10.7.7.7",
        ["Hub:ForwardedHeaders:ForwardLimit"] = "2",
        ["Hub:LaunchRateLimit:PermitLimit"] = "2",
    });

    [Fact]
    public void Forward_limit_must_be_at_least_one()
    {
        var settings = Settings("Server=unused");
        settings["Hub:ForwardedHeaders:ForwardLimit"] = "0";

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Hub:ForwardedHeaders:ForwardLimit", ex.Message);
    }

    [MetadataSqlFact]
    public Task Host_does_not_configure_data_protection_persistence() => RunAsync((app, _) =>
    {
        var repository = app.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;

        Assert.Equal("MetadataXmlRepository", repository?.GetType().Name);
        return Task.CompletedTask;
    });

    [MetadataSqlFact]
    public Task Healthz_is_ok_and_root_redirects() => RunAsync(async (_, client) =>
    {
        var health = await client.SendAsync(Request(HttpMethod.Get, "/healthz"));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("ok", await health.Content.ReadAsStringAsync());

        var root = await client.SendAsync(Request(HttpMethod.Get, "/"));
        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/querybuilder/", root.Headers.Location?.OriginalString);
    });

    [MetadataSqlFact]
    public Task No_path_base() => RunAsync(async (_, client) =>
    {
        var plain = await client.SendAsync(Request(HttpMethod.Get, "/querybuilder/api/me", "10.9.9.9"));
        var prefixed = await client.SendAsync(Request(HttpMethod.Get, "/querybuilder/api/me", "10.9.9.9", ("X-Forwarded-Prefix", "/foo")));
        var underPrefix = await client.SendAsync(Request(HttpMethod.Get, "/foo/querybuilder/api/me", "10.9.9.9"));

        Assert.NotEqual(HttpStatusCode.NotFound, plain.StatusCode);
        Assert.Equal(plain.StatusCode, prefixed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, underPrefix.StatusCode);
    }, new() { ["Hub:ForwardedHeaders:KnownProxies:0"] = "10.9.9.9" });
}
