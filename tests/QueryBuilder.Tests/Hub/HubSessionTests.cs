using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Hub;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class HubSessionTests
{
    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A hub-mode host on a TestServer. The sign-in and inspection endpoints belong to the test, not the package.</summary>
    private sealed class TestHub : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly HttpClient _client;
        private readonly Dictionary<string, string> _jar = new();

        public FakeClock Clock { get; }

        private TestHub(IHost host, FakeClock clock)
        {
            _host = host;
            Clock = clock;
            _client = host.GetTestClient();
        }

        public static async Task<TestHub> StartAsync(string connectionString, FakeClock clock, bool hub = true)
        {
            var host = await new HostBuilder().ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddQueryBuilderEditor(o =>
                    {
                        o.ConnectionString = connectionString;
                        o.ApplyMigrations = false;
                        o.Hub.Enabled = hub;
                        o.Hub.Audience = "https://hub.test";
                    });
                    services.AddSingleton<TimeProvider>(clock);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(MapTestEndpoints);
                });
            }).StartAsync();
            return new TestHub(host, clock);
        }

        private static void MapTestEndpoints(IEndpointRouteBuilder e)
        {
            e.MapPost("/querybuilder/test/signin", async (HttpContext ctx, TimeProvider clock) =>
            {
                var q = ctx.Request.Query;
                var now = clock.GetUtcNow();
                var principal = q["kind"] == "super"
                    ? HubSession.ForSuperAdmin(q["oid"]!, "Super", "s@x", q.ContainsKey("viewing") ? Guid.Parse(q["viewing"]!) : null, now)
                    : q.ContainsKey("raw")
                        ? new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                            [new System.Security.Claims.Claim(HubSession.Kind, HubSession.AppKind), new System.Security.Claims.Claim(HubSession.App, q["app"]!)],
                            HubSession.Scheme))
                        : HubSession.ForApp(Guid.Parse(q["app"]!), q["user"]!, "Name", "u@x", Enum.Parse<HubRole>(q["role"].FirstOrDefault() ?? "Viewer"),
                            q["scope"] == "*" ? ResolvedDataScope.Unrestricted : ParseScope(q["scope"].FirstOrDefault() ?? "{}"), "/back", now);
                await ctx.SignInAsync(HubSession.Scheme, principal);
                return Results.Ok();
            });

            e.MapGet("/querybuilder/test/ctx", async (HttpContext ctx, IAppContext app, ICurrentUserService user, ICurrentDataScope scope, AppDbContext db) =>
            {
                var first = app.AppId;
                if (ctx.Request.Query["swap"].FirstOrDefault() is { } swap)
                {
                    // A cookie changing mid-request must not move the scope to another app.
                    ctx.Items[HubSessionStartupFilter.PrincipalItemKey] =
                        HubSession.ForApp(Guid.Parse(swap), "x", null, null, HubRole.Viewer, ResolvedDataScope.Unrestricted, null, DateTimeOffset.UtcNow);
                }

                var resolved = await scope.GetAsync(CancellationToken.None);
                return Results.Json(new
                {
                    appId = app.AppId,
                    firstAppId = first,
                    isHubWide = app.IsHubWide,
                    userId = user.UserId,
                    displayName = user.DisplayName,
                    email = user.Email,
                    role = user.Role?.ToString(),
                    isSuperAdmin = user.IsSuperAdmin,
                    dataSources = await db.DataSources.CountAsync(),
                    queries = await db.SavedQueries.CountAsync(),
                    scopeEnabled = scope.IsEnabled,
                    hostKeys = scope.HostKeys,
                    unrestricted = resolved.IsUnrestricted,
                    scopeValues = resolved.Values.ToDictionary(p => p.Key, p => p.Value),
                });
            });
        }

        private static ResolvedDataScope ParseScope(string json) => new(false,
            JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)!
                .ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value));

        public async Task<HttpResponseMessage> SignInAsync(string query)
        {
            var response = await SendAsync(HttpMethod.Post, "/querybuilder/test/signin?" + query);
            response.EnsureSuccessStatusCode();
            return response;
        }

        public async Task<JsonElement> CtxAsync(string query = "")
        {
            var response = await SendAsync(HttpMethod.Get, "/querybuilder/test/ctx" + (query.Length > 0 ? "?" + query : ""));
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        public void SetCookie(string name, string value) => _jar[name] = value;

        public string CookieHeader => string.Join("; ", _jar.Select(p => $"{p.Key}={p.Value}"));

        public string? CookieValue(string name) => _jar.GetValueOrDefault(name);

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            if (_jar.Count > 0)
            {
                request.Headers.Add("Cookie", CookieHeader);
            }

            var response = await _client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var pair = cookie.Split(';')[0];
                    var eq = pair.IndexOf('=');
                    var name = pair[..eq];
                    var value = pair[(eq + 1)..];
                    var expired = cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
                    if (value.Length == 0 || expired)
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

        public T Service<T>() where T : notnull => _host.Services.GetRequiredService<T>();

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private static readonly DateTimeOffset T0 = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed record Seeded(Guid AppA, Guid AppB);

    private static Task RunAsync(Func<string, Seeded, Task> body, Action<AppDbContext, Guid, Guid>? seed = null) =>
        MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
        {
            var seeded = new Seeded(Guid.NewGuid(), Guid.NewGuid());
            await using (var db = new AppDbContext(options, new FixedAppContext(App.NoneId, hubWide: true)))
            {
                // As if a hub had already started on this database: the guard refuses a hub on data with no recorded mode.
                db.MetadataSettings.Add(new MetadataSetting { Key = "Mode", Value = "Hub" });
                db.Apps.Add(new App { Id = seeded.AppA, Key = "a", Name = "A", ScopeKeys = ["k"] });
                db.Apps.Add(new App { Id = seeded.AppB, Key = "b", Name = "B" });
                await db.SaveChangesAsync();
            }

            foreach (var app in new[] { seeded.AppA, seeded.AppB })
            {
                await using var db = new AppDbContext(options, new FixedAppContext(app));
                var source = new DataSource { Id = Guid.NewGuid(), Name = "source" };
                db.DataSources.Add(source);
                await db.SaveChangesAsync();
                db.SavedQueries.Add(new SavedQuery { Id = Guid.NewGuid(), Name = "q", DataSourceId = source.Id, OwnerId = "u", DefinitionJson = "{}" });
                await db.SaveChangesAsync();
            }

            await body(connectionString, seeded);
        });

    [MetadataSqlFact]
    public Task No_session_resolves_to_none_and_lists_are_empty() => RunAsync(async (cs, _) =>
    {
        await using var hub = await TestHub.StartAsync(cs, new FakeClock(T0));

        var none = await hub.CtxAsync();
        Assert.Equal(Guid.Empty, none.GetProperty("appId").GetGuid());
        Assert.Equal(0, none.GetProperty("dataSources").GetInt32());
        Assert.Equal(0, none.GetProperty("queries").GetInt32());
        Assert.Equal("anonymous", none.GetProperty("userId").GetString());
        Assert.Equal(JsonValueKind.Null, none.GetProperty("role").ValueKind);
        Assert.False(none.GetProperty("unrestricted").GetBoolean());
        Assert.Empty(none.GetProperty("scopeValues").EnumerateObject());

        // A made-up cookie is no session either.
        hub.SetCookie("__Host-qb-hub", "not-a-real-ticket");
        var forged = await hub.CtxAsync();
        Assert.Equal(Guid.Empty, forged.GetProperty("appId").GetGuid());
        Assert.Equal(0, forged.GetProperty("queries").GetInt32());
    });

    [MetadataSqlFact]
    public Task App_session_resolves_to_its_app_and_frozen_per_request() => RunAsync(async (cs, seeded) =>
    {
        await using var hub = await TestHub.StartAsync(cs, new FakeClock(T0));
        await hub.SignInAsync($"app={seeded.AppA}&user=mary&role=Author&scope=*");

        var ctx = await hub.CtxAsync();
        Assert.Equal(seeded.AppA, ctx.GetProperty("appId").GetGuid());
        Assert.False(ctx.GetProperty("isHubWide").GetBoolean());
        Assert.Equal("mary", ctx.GetProperty("userId").GetString());
        Assert.Equal("Name", ctx.GetProperty("displayName").GetString());
        Assert.Equal("u@x", ctx.GetProperty("email").GetString());
        Assert.Equal("Author", ctx.GetProperty("role").GetString());
        Assert.False(ctx.GetProperty("isSuperAdmin").GetBoolean());
        Assert.Equal(1, ctx.GetProperty("dataSources").GetInt32());

        var swapped = await hub.CtxAsync($"swap={seeded.AppB}");
        Assert.Equal(seeded.AppA, swapped.GetProperty("firstAppId").GetGuid());
        Assert.Equal(seeded.AppA, swapped.GetProperty("appId").GetGuid());
        Assert.Equal(1, swapped.GetProperty("queries").GetInt32());
    });

    [MetadataSqlFact]
    public Task Super_admin_without_viewing_app_resolves_to_none() => RunAsync(async (cs, seeded) =>
    {
        await using var hub = await TestHub.StartAsync(cs, new FakeClock(T0));
        await hub.SignInAsync("kind=super&oid=oid-1");

        var ctx = await hub.CtxAsync();
        Assert.Equal(Guid.Empty, ctx.GetProperty("appId").GetGuid());
        Assert.Equal(0, ctx.GetProperty("dataSources").GetInt32());
        Assert.True(ctx.GetProperty("isSuperAdmin").GetBoolean());
        Assert.Equal("super:oid-1", ctx.GetProperty("userId").GetString());
        Assert.True(ctx.GetProperty("unrestricted").GetBoolean());

        await hub.SignInAsync($"kind=super&oid=oid-1&viewing={seeded.AppB}");
        var viewing = await hub.CtxAsync();
        Assert.Equal(seeded.AppB, viewing.GetProperty("appId").GetGuid());
        Assert.Equal(1, viewing.GetProperty("dataSources").GetInt32());
    });

    [MetadataSqlFact]
    public Task Session_older_than_8h_is_rejected() => RunAsync(async (cs, seeded) =>
    {
        var clock = new FakeClock(T0);
        await using var hub = await TestHub.StartAsync(cs, clock);
        await hub.SignInAsync($"app={seeded.AppA}&user=mary");

        // Active every 30 minutes, so idle never fires; only the cap can end it.
        for (var i = 0; i < 16; i++)
        {
            clock.Now += TimeSpan.FromMinutes(30);
            Assert.Equal(seeded.AppA, (await hub.CtxAsync()).GetProperty("appId").GetGuid());
        }

        Assert.Equal(T0.AddHours(8), clock.Now);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(Guid.Empty, (await hub.CtxAsync()).GetProperty("appId").GetGuid());
        Assert.Null(hub.CookieValue("__Host-qb-hub"));
    });

    [MetadataSqlFact]
    public Task Idle_over_1h_is_rejected_and_activity_extends_it() => RunAsync(async (cs, seeded) =>
    {
        var clock = new FakeClock(T0);
        await using var hub = await TestHub.StartAsync(cs, clock);
        await hub.SignInAsync($"app={seeded.AppA}&user=mary");

        clock.Now += TimeSpan.FromMinutes(40);
        Assert.Equal(seeded.AppA, (await hub.CtxAsync()).GetProperty("appId").GetGuid());
        // 80 minutes after sign-in but only 40 since the last request: the idle window slid.
        clock.Now += TimeSpan.FromMinutes(40);
        Assert.Equal(seeded.AppA, (await hub.CtxAsync()).GetProperty("appId").GetGuid());

        clock.Now += TimeSpan.FromMinutes(61);
        Assert.Equal(Guid.Empty, (await hub.CtxAsync()).GetProperty("appId").GetGuid());
    });

    [MetadataSqlFact]
    public Task Disabling_the_app_ends_its_sessions_within_30s() => RunAsync(async (cs, seeded) =>
    {
        var clock = new FakeClock(T0);
        await using var hub = await TestHub.StartAsync(cs, clock);
        await hub.SignInAsync($"app={seeded.AppA}&user=mary");
        Assert.Equal(seeded.AppA, (await hub.CtxAsync()).GetProperty("appId").GetGuid());

        await using (var scope = hub.Service<IHubWideScopeFactory>().Create())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Apps.Where(a => a.Id == seeded.AppA).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsEnabled, false));
        }

        clock.Now += TimeSpan.FromSeconds(10);
        Assert.Equal(seeded.AppA, (await hub.CtxAsync()).GetProperty("appId").GetGuid());

        clock.Now += TimeSpan.FromSeconds(21);
        var after = await hub.CtxAsync();
        Assert.Equal(Guid.Empty, after.GetProperty("appId").GetGuid());
        Assert.Equal(0, after.GetProperty("queries").GetInt32());
        Assert.Null(hub.CookieValue("__Host-qb-hub"));
    });

    [MetadataSqlFact]
    public Task A_session_for_an_app_that_does_not_exist_is_rejected() => RunAsync(async (cs, _) =>
    {
        await using var hub = await TestHub.StartAsync(cs, new FakeClock(T0));
        await hub.SignInAsync($"app={Guid.NewGuid()}&user=mary");
        Assert.Equal(Guid.Empty, (await hub.CtxAsync()).GetProperty("appId").GetGuid());
    });

    [MetadataSqlFact]
    public Task A_session_missing_required_claims_is_rejected() => RunAsync(async (cs, seeded) =>
    {
        await using var hub = await TestHub.StartAsync(cs, new FakeClock(T0));
        await hub.SignInAsync($"raw=1&app={seeded.AppA}");
        var ctx = await hub.CtxAsync();
        Assert.Equal(Guid.Empty, ctx.GetProperty("appId").GetGuid());
        Assert.Equal("anonymous", ctx.GetProperty("userId").GetString());
    });

    [MetadataSqlFact]
    public Task Session_scope_reaches_the_data_scope_service() => RunAsync(async (cs, seeded) =>
    {
        await using var hub = await TestHub.StartAsync(cs, new FakeClock(T0));
        await hub.SignInAsync($"app={seeded.AppA}&user=mary&scope={Uri.EscapeDataString("{\"k\":[\"1\",\"2\"]}")}");

        var ctx = await hub.CtxAsync();
        Assert.True(ctx.GetProperty("scopeEnabled").GetBoolean());
        Assert.False(ctx.GetProperty("unrestricted").GetBoolean());
        Assert.Equal(["1", "2"], ctx.GetProperty("scopeValues").GetProperty("k").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(["k"], ctx.GetProperty("hostKeys").EnumerateArray().Select(v => v.GetString()));

        await hub.SignInAsync($"app={seeded.AppA}&user=mary&scope=*");
        Assert.True((await hub.CtxAsync()).GetProperty("unrestricted").GetBoolean());

        // App B declares no keys.
        await hub.SignInAsync($"app={seeded.AppB}&user=bob&scope=*");
        Assert.Empty((await hub.CtxAsync()).GetProperty("hostKeys").EnumerateArray());
    });

    [MetadataSqlFact]
    public Task Data_protection_keys_survive_a_new_host_instance() => RunAsync(async (cs, seeded) =>
    {
        string cookie;
        await using (var first = await TestHub.StartAsync(cs, new FakeClock(T0)))
        {
            await first.SignInAsync($"app={seeded.AppA}&user=mary&role=Admin");
            cookie = first.CookieValue("__Host-qb-hub")!;
            Assert.NotNull(cookie);
        }

        await using var second = await TestHub.StartAsync(cs, new FakeClock(T0));
        second.SetCookie("__Host-qb-hub", cookie);
        var ctx = await second.CtxAsync();
        Assert.Equal(seeded.AppA, ctx.GetProperty("appId").GetGuid());
        Assert.Equal("mary", ctx.GetProperty("userId").GetString());

        // Both hosts used one key ring, not one each.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(cs, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)).Options;
        await using var db = new AppDbContext(options, new FixedAppContext(App.NoneId));
        Assert.Equal(1, await db.DataProtectionKeys.CountAsync(k => k.Xml.Contains("<key ")));
    });

    [MetadataSqlFact]
    public Task The_key_repository_only_ever_inserts() => RunAsync(async (cs, _) =>
    {
        await using var hub = await TestHub.StartAsync(cs, new FakeClock(T0));
        var repository = hub.Service<MetadataXmlRepository>();
        var before = repository.GetAllElements().Count;

        repository.StoreElement(new XElement("key", new XAttribute("id", "1")), "key-1");
        repository.StoreElement(new XElement("key", new XAttribute("id", "2")), "key-2");

        var all = repository.GetAllElements();
        Assert.Equal(before + 2, all.Count);
        Assert.Contains(all, e => (string?)e.Attribute("id") == "1");
        Assert.Contains(all, e => (string?)e.Attribute("id") == "2");
    });

    private sealed class FlippingSource(params Guid[] apps) : IAppSessionSource
    {
        private int _reads;
        public int Reads => _reads;

        public HubSessionInfo? Current => new(false, apps[Math.Min(_reads++, apps.Length - 1)], "u", "u", null, HubRole.Viewer, ResolvedDataScope.Unrestricted, null);
    }

    [Fact]
    public void The_accessor_reads_the_session_once_and_refuses_a_later_choice_of_another_app()
    {
        var a = Guid.NewGuid();
        var source = new FlippingSource(a, Guid.NewGuid());
        var accessor = new AppContextAccessor(new MetadataMode(true), source);

        Assert.Equal(a, accessor.AppId);
        Assert.Equal(a, accessor.AppId);
        Assert.Equal(1, source.Reads);
        Assert.Throws<InvalidOperationException>(() => accessor.UseApp(Guid.NewGuid()));
    }

    [Fact]
    public void Embedded_accessor_never_consults_the_session()
    {
        var source = new FlippingSource(Guid.NewGuid());
        var accessor = new AppContextAccessor(new MetadataMode(false), source);

        Assert.Equal(App.DefaultId, accessor.AppId);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void Embedded_mode_registers_no_hub_scheme()
    {
        var services = new ServiceCollection();
        services.AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=unused;Database=unused";
            o.ApplyMigrations = false;
        });

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAuthenticationService) || d.ServiceType == typeof(IAuthenticationSchemeProvider));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAppSessionSource));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(MetadataXmlRepository));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(HubSessionStartupFilter));
        Assert.DoesNotContain(services, d => d.ServiceType.Name.Contains("DataProtection", StringComparison.Ordinal));
    }
}
