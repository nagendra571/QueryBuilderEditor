using System.Net;
using Xunit;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Hub;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Tests.Hub;

/// <summary>
/// The whole package (MapQueryBuilderEditor included) on a TestServer against a throwaway metadata database, with a
/// test-only GET endpoint that mints sessions. Each <see cref="Browser"/> keeps its own cookies, so one test can hold a
/// super admin and an app user at the same time.
/// </summary>
internal sealed class HubApiHost : IAsyncDisposable
{
    public static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    public sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public sealed class Browser(HttpClient client)
    {
        private readonly Dictionary<string, string> _jar = new();

        /// <summary>The same cookies, sent to a restarted host.</summary>
        public Browser MovedTo(HttpClient other)
        {
            var moved = new Browser(other);
            foreach (var pair in _jar)
            {
                moved._jar[pair.Key] = pair.Value;
            }

            return moved;
        }

        public IReadOnlyDictionary<string, string> Cookies => _jar;

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, bool csrf = true)
        {
            var request = new HttpRequestMessage(method, url);
            if (_jar.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _jar.Select(p => $"{p.Key}={p.Value}")));
            }

            if (csrf)
            {
                request.Headers.Add("X-QueryBuilder-Request", "1");
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            var response = await client.SendAsync(request);
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

        public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(HttpMethod.Get, url);
        public Task<HttpResponseMessage> PostAsync(string url, object? body) => SendAsync(HttpMethod.Post, url, body);
        public Task<HttpResponseMessage> PutAsync(string url, object? body) => SendAsync(HttpMethod.Put, url, body);

        public async Task<HttpStatusCode> StatusAsync(string url) => (await GetAsync(url)).StatusCode;

        public async Task SignInAppAsync(Guid app, HubRole role, string user = "mary", string? scope = null) =>
            Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/querybuilder/test/signin?kind=app&app={app}&role={role}&user={user}{(scope is null ? "" : "&scope=" + Uri.EscapeDataString(scope))}")).StatusCode);

        public async Task SignInSuperAsync(Guid? viewing = null) =>
            Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/querybuilder/test/signin?kind=super&oid=admin-oid{(viewing is { } v ? $"&viewing={v}" : "")}")).StatusCode);

        public async Task SignInSuperMatchedAsync(string kind, string value, string tenant = "tenant-1") =>
            Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/querybuilder/test/signin?kind=super&oid=admin-oid&mkind={kind}&mvalue={Uri.EscapeDataString(value)}&tid={tenant}")).StatusCode);

        public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }
    }

    public Guid AppA, AppB;
    public RSA RsaA = RSA.Create(2048);
    public FakeClock Clock = new(DateTimeOffset.UtcNow);
    public DbContextOptions<AppDbContext> Options = null!;
    public IHost Host = null!;
    public string ConnectionString = null!;
    private bool _hub;
    private bool _realIdentity;
    private Action<IServiceCollection>? _services;
    private readonly List<HttpClient> _clients = [];

    /// <summary>Stops the host and starts another on the same database (so the same Data Protection key ring) with other options.</summary>
    public async Task RestartAsync(Action<QueryBuilderEditorOptions>? configure)
    {
        await Host.StopAsync();
        Host.Dispose();
        Host = await StartHostAsync(configure);
    }

    public Browser NewBrowser()
    {
        var client = Host.GetTestClient();
        _clients.Add(client);
        return new Browser(client);
    }

    public AppDbContext HubWideDb() => new(Options, new FixedAppContext(App.NoneId, hubWide: true));

    public static Task RunAsync(Func<HubApiHost, Task> body, bool hub = true, Action<QueryBuilderEditorOptions>? configure = null, bool realIdentity = false, Action<IServiceCollection>? services = null) =>
        MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
        {
            var env = new HubApiHost { Options = options, ConnectionString = connectionString, _hub = hub, _realIdentity = realIdentity, _services = services };
            await using (var db = env.HubWideDb())
            {
                db.MetadataSettings.Add(new MetadataSetting { Key = "Mode", Value = hub ? "Hub" : "Embedded" });
                var a = new App { Key = "app-a", Name = "App A", ScopeKeys = ["ProgramId"] };
                var b = new App { Key = "app-b", Name = "App B" };
                env.AppA = a.Id;
                env.AppB = b.Id;
                db.Apps.AddRange(a, b);
                db.AppSigningKeys.Add(new AppSigningKey { AppId = a.Id, Kid = "rsa-a", Algorithm = "RS256", PublicKeyPem = TestTokens.Pem(env.RsaA) });
                await db.SaveChangesAsync();
            }

            env.Host = await env.StartHostAsync(configure);

            await using (env)
            {
                await body(env);
            }
        });

    private async Task<IHost> StartHostAsync(Action<QueryBuilderEditorOptions>? configure)
    {
        var env = this;
        var hub = _hub;
        var connectionString = ConnectionString;
        {
            return await new HostBuilder().ConfigureWebHost(web =>
            {
                web.UseTestServer();
                // A named connection string lets a test data source query the metadata database itself (see dbo.v in the matrix test).
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:biz"] = connectionString }));
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    env._services?.Invoke(services);
                    services.AddProblemDetails();
                    services.AddQueryBuilderEditor(o =>
                    {
                        o.ConnectionString = connectionString;
                        o.ApplyMigrations = false;
                        o.Hub.Enabled = hub;
                        o.Hub.Audience = "https://hub.test";
                        o.Hub.SuperAdminSignInScheme = "host";
                        o.Hub.SuperAdmins.ObjectIds = ["admin-oid"];
                        o.Hub.SuperAdmins.TenantIds = ["tenant-1"];
                        o.DataSourceManagement.EncryptionKey = Key;
                        configure?.Invoke(o);
                    });
                    services.AddSingleton<TimeProvider>(env.Clock);
                    // Test sources use the "biz" string, which points back at the metadata database on purpose.
                    if (!env._realIdentity)
                    {
                        services.AddSingleton<QueryBuilder.Application.Abstractions.IMetadataDatabaseIdentity>(new FakeMetadataIdentity());
                    }
                });
                web.Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        e.MapQueryBuilderEditor();
                        e.MapGet("/querybuilder/test/challenge", () => Results.Challenge(authenticationSchemes: [HubSession.Scheme]));
                        e.MapGet("/querybuilder/test/signin", async (HttpContext ctx, TimeProvider clock) =>
                        {
                            var q = ctx.Request.Query;
                            var now = clock.GetUtcNow();
                            var principal = q["kind"] == "super"
                                ? HubSession.ForSuperAdmin(q["oid"]!, "Super", "s@x", q.ContainsKey("viewing") ? Guid.Parse(q["viewing"]!) : null, now, Enum.Parse<SuperAdminMatchKind>(q["mkind"].FirstOrDefault() ?? "ObjectId"), q["mvalue"].FirstOrDefault() ?? q["oid"]!, q["tid"].FirstOrDefault() ?? "tenant-1", q["provider"].FirstOrDefault() ?? "Entra")
                                : HubSession.ForApp(Guid.Parse(q["app"]!), q["user"]!, "Name", "u@x", Enum.Parse<HubRole>(q["role"]!),
                                    q.ContainsKey("scope") ? ScopeOf(q["scope"]!) : QueryBuilder.Application.Abstractions.ResolvedDataScope.Unrestricted, "/back", now);
                            await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignInAsync(ctx, HubSession.Scheme, principal);
                            return Results.Ok();
                        });
                    });
                });
            }).StartAsync();
        }
    }

    // "Key=v1,v2;Other=x"
    private static QueryBuilder.Application.Abstractions.ResolvedDataScope ScopeOf(string text) =>
        new(false, text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(p => p[0], p => (IReadOnlyList<string>)p[1].Split(','), StringComparer.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await Host.StopAsync();
        Host.Dispose();
        RsaA.Dispose();
    }
}
