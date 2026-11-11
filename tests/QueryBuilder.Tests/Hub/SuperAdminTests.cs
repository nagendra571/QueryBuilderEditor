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
using QueryBuilder.Editor.Hub;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class SuperAdminTests
{
    private const string Audience = "https://hub.test";
    private const string AdminOid = "11111111-aaaa-bbbb-cccc-000000000001";
    private const string TenantId = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string OtherTenantId = "bbbbbbbb-1111-2222-3333-444444444444";
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
        private string _scheme = "";

        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context)
        {
            _context = context;
            _scheme = scheme.Name;
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

            // What RemoteAuthenticationHandler does: the ticket carries the challenge's own properties and the scheme that made it.
            var ticketProperties = new AuthenticationProperties(new Dictionary<string, string?>(properties!.Items)) { RedirectUri = properties.RedirectUri };
            ticketProperties.Items[".AuthScheme"] = _scheme;
            await _context.SignInAsync("QueryBuilder.HubExternal", new ClaimsPrincipal(new ClaimsIdentity(claims, "host")), ticketProperties);
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
        public Task<HttpResponseMessage> SignInAsync(params (string Type, string Value)[] claims) =>
            SignInRawAsync(claims.Any(c => c.Type == "tid") ? claims : [.. claims, ("tid", TenantId)]);

        public async Task<HttpResponseMessage> SignInRawAsync(params (string Type, string Value)[] claims)
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
                        o.Hub.SuperAdmins.TenantIds = [TenantId];
                        o.Hub.SuperAdmins.ObjectIds = [AdminOid];
                        o.Hub.SuperAdmins.Emails = ["Boss@Example.com"];
                        o.Hub.SuperAdmins.GroupIds = ["group-1"];
                        o.DataSourceManagement.EncryptionKey = Key;
                    });
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        e.MapQueryBuilderEditor();
                        // Another handler of the host leaving a ticket in the external cookie: no marker, or another scheme's stamp.
                        e.MapGet("/test/ext", async ctx =>
                        {
                            var items = new Dictionary<string, string?>();
                            if (ctx.Request.Query["marker"] == "1") items["qb:superadmin"] = "1";
                            if (ctx.Request.Query.ContainsKey("stamp")) items[".AuthScheme"] = ctx.Request.Query["stamp"];
                            var claims = new[] { new Claim("oid", AdminOid), new Claim("tid", TenantId), new Claim("email", "secret.person@example.com"), new Claim("name", "Secret Person") };
                            await ctx.SignInAsync("QueryBuilder.HubExternal", new ClaimsPrincipal(new ClaimsIdentity(claims, "x")), new AuthenticationProperties(items));
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

    [MetadataSqlFact]
    public Task A_ticket_not_started_by_signin_or_stamped_by_another_scheme_is_refused_and_audited() => RunAsync(async env =>
    {
        foreach (var query in new[] { "?stamp=host", "?marker=1&stamp=other", "?marker=1" })
        {
            env.Jar.Clear();
            Assert.Equal(HttpStatusCode.OK, (await env.GetAsync("/test/ext" + query)).StatusCode);
            var response = await env.GetAsync("/querybuilder/signin/complete");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.False(env.Jar.ContainsKey("__Host-qb-hub"));
            Assert.False(env.Jar.ContainsKey("__Host-qb-hub-ext"));
        }

        await using var db = env.Db();
        var audits = await db.AuditLogEntries.Where(a => a.Action == AuditAction.SuperAdminSignInRefused).ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.All(audits, a =>
        {
            Assert.Null(a.AppId);
            Assert.Equal("anonymous", a.Actor);
            Assert.Contains("WrongScheme", a.DetailsJson);
            Assert.DoesNotContain("secret", a.DetailsJson + a.Summary, StringComparison.OrdinalIgnoreCase);
        });
        Assert.False(await db.AuditLogEntries.AnyAsync(a => a.Action == AuditAction.SuperAdminSignedIn));
    });

    [MetadataSqlFact]
    public Task Only_an_allowed_tenant_can_match_even_with_an_allowlisted_email() => RunAsync(async env =>
    {
        var wrong = await env.SignInRawAsync(("oid", "55555555-0000-0000-0000-000000000005"), ("email", "boss@example.com"), ("tid", OtherTenantId));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        var missing = await env.SignInRawAsync(("oid", "55555555-0000-0000-0000-000000000005"), ("email", "boss@example.com"));
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        var two = await env.SignInRawAsync(("oid", "55555555-0000-0000-0000-000000000005"), ("email", "boss@example.com"), ("tid", TenantId), ("tid", OtherTenantId));
        Assert.Equal(HttpStatusCode.Forbidden, two.StatusCode);
        Assert.False(env.Jar.ContainsKey("__Host-qb-hub"));

        await using (var db = env.Db())
        {
            var refused = await db.AuditLogEntries.Where(a => a.Action == AuditAction.SuperAdminSignInRefused).ToListAsync();
            Assert.Equal(3, refused.Count);
            Assert.All(refused, a => Assert.Contains("WrongTenant", a.DetailsJson));
            Assert.Contains(refused, a => a.DetailsJson!.Contains(OtherTenantId));
            Assert.DoesNotContain(refused, a => a.DetailsJson!.Contains("example.com"));
        }

        var ok = await env.SignInAsync(("oid", "55555555-0000-0000-0000-000000000005"), ("email", "boss@example.com"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
    });

    [MetadataSqlFact]
    public Task Refusing_a_real_sign_in_ends_a_session_but_a_bare_visit_does_not() => RunAsync(async env =>
    {
        await env.SignInAsync(("oid", AdminOid));
        Assert.Equal($"super:{AdminOid}", await env.MeUserAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await env.GetAsync("/querybuilder/signin/complete")).StatusCode);
        Assert.Equal($"super:{AdminOid}", await env.MeUserAsync());
        await using (var db = env.Db())
        {
            Assert.False(await db.AuditLogEntries.AnyAsync(a => a.Action == AuditAction.SuperAdminSignInRefused));
        }

        await env.SignInAsync(("oid", "66666666-0000-0000-0000-000000000006"));
        Assert.Equal("anonymous", await env.MeUserAsync());
        await using (var db = env.Db())
        {
            var audit = await db.AuditLogEntries.SingleAsync(a => a.Action == AuditAction.SuperAdminSignInRefused);
            Assert.Contains("NotAllowlisted", audit.DetailsJson);
        }
    });

    [MetadataSqlFact]
    public Task Object_ids_compare_as_guids_and_conflicting_ids_are_refused() => RunAsync(async env =>
    {
        var upper = await env.SignInAsync(("oid", AdminOid.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Redirect, upper.StatusCode);

        env.Jar.Clear();
        var two = await env.SignInAsync(("oid", AdminOid), ("http://schemas.microsoft.com/identity/claims/objectidentifier", "77777777-0000-0000-0000-000000000007"));
        Assert.Equal(HttpStatusCode.Forbidden, two.StatusCode);
        await using (var db = env.Db())
        {
            Assert.Contains("NoStableId", (await db.AuditLogEntries.SingleAsync(a => a.Action == AuditAction.SuperAdminSignInRefused)).DetailsJson);
        }

        // The same id in both claim forms is one id.
        env.Jar.Clear();
        var same = await env.SignInAsync(("oid", AdminOid), ("http://schemas.microsoft.com/identity/claims/objectidentifier", AdminOid.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Redirect, same.StatusCode);
    });

    [MetadataSqlFact]
    public Task View_app_refuses_the_default_and_none_apps() => RunAsync(async env =>
    {
        await env.SignInAsync(("oid", AdminOid));
        Assert.Equal(HttpStatusCode.NotFound, (await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = App.DefaultId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.PostAsync("/querybuilder/api/hub/view-app", new { appId = App.NoneId })).StatusCode);
        Assert.Equal(0, await env.DataSourceCountAsync());
    });

    [Fact]
    public void Recognizer_requires_the_tenant_then_matches_exactly_and_ignores_blank_entries()
    {
        var oid1 = "11111111-1111-1111-1111-111111111111";
        var allowed = new SuperAdminOptions
        {
            TenantIds = [TenantId, " "], ObjectIds = [oid1, " "], Emails = ["A@B.com", ""], GroupIds = ["{22222222-2222-2222-2222-222222222222}", "plain-group"],
        };
        static ClaimsPrincipal P(params (string, string)[] c) => new(new ClaimsIdentity(c.Select(x => new Claim(x.Item1, x.Item2)), "host"));
        static SuperAdminRecognition R(SuperAdminOptions o, params (string, string)[] c) =>
            QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P([.. c, ("tid", TenantId.ToUpperInvariant())]), o);

        Assert.Equal(QueryBuilder.Editor.Hub.SuperAdminMatchKind.ObjectId, R(allowed, ("oid", oid1.ToUpperInvariant())).Match!.Kind);
        Assert.Equal(QueryBuilder.Editor.Hub.SuperAdminMatchKind.Email, R(allowed, ("oid", "x"), ("upn", "a@b.COM")).Match!.Kind);
        Assert.Equal(QueryBuilder.Editor.Hub.SuperAdminMatchKind.Group, R(allowed, ("oid", "x"), ("groups", "22222222-2222-2222-2222-222222222222")).Match!.Kind);
        Assert.Equal(QueryBuilder.Editor.Hub.SuperAdminMatchKind.Group, R(allowed, ("oid", "x"), ("groups", "plain-group")).Match!.Kind);
        Assert.Null(R(allowed, ("oid", "x"), ("groups", "PLAIN-GROUP")).Match);
        Assert.Null(R(allowed, ("oid", "x"), ("email", " a@b.com")).Match);
        Assert.Null(R(allowed, ("oid", "x"), ("email", "")).Match);
        Assert.Equal(SuperAdminRefusalReason.NotAllowlisted, R(allowed, ("oid", "x"), ("groups", "g")).Refusal);
        Assert.Equal(SuperAdminRefusalReason.GroupsOverage, R(allowed, ("oid", "x"), ("_claim_names", "{\"groups\":\"s\"}")).Refusal);
        // No stable id to attribute the session to.
        Assert.Equal(SuperAdminRefusalReason.NoStableId, R(allowed, ("email", "a@b.com")).Refusal);
        Assert.Equal("sub-1", R(allowed, ("sub", "sub-1"), ("groups", "plain-group")).Match!.ObjectId);
        // Tenant first: an allowlisted object id from another tenant, or with none, never matches.
        var other = QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("oid", oid1), ("tid", OtherTenantId)), allowed);
        Assert.Equal(SuperAdminRefusalReason.WrongTenant, other.Refusal);
        Assert.Equal(OtherTenantId, other.TenantId);
        Assert.Equal(SuperAdminRefusalReason.WrongTenant, QueryBuilder.Editor.Hub.SuperAdminRecognizer.Recognize(P(("oid", oid1)), allowed).Refusal);
    }

    [Theory]
    [InlineData("Audience")]
    [InlineData("SuperAdminSignInScheme")]
    [InlineData("SuperAdmins")]
    [InlineData("TenantIds")]
    public void Hub_mode_refuses_to_start_without_audience_scheme_allowlist_or_tenant(string missing)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=unused;Database=unused";
            o.Hub.Enabled = true;
            o.DataSourceManagement.EncryptionKey = Key;
            o.Hub.Audience = missing == "Audience" ? " " : "https://secret-audience.test";
            o.Hub.SuperAdminSignInScheme = missing == "SuperAdminSignInScheme" ? "" : "host-scheme-secret";
            o.Hub.SuperAdmins = new SuperAdminOptions
            {
                Emails = missing == "SuperAdmins" ? [" "] : ["secret@example.com"],
                TenantIds = missing == "TenantIds" ? [" "] : ["tenant-secret"],
            };
        }));

        Assert.Contains(missing, ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
    }
}
