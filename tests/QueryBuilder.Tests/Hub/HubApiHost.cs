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

        public async Task SignInAppAsync(Guid app, HubRole role, string user = "mary") =>
            Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/querybuilder/test/signin?kind=app&app={app}&role={role}&user={user}")).StatusCode);

        public async Task SignInSuperAsync(Guid? viewing = null) =>
            Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/querybuilder/test/signin?kind=super&oid=admin-oid{(viewing is { } v ? $"&viewing={v}" : "")}")).StatusCode);

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
    private readonly List<HttpClient> _clients = [];

    public Browser NewBrowser()
    {
        var client = Host.GetTestClient();
        _clients.Add(client);
        return new Browser(client);
    }

    public AppDbContext HubWideDb() => new(Options, new FixedAppContext(App.NoneId, hubWide: true));

    public static Task RunAsync(Func<HubApiHost, Task> body, bool hub = true, Action<QueryBuilderEditorOptions>? configure = null) =>
        MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
        {
            var env = new HubApiHost { Options = options };
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

            env.Host = await new HostBuilder().ConfigureWebHost(web =>
            {
                web.UseTestServer();
                // A named connection string lets a test data source query the metadata database itself (see dbo.v in the matrix test).
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:biz"] = connectionString }));
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
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
                                ? HubSession.ForSuperAdmin(q["oid"]!, "Super", "s@x", q.ContainsKey("viewing") ? Guid.Parse(q["viewing"]!) : null, now)
                                : HubSession.ForApp(Guid.Parse(q["app"]!), q["user"]!, "Name", "u@x", Enum.Parse<HubRole>(q["role"]!),
                                    QueryBuilder.Application.Abstractions.ResolvedDataScope.Unrestricted, "/back", now);
                            await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignInAsync(ctx, HubSession.Scheme, principal);
                            return Results.Ok();
                        });
                    });
                });
            }).StartAsync();

            await using (env)
            {
                await body(env);
            }
        });

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
