using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
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
public class SuperAdminTests
{
    private const string Audience = "https://hub.test";
    private const string AdminOid = "11111111-aaaa-bbbb-cccc-000000000001";
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private sealed class OffsetClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        public List<string> Lines { get; } = [];
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
                    owner.Lines.Add($"{logLevel}: {formatter(state, exception)}");
                }
            }
        }
    }

    /// <summary>
    /// Stands in for the host's OpenID Connect handler: when challenged it does what the callback would, leaving the
    /// user in the package's external cookie and returning to the RedirectUri. Who signs in comes from the query string.
    /// </summary>
    private sealed class FakeHostScheme : IAuthenticationHandler
    {
        private HttpContext _context = null!;

        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context)
        {
            _context = context;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        public async Task ChallengeAsync(AuthenticationProperties? properties)
        {
            var claims = new List<Claim>();
            foreach (var (key, values) in _context.Request.Query)
            {
                foreach (var value in values)
                {
                    claims.Add(new Claim(key, value!));
                }
            }

            await _context.SignInAsync("QueryBuilder.HubExternal", new ClaimsPrincipal(new ClaimsIdentity(claims, "host")));
            _context.Response.Redirect(properties!.RedirectUri!);
        }

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class Env : IAsyncDisposable
    {
        public Guid AppA, AppB, QueryInA;
        public RSA RsaA = RSA.Create(2048);
        public IHost Host = null!;
        public HttpClient Client = null!;
        public DbContextOptions<AppDbContext> Options = null!;
        public CapturingLogs Logs = new();
        public OffsetClock Clock = new();
        public Dictionary<string, string> Jar { get; } = new();

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (Jar.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", Jar.Select(p => $"{p.Key}={p.Value}")));
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
                        Jar.Remove(name);
                    }
                    else
                    {
                        Jar[name] = value;
                    }
                }
            }

            return response;
        }

        public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(new HttpRequestMessage(HttpMethod.Get, url));

        public Task<HttpResponseMessage> PostAsync(string url, object? body, bool header = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
            if (header)
            {
                request.Headers.Add("X-QueryBuilder-Request", "1");
            }

            return SendAsync(request);
        }

        /// <summary>The whole round trip the host's OIDC handler would make; returns the response of the complete step.</summary>
        public async Task<HttpResponseMessage> SignInAsync(params (string Type, string Value)[] claims)
        {
            var query = string.Join("&", claims.Select(c => $"{Uri.EscapeDataString(c.Type)}={Uri.EscapeDataString(c.Value)}"));
            var challenge = await GetAsync("/querybuilder/signin?" + query);
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            Assert.Equal("/querybuilder/signin/complete", challenge.Headers.Location!.OriginalString);
            return await GetAsync("/querybuilder/signin/complete");
        }

        public async Task<string?> MeUserAsync()
        {
            var response = await GetAsync("/querybuilder/api/me");
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("userId").GetString();
        }

        public async Task<int> DataSourceCountAsync()
        {
            var response = await GetAsync("/querybuilder/api/data-sources");
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
            RsaA.Dispose();
        }
    }

    private static Task RunAsync(Func<Env, Task> body) =>
        MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
        {
            var env = new Env { Options = options };
            await using (var db = env.Db())
            {
                db.MetadataSettings.Add(new MetadataSetting { Key = "Mode", Value = "Hub" });
                var a = new App { Key = "app-a", Name = "A", ScopeKeys = ["ProgramId"] };
                var b = new App { Key = "app-b", Name = "B", ScopeKeys = ["ProgramId"], IsEnabled = false };
                env.AppA = a.Id; env.AppB = b.Id;
                db.Apps.AddRange(a, b);
                db.AppSigningKeys.Add(new AppSigningKey { AppId = a.Id, Kid = "rsa-a", Algorithm = "RS256", PublicKeyPem = TestTokens.Pem(env.RsaA) });
                await db.SaveChangesAsync();
            }

            await using (var db = env.Db(env.AppA))
            {
                var source = new DataSource { Id = Guid.NewGuid(), Name = "a1" };
                db.DataSources.Add(source);
                await db.SaveChangesAsync();
                var query = new SavedQuery
                {
                    Id = Guid.NewGuid(), Name = "q", DataSourceId = source.Id, OwnerId = $"super:{AdminOid}", DefinitionJson = "{}",
                };
                env.QueryInA = query.Id;
                db.SavedQueries.Add(query);
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
                    services.AddSingleton<TimeProvider>(env.Clock);
                    services.AddAuthentication(o => o.AddScheme<FakeHostScheme>("host", null));
                    services.AddQueryBuilderEditor(o =>
                    {
                        o.ConnectionString = connectionString;
                        o.ApplyMigrations = false;
                        o.Hub.Enabled = true;
                        o.Hub.Audience = Audience;
                        o.Hub.SuperAdminSignInScheme = "host";
                        o.Hub.SuperAdmins.ObjectIds = [AdminOid];
                        o.Hub.SuperAdmins.Emails = ["Boss@Example.com"];
                        o.Hub.SuperAdmins.GroupIds = ["group-1"];
                        o.DataSourceManagement.EncryptionKey = Key;
                    });
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapQueryBuilderEditor());
                });
            }).StartAsync();
            env.Client = env.Host.GetTestClient();

            await using (env)
            {
                await body(env);
            }
        });

    [MetadataSqlFact]
    public Task Allowlisted_object_id_email_or_group_becomes_super_admin() => RunAsync(async env =>
    {
        var cases = new (string Oid, (string, string)[] Claims, string Kind)[]
        {
            (AdminOid, [("oid", AdminOid)], "ObjectId"),
            ("22222222-0000-0000-0000-000000000002", [("oid", "22222222-0000-0000-0000-000000000002"), ("preferred_username", "boss@example.COM")], "Email"),
            ("33333333-0000-0000-0000-000000000003", [("http://schemas.microsoft.com/identity/claims/objectidentifier", "33333333-0000-0000-0000-000000000003"), ("groups", "other"), ("groups", "group-1")], "Group"),
        };

        foreach (var (oid, claims, kind) in cases)
        {
            env.Jar.Clear();
            var response = await env.SignInAsync(claims);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/querybuilder/", response.Headers.Location!.OriginalString);
            Assert.False(env.Jar.ContainsKey("__Host-qb-hub-ext"), "the external ticket must always be deleted");
            Assert.Equal($"super:{oid}", await env.MeUserAsync());

            await using var db = env.Db();
            var audit = await db.AuditLogEntries.OrderByDescending(a => a.TimestampUtc)
                .FirstAsync(a => a.Action == AuditAction.SuperAdminSignedIn);
            Assert.Null(audit.AppId);
            Assert.Equal($"super:{oid}", audit.Actor);
            using var details = JsonDocument.Parse(audit.DetailsJson!);
            Assert.Equal(kind, details.RootElement.GetProperty("matchedBy").GetString());
            Assert.Equal(oid, details.RootElement.GetProperty("objectId").GetString());
            Assert.DoesNotContain("example.com", audit.DetailsJson + audit.Summary, StringComparison.OrdinalIgnoreCase);
        }
    });

    [MetadataSqlFact]
    public Task Signed_in_but_not_allowlisted_is_403_without_session() => RunAsync(async env =>
    {
        var response = await env.SignInAsync(("oid", "99999999-0000-0000-0000-000000000009"), ("email", "nobody@example.com"), ("groups", "group-2"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("QueryBuilder.Hub", response.Headers.TryGetValues("Set-Cookie", out var c) ? string.Join(";", c) : "");
        Assert.False(env.Jar.ContainsKey("__Host-qb-hub"));
        Assert.False(env.Jar.ContainsKey("__Host-qb-hub-ext"));
        Assert.Equal("anonymous", await env.MeUserAsync());

        await using var db = env.Db();
        Assert.False(await db.AuditLogEntries.AnyAsync(a => a.Action == AuditAction.SuperAdminSignedIn));
    });

    [MetadataSqlFact]
    public Task Completing_without_a_host_sign_in_is_403() => RunAsync(async env =>
    {
        var response = await env.GetAsync("/querybuilder/signin/complete");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(env.Jar.ContainsKey("__Host-qb-hub"));
    });

    [MetadataSqlFact]
    public Task A_groups_overage_is_not_a_match_and_is_logged() => RunAsync(async env =>
    {
        var response = await env.SignInAsync(("oid", "44444444-0000-0000-0000-000000000004"), ("_claim_names", "{\"groups\":\"src1\"}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(env.Logs.Lines, l => l.StartsWith("Information:") && l.Contains("groups"));
    });

    [MetadataSqlFact]
    public Task Launch_token_with_role_SuperAdmin_is_refused_UnknownRole() => RunAsync(async env =>
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var token = TestTokens.Rs256(env.RsaA, new Dictionary<string, object?>
        {
            ["iss"] = "app-a", ["aud"] = Audience, ["sub"] = "u1", ["role"] = "SuperAdmin", ["iat"] = now, ["exp"] = now + 60,
            ["jti"] = Guid.NewGuid().ToString("N"), ["unrestricted"] = true,
        }, "rsa-a");

        var response = await env.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/querybuilder/launch")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(env.Jar.ContainsKey("__Host-qb-hub"));
        Assert.Equal("anonymous", await env.MeUserAsync());
        await using var db = env.Db();
        var audit = await db.AuditLogEntries.SingleAsync(a => a.Action == AuditAction.LaunchRefused);
        Assert.Contains("UnknownRole", audit.DetailsJson);
    });

    [MetadataSqlFact]
    public Task View_app_switches_resolution_and_audits_inside_that_app() => RunAsync(async env =>
    {
        await env.SignInAsync(("oid", AdminOid), ("name", "Super Sam"));
        Assert.Equal(0, await env.DataSourceCountAsync());

        var toA = await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = env.AppA });
        Assert.Equal(HttpStatusCode.OK, toA.StatusCode);
        Assert.Equal(1, await env.DataSourceCountAsync());
        Assert.Equal($"super:{AdminOid}", await env.MeUserAsync());

        // Disabled apps can be viewed: a super admin must be able to see what they turned off.
        Assert.Equal(HttpStatusCode.OK, (await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = env.AppB })).StatusCode);
        Assert.Equal(2, await env.DataSourceCountAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(2, await env.DataSourceCountAsync());

        Assert.Equal(HttpStatusCode.OK, (await env.PostAsync("/querybuilder/api/hub/view-app", new { })).StatusCode);
        Assert.Equal(0, await env.DataSourceCountAsync());

        // Inside app A, a normal handler's audit entry lands in A, attributed to the super admin.
        await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = env.AppA });
        var disable = await env.PostAsync($"/querybuilder/api/queries/{env.QueryInA}/disable", null);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        await using var db = env.Db();
        var audit = await db.AuditLogEntries.SingleAsync(a => a.Action == AuditAction.QueryDisabled);
        Assert.Equal(env.AppA, audit.AppId);
        Assert.Equal($"super:{AdminOid}", audit.Actor);
    });

    [MetadataSqlFact]
    public Task View_app_needs_a_super_admin_session_and_the_request_header() => RunAsync(async env =>
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = env.AppA })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = env.AppA }, header: false)).StatusCode);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var token = TestTokens.Rs256(env.RsaA, new Dictionary<string, object?>
        {
            ["iss"] = "app-a", ["aud"] = Audience, ["sub"] = "u1", ["role"] = "Admin", ["iat"] = now, ["exp"] = now + 60,
            ["jti"] = Guid.NewGuid().ToString("N"), ["unrestricted"] = true,
        }, "rsa-a");
        var launch = await env.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/querybuilder/launch")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }),
        });
        Assert.Equal(HttpStatusCode.SeeOther, launch.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = env.AppA })).StatusCode);
        Assert.Equal("u1", await env.MeUserAsync());
    });

    [MetadataSqlFact]
    public Task Switching_apps_does_not_extend_the_absolute_lifetime() => RunAsync(async env =>
    {
        await env.SignInAsync(("oid", AdminOid));

        // Active every 50 minutes, so the 1 h idle limit never ends it; only the 8 h cap can.
        for (var minutes = 50; minutes <= 450; minutes += 50)
        {
            env.Clock.Offset = TimeSpan.FromMinutes(minutes);
            Assert.Equal($"super:{AdminOid}", await env.MeUserAsync());
        }

        env.Clock.Offset = TimeSpan.FromMinutes(470);
        Assert.Equal(HttpStatusCode.OK, (await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = env.AppA })).StatusCode);
        Assert.Equal(1, await env.DataSourceCountAsync());

        env.Clock.Offset = TimeSpan.FromMinutes(490);
        Assert.Equal("anonymous", await env.MeUserAsync());
    });

    [Fact]
    public void Recognizer_matches_exactly_and_ignores_blank_entries()
    {
        var allowed = new SuperAdminOptions { ObjectIds = ["Oid-1", " "], Emails = ["A@B.com", ""], GroupIds = ["g1"] };
        static ClaimsPrincipal P(params (string, string)[] c) => new(new ClaimsIdentity(c.Select(x => new Claim(x.Item1, x.Item2)), "host"));

        Assert.Equal(QueryBuilder.Editor.Hub.SuperAdminMatchKind.ObjectId, QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("oid", "Oid-1")), allowed)!.Kind);
        Assert.Null(QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("oid", "oid-1")), allowed));
        Assert.Equal(QueryBuilder.Editor.Hub.SuperAdminMatchKind.Email, QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("oid", "x"), ("upn", "a@b.COM")), allowed)!.Kind);
        Assert.Null(QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("oid", "x"), ("email", " a@b.com")), allowed));
        Assert.Null(QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("oid", "x"), ("email", "")), allowed));
        Assert.Null(QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("oid", "x"), ("groups", "G1")), allowed));
        // No stable id to attribute the session to.
        Assert.Null(QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("email", "a@b.com")), allowed));
        Assert.Equal("sub-1", QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("sub", "sub-1"), ("groups", "g1")), allowed)!.ObjectId);
    }

    [Theory]
    [InlineData("Audience")]
    [InlineData("SuperAdminSignInScheme")]
    [InlineData("SuperAdmins")]
    public void Hub_mode_refuses_to_start_without_audience_scheme_or_allowlist(string missing)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=unused;Database=unused";
            o.Hub.Enabled = true;
            o.DataSourceManagement.EncryptionKey = Key;
            o.Hub.Audience = missing == "Audience" ? " " : "https://secret-audience.test";
            o.Hub.SuperAdminSignInScheme = missing == "SuperAdminSignInScheme" ? "" : "host-scheme-secret";
            o.Hub.SuperAdmins = missing == "SuperAdmins"
                ? new SuperAdminOptions { Emails = [" "] }
                : new SuperAdminOptions { Emails = ["secret@example.com"] };
        }));

        Assert.Contains(missing, ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
    }
}
