using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class HubLaunchEndpointTests
{
    private const string Audience = "https://hub.test";
    private const string ReturnUrl = "https://a.example/home";
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private sealed class CapturingLogs : ILoggerProvider
    {
        public List<string> Lines { get; } = [];
        public List<string> Errors { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }

        private sealed class Logger(CapturingLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Lines)
                {
                    var line = formatter(state, exception) + (exception?.ToString() ?? "");
                    owner.Lines.Add(line);
                    if (logLevel >= LogLevel.Error)
                    {
                        owner.Errors.Add(line);
                    }
                }
            }
        }
    }

    private sealed class Env : IAsyncDisposable
    {
        public Guid AppA, AppB;
        public RSA RsaA = RSA.Create(2048), RsaB = RSA.Create(2048), Stranger = RSA.Create(2048);
        public CapturingLogs Logs = new();
        public IHost Host = null!;
        public HttpClient Client = null!;
        public DbContextOptions<AppDbContext> Options = null!;
        private readonly Dictionary<string, string> _jar = new();

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public Dictionary<string, object?> Claims(string iss, string sub, Action<Dictionary<string, object?>>? change = null)
        {
            var claims = new Dictionary<string, object?>
            {
                ["iss"] = iss, ["aud"] = Audience, ["sub"] = sub, ["role"] = "Admin", ["name"] = "Mary", ["email"] = "mary@a.example",
                ["iat"] = Now, ["exp"] = Now + 60, ["jti"] = Guid.NewGuid().ToString("N"),
                ["scope"] = new Dictionary<string, string[]> { ["ProgramId"] = ["10", "20"] },
            };
            change?.Invoke(claims);
            return claims;
        }

        public string TokenA(string sub = "user-1", Action<Dictionary<string, object?>>? change = null) =>
            TestTokens.Rs256(RsaA, Claims("app-a", sub, change), "rsa-a");

        public string TokenB(string sub = "user-b") => TestTokens.Rs256(RsaB, Claims("app-b", sub), "rsa-b");

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (_jar.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _jar.Select(p => $"{p.Key}={p.Value}")));
            }

            var response = await Client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var pair = cookie.Split(';')[0];
                    var eq = pair.IndexOf('=');
                    var name = pair[..eq];
                    var value = pair[(eq + 1)..];
                    if (value.Length == 0 || cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                    {
                        _jar.Remove(name);
                    }
                    else
                    {
                        _jar[name] = value;
                    }
                }
            }

            return response;
        }

        public Task<HttpResponseMessage> LaunchAsync(string token) => SendAsync(new HttpRequestMessage(HttpMethod.Post, "/querybuilder/launch")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }),
        });

        public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(new HttpRequestMessage(HttpMethod.Get, url));

        public async Task<string?> MeUserAsync()
        {
            var response = await GetAsync("/querybuilder/api/me");
            // Null means 401: no session (or none that resolves to an app). Callers assert that explicitly.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("userId").GetString();
        }

        public async Task<int?> DataSourceCountAsync()
        {
            var response = await GetAsync("/querybuilder/api/data-sources");
            // Null means 401: the gate refuses a session without an app before any query runs.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetArrayLength();
        }

        public AppDbContext Db(Guid? app = null) =>
            new(Options, new FixedAppContext(app ?? App.NoneId, hubWide: app is null));

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Host.StopAsync();
            Host.Dispose();
            RsaA.Dispose(); RsaB.Dispose(); Stranger.Dispose();
        }
    }

    private static Task RunAsync(Func<Env, Task> body, bool hub = true) =>
        MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
        {
            var env = new Env { Options = options };
            await using (var db = env.Db())
            {
                db.MetadataSettings.Add(new MetadataSetting { Key = "Mode", Value = hub ? "Hub" : "Embedded" });
                var a = new App { Key = "app-a", Name = "A", ScopeKeys = ["ProgramId"] };
                var b = new App { Key = "app-b", Name = "B", ScopeKeys = ["ProgramId"] };
                env.AppA = a.Id; env.AppB = b.Id;
                db.Apps.AddRange(a, b);
                db.AppSigningKeys.AddRange(
                    new AppSigningKey { AppId = a.Id, Kid = "rsa-a", Algorithm = "RS256", PublicKeyPem = TestTokens.Pem(env.RsaA) },
                    new AppSigningKey { AppId = b.Id, Kid = "rsa-b", Algorithm = "RS256", PublicKeyPem = TestTokens.Pem(env.RsaB) });
                db.AppReturnUrls.Add(new AppReturnUrl { AppId = a.Id, Url = ReturnUrl });
                await db.SaveChangesAsync();
            }

            await using (var db = env.Db(env.AppA))
            {
                db.DataSources.Add(new DataSource { Id = Guid.NewGuid(), Name = "a1" });
                await db.SaveChangesAsync();
            }

            await using (var db = env.Db(env.AppB))
            {
                db.DataSources.AddRange(new DataSource { Id = Guid.NewGuid(), Name = "b1" }, new DataSource { Id = Guid.NewGuid(), Name = "b2" });
                await db.SaveChangesAsync();
            }

            env.Host = await new HostBuilder().ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging(l => l.AddProvider(env.Logs));
                    services.AddQueryBuilderEditor(o =>
                    {
                        o.ConnectionString = connectionString;
                        o.ApplyMigrations = false;
                        o.Hub.Enabled = hub;
                        o.Hub.Audience = Audience;
                        o.Hub.SuperAdminSignInScheme = "host";
                        o.Hub.SuperAdmins.ObjectIds = ["admin-oid"];
                        o.Hub.SuperAdmins.TenantIds = ["tenant-1"];
                        o.DataSourceManagement.EncryptionKey = Key;
                    });
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        e.MapQueryBuilderEditor();
                        // Stand-ins for the SPA's static files and for an API response a host marks publicly cacheable.
                        e.MapGet("/querybuilder/assets/app.js", ctx =>
                        {
                            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                            return ctx.Response.WriteAsync("x");
                        });
                        e.MapGet("/querybuilder/favicon.svg", ctx => ctx.Response.WriteAsync("x"));
                        e.MapGet("/querybuilder/api/public-test", ctx =>
                        {
                            ctx.Response.Headers.CacheControl = "public, max-age=60";
                            return ctx.Response.WriteAsync("x");
                        });
                    });
                });
            }).StartAsync();
            env.Client = env.Host.GetTestClient();

            await using (env)
            {
                await body(env);
            }
        });

    private static string Text(HttpResponseMessage r) => r.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    [MetadataSqlFact]
    public Task Valid_launch_sets_the_session_and_redirects() => RunAsync(async env =>
    {
        var response = await env.LaunchAsync(env.TokenA());

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/querybuilder/", response.Headers.Location!.OriginalString);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.True(response.Headers.Contains("Set-Cookie"));
        Assert.Equal("user-1", await env.MeUserAsync());
        Assert.Equal(1, await env.DataSourceCountAsync());

        await using var db = env.Db();
        var audit = await db.AuditLogEntries.SingleAsync(a => a.Action == AuditAction.LaunchSucceeded);
        Assert.Equal(env.AppA, audit.AppId);
        Assert.Equal("user-1", audit.Actor);
        using var details = JsonDocument.Parse(audit.DetailsJson!);
        Assert.Equal("Admin", details.RootElement.GetProperty("role").GetString());
        Assert.Equal(2, details.RootElement.GetProperty("scopeValueCounts").GetProperty("ProgramId").GetInt32());
        Assert.True(details.RootElement.GetProperty("returnUrlPresent").ValueKind == JsonValueKind.False);
        Assert.DoesNotContain("\"10\"", audit.DetailsJson);
    });

    [MetadataSqlFact]
    public Task A_replayed_launch_is_refused_without_an_error_in_the_log() => RunAsync(async env =>
    {
        var token = env.TokenA();
        Assert.Equal(HttpStatusCode.SeeOther, (await env.LaunchAsync(token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.LaunchAsync(token)).StatusCode);

        await using var db = env.Db();
        Assert.Contains(await db.AuditLogEntries.ToListAsync(), a => a.Action == AuditAction.LaunchRefused && a.DetailsJson!.Contains("Replayed"));
        Assert.Empty(env.Logs.Errors);
        Assert.Single(await db.LaunchReplays.ToListAsync());
    });

    [MetadataSqlFact]
    public Task A_launch_naming_the_default_app_is_refused_even_with_a_key_on_it() => RunAsync(async env =>
    {
        await using (var db = env.Db())
        {
            db.AppSigningKeys.Add(new AppSigningKey { AppId = App.DefaultId, Kid = "rsa-d", Algorithm = "RS256", PublicKeyPem = TestTokens.Pem(env.RsaA) });
            await db.SaveChangesAsync();
        }

        var token = TestTokens.Rs256(env.RsaA, env.Claims("default", "user-d"), "rsa-d");
        Assert.Equal(HttpStatusCode.BadRequest, (await env.LaunchAsync(token)).StatusCode);
        Assert.Null(await env.MeUserAsync());
    });

    [MetadataSqlFact]
    public Task Launch_upserts_the_seen_user_in_its_app() => RunAsync(async env =>
    {
        Assert.Equal(HttpStatusCode.SeeOther, (await env.LaunchAsync(env.TokenA())).StatusCode);
        Assert.Equal(HttpStatusCode.SeeOther, (await env.LaunchAsync(env.TokenA(change: c => c["name"] = "Mary Q"))).StatusCode);

        await using var db = env.Db();
        var seen = await db.SeenUsers.IgnoreQueryFilters().ToListAsync();
        var row = Assert.Single(seen);
        Assert.Equal(env.AppA, row.AppId);
        Assert.Equal("user-1", row.UserId);
        Assert.Equal("Mary Q", row.Name);
        Assert.Equal("mary@a.example", row.Email);
    });

    [MetadataSqlFact]
    public Task Refused_launch_is_400_generic_audited_with_reason_and_never_stores_the_token() => RunAsync(async env =>
    {
        var wrongKey = TestTokens.Rs256(env.Stranger, env.Claims("app-a", "user-1"), "rsa-a");
        var response = await env.LaunchAsync(wrongKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("This link to QueryBuilder isn't valid or has expired. Go back and try again.", body);
        Assert.DoesNotContain("BadSignature", body);
        Assert.DoesNotContain("<script", body);
        Assert.DoesNotContain("http", body.Replace("http-equiv", ""));
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.Contains("qb-hub=") && !c.Contains("expires=Thu, 01 Jan 1970")));

        await env.LaunchAsync("not-a-token");
        await env.LaunchAsync(TestTokens.Rs256(env.RsaA, env.Claims("app-a", "user-1", c => c["aud"] = "https://elsewhere"), "rsa-a"));

        await using var db = env.Db();
        var rows = await db.AuditLogEntries.IgnoreQueryFilters().Where(a => a.Action == AuditAction.LaunchRefused).OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("anonymous", r.Actor));

        // Unproven app: hub-level entry, the claimed key only inside the details.
        Assert.Null(rows[0].AppId);
        Assert.Contains("\"claimedApp\":\"app-a\"", rows[0].DetailsJson);
        Assert.Contains("BadSignature", rows[0].DetailsJson);
        Assert.Contains("\"authenticated\":false", rows[0].DetailsJson);
        Assert.Contains("unauthenticated", rows[0].Summary);

        Assert.Null(rows[1].AppId);
        Assert.Contains("Malformed", rows[1].DetailsJson);

        Assert.Equal(env.AppA, rows[2].AppId);
        Assert.Contains("WrongAudience", rows[2].DetailsJson);
        Assert.Contains("\"authenticated\":true", rows[2].DetailsJson);

        var signature = wrongKey.Split('.')[2];
        foreach (var row in await db.AuditLogEntries.IgnoreQueryFilters().ToListAsync())
        {
            Assert.DoesNotContain(signature, row.Summary + row.DetailsJson + row.EntityName);
            Assert.DoesNotContain(wrongKey.Split('.')[1], row.Summary + row.DetailsJson + row.EntityName);
        }

        lock (env.Logs.Lines)
        {
            Assert.DoesNotContain(env.Logs.Lines, l => l.Contains(signature) || l.Contains(wrongKey.Split('.')[1]));
        }
    });

    [MetadataSqlFact]
    public Task A_refused_launch_ends_the_current_session() => RunAsync(async env =>
    {
        await env.LaunchAsync(env.TokenA());
        Assert.Equal("user-1", await env.MeUserAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await env.LaunchAsync("garbage")).StatusCode);
        Assert.Null(await env.MeUserAsync());
    });

    [MetadataSqlFact]
    public Task Launch_accepts_only_form_posts() => RunAsync(async env =>
    {
        var get = await env.GetAsync("/querybuilder/launch");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Equal("POST", string.Join(",", get.Content.Headers.Allow));

        var json = await env.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/querybuilder/launch")
        {
            Content = new StringContent("{\"token\":\"x\"}", Encoding.UTF8, "application/json"),
        });
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, json.StatusCode);

        var multipart = await env.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/querybuilder/launch")
        {
            Content = new MultipartFormDataContent { { new StringContent(env.TokenA()), "token" } },
        });
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, multipart.StatusCode);

        // The rejected requests were not launch attempts, so they left no audit rows.
        await using var db = env.Db();
        Assert.Empty(await db.AuditLogEntries.IgnoreQueryFilters().ToListAsync());
    });

    [MetadataSqlFact]
    public Task An_oversized_or_missing_token_is_refused_as_malformed() => RunAsync(async env =>
    {
        var huge = await env.LaunchAsync(new string('a', 600 * 1024));
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);

        var none = await env.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/querybuilder/launch")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["other"] = "x" }),
        });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);

        await using var db = env.Db();
        var rows = await db.AuditLogEntries.IgnoreQueryFilters().Where(a => a.Action == AuditAction.LaunchRefused).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Contains("Malformed", r.DetailsJson));
    });

    [MetadataSqlFact]
    public Task Second_launch_for_another_app_replaces_the_session() => RunAsync(async env =>
    {
        await env.LaunchAsync(env.TokenA());
        Assert.Equal("user-1", await env.MeUserAsync());
        Assert.Equal(1, await env.DataSourceCountAsync());

        Assert.Equal(HttpStatusCode.SeeOther, (await env.LaunchAsync(env.TokenB())).StatusCode);
        Assert.Equal("user-b", await env.MeUserAsync());
        Assert.Equal(2, await env.DataSourceCountAsync());

        await using var db = env.Db();
        Assert.Equal(env.AppB, (await db.AuditLogEntries.IgnoreQueryFilters().SingleAsync(a => a.Actor == "user-b")).AppId);
    });

    [MetadataSqlFact]
    public Task Logout_redirects_to_a_registered_return_url_only() => RunAsync(async env =>
    {
        await env.LaunchAsync(env.TokenA(change: c => c["returnUrl"] = ReturnUrl));
        var logout = await env.GetAsync("/querybuilder/logout");
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Assert.Equal(ReturnUrl, logout.Headers.Location!.OriginalString);
        Assert.Null(await env.MeUserAsync());

        // The session's copy of the URL is a snapshot: once the app drops it, logout no longer follows it.
        await env.LaunchAsync(env.TokenA(change: c => c["returnUrl"] = ReturnUrl));
        await using (var db = env.Db())
        {
            db.AppReturnUrls.RemoveRange(await db.AppReturnUrls.IgnoreQueryFilters().ToListAsync());
            await db.SaveChangesAsync();
        }

        var after = await env.GetAsync("/querybuilder/logout");
        Assert.Equal(HttpStatusCode.Found, after.StatusCode);
        Assert.Equal("/querybuilder/", after.Headers.Location!.OriginalString);

        var noSession = await env.GetAsync("/querybuilder/logout");
        Assert.Equal("/querybuilder/", noSession.Headers.Location!.OriginalString);
    });

    [MetadataSqlFact]
    public Task State_changing_api_call_without_the_header_is_400_in_hub_mode() => RunAsync(async env =>
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            var refused = await env.SendAsync(new HttpRequestMessage(method, "/querybuilder/api/nothing-here"));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            Assert.Contains("X-QueryBuilder-Request", problem.RootElement.GetProperty("detail").GetString());
            Assert.Equal("application/problem+json", refused.Content.Headers.ContentType!.MediaType);

            var wrongValue = new HttpRequestMessage(method, "/querybuilder/api/nothing-here");
            wrongValue.Headers.Add("X-QueryBuilder-Request", "0");
            Assert.Equal(HttpStatusCode.BadRequest, (await env.SendAsync(wrongValue)).StatusCode);

            var allowed = new HttpRequestMessage(method, "/querybuilder/api/nothing-here");
            allowed.Headers.Add("X-QueryBuilder-Request", "1");
            Assert.Equal(HttpStatusCode.NotFound, (await env.SendAsync(allowed)).StatusCode);
        }

        // Reads never need it, and neither does the launch endpoint (it creates the session).
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.GetAsync("/querybuilder/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.SeeOther, (await env.LaunchAsync(env.TokenA())).StatusCode);
    });

    [MetadataSqlFact]
    public Task Embedded_mode_has_no_launch_endpoint_and_no_header_rule() => RunAsync(async env =>
    {
        Assert.Equal(HttpStatusCode.NotFound, (await env.LaunchAsync("x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.GetAsync("/querybuilder/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/querybuilder/api/nothing-here"))).StatusCode);
    }, hub: false);

    [MetadataSqlFact]
    public Task Static_and_publicly_cacheable_responses_never_carry_the_session_cookie() => RunAsync(async env =>
    {
        await env.LaunchAsync(env.TokenA());

        // Control: an API response from a signed-in user renews the cookie, so the checks below can fail.
        var me = await env.GetAsync("/querybuilder/api/me");
        Assert.True(me.Headers.Contains("Set-Cookie"));

        foreach (var url in new[] { "/querybuilder/assets/app.js", "/querybuilder/favicon.svg" })
        {
            var response = await env.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"), url);
        }

        // On an API path the session is still read and renewed; the cookie handler then makes the response
        // non-cacheable, and whatever the order, a publicly cacheable response must never carry the cookie.
        var shared = await env.GetAsync("/querybuilder/api/public-test");
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        Assert.False(shared.Headers.Contains("Set-Cookie") && shared.Headers.CacheControl?.Public == true);
    });

    [MetadataSqlFact]
    public Task A_refusal_while_signed_in_is_never_written_under_the_sessions_app() => RunAsync(async env =>
    {
        await env.LaunchAsync(env.TokenA());
        Assert.Equal("user-1", await env.MeUserAsync());

        // Pre-signature (signed by the wrong key, claiming app B): hub-level, not app A and not even app B.
        var forged = TestTokens.Rs256(env.Stranger, env.Claims("app-b", "x"), "rsa-b");
        Assert.Equal(HttpStatusCode.BadRequest, (await env.LaunchAsync(forged)).StatusCode);
        Assert.Null(await env.MeUserAsync());

        // Post-signature (valid but expired): the token's verified app, still not the session's.
        await env.LaunchAsync(env.TokenA());
        var expired = TestTokens.Rs256(env.RsaB, env.Claims("app-b", "y", c => { c["iat"] = Env.Now - 200; c["exp"] = Env.Now - 100; }), "rsa-b");
        Assert.Equal(HttpStatusCode.BadRequest, (await env.LaunchAsync(expired)).StatusCode);
        Assert.Null(await env.MeUserAsync());

        await using var db = env.Db();
        var rows = await db.AuditLogEntries.IgnoreQueryFilters().Where(a => a.Action == AuditAction.LaunchRefused).OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Null(rows[0].AppId);
        Assert.Contains("\"claimedApp\":\"app-b\"", rows[0].DetailsJson);
        Assert.Equal(env.AppB, rows[1].AppId);
        Assert.Contains("Expired", rows[1].DetailsJson);
        Assert.DoesNotContain(rows, r => r.AppId == env.AppA);
    });

    private static ServiceProvider BuildProvider(bool hub)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        // A real host always has these; the package expects them.
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = @"Server=(localdb)\MSSQLLocalDB;Database=unused;Integrated Security=true";
            o.ApplyMigrations = false;
            o.Hub.Enabled = hub;
            o.Hub.Audience = Audience;
            o.Hub.SuperAdminSignInScheme = "host";
            o.Hub.SuperAdmins.ObjectIds = ["admin-oid"];
            o.Hub.SuperAdmins.TenantIds = ["tenant-1"];
            o.DataSourceManagement.EncryptionKey = Key;
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void Embedded_service_provider_validates_on_build() => BuildProvider(hub: false).Dispose();

    [Fact]
    public void Hub_service_provider_validates_on_build() => BuildProvider(hub: true).Dispose();
}
