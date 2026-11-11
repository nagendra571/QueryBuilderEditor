using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Middleware;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Security;
using Xunit;

namespace QueryBuilder.Tests.Hub;

/// <summary>
/// Calls every /querybuilder/api endpoint as App B (and as a super admin viewing App B) with App A's ids, and proves
/// nothing of App A was revealed or changed. The endpoint list is read from the running host, so an endpoint added
/// without a row in <see cref="Bodies"/>, <see cref="NoIdCalls"/> or <see cref="Excluded"/> fails the test. Every
/// guid-routed call is also sent with App B's own ids (a positive control), so a 404 can only mean "not yours".
/// </summary>
[Collection(MetadataDatabaseCollection.Name)]
public partial class CrossAppEndpointMatrixTests
{
    private const string Mount = "/querybuilder";

    private static readonly byte[] OldKey = Enumerable.Repeat((byte)0x63, 32).ToArray();

    private sealed record Ids(
        Guid DataSource, Guid Query, Guid Visualization, Guid Dashboard, Guid Tile, Guid QueryShare, Guid DashboardShare, Guid Rule, Guid Managed)
    {
        public IEnumerable<Guid> All => [DataSource, Query, Visualization, Dashboard, Tile, QueryShare, DashboardShare, Rule, Managed];
    }

    // Ok: 2xx. NotFound: 404. Exact: the listed status, with a reason. Refused / BadRequest: 403 / 400 AND the same status and
    // body as the twin call that names a missing id, so existence is not revealed. Same: any status below 500, identical to the
    // twin. Positive: the control with B's own ids, anything but 404 or 5xx.
    private enum Expect { Ok, NotFound, Exact, Refused, BadRequest, Same, Positive }

    private sealed record Call(string Label, HttpMethod Method, string Url, object? Body, Expect Expect, Call? Twin = null, int Status = 0, string? Why = null);

    // Exact "METHOD pattern" keys, never a prefix, so a future app-scoped route under /api/hub is not silently skipped.
    private static readonly Dictionary<string, string> Excluded = new()
    {
        ["GET /api/hub/apps"] = "super-admin-only registry of apps, not app-scoped; gate covered by HubAuthorizationTests / HubRegistryTests",
        ["POST /api/hub/apps"] = "super-admin-only registry of apps, not app-scoped; covered by HubRegistryTests",
        ["PUT /api/hub/apps/{id}"] = "super-admin-only registry of apps, not app-scoped; covered by HubRegistryTests",
        ["POST /api/hub/apps/{id}/keys"] = "super-admin-only registry of apps, not app-scoped; covered by HubRegistryTests",
        ["DELETE /api/hub/apps/{id}/keys/{kid}"] = "super-admin-only registry of apps, not app-scoped; covered by HubRegistryTests",
        ["PUT /api/hub/apps/{id}/return-urls"] = "super-admin-only registry of apps, not app-scoped; covered by HubRegistryTests",
        ["POST /api/hub/view-app"] = "super-admin-only choice of the app to view; covered by SuperAdminTests"
    };

    // Guid-routed endpoints: key is "METHOD pattern" with the ":guid" constraint dropped; value builds a valid JSON body.
    private static readonly Dictionary<string, Func<Ids, object?>> Bodies = new()
    {
        ["PUT /api/admin/data-sources/{id}"] = _ => new { name = "renamed", description = "x" },
        ["PUT /api/admin/data-sources/{id}/active"] = _ => new { isActive = false },
        ["POST /api/admin/data-sources/{id}/test"] = _ => null,
        ["GET /api/admin/data-sources/{id}"] = _ => null,
        ["PUT /api/admin/data-sources/{id}/catalog-policy"] = _ => new { catalogScope = "TablesAndViews", allowedObjects = new[] { "dbo.v" } },
        ["GET /api/admin/data-sources/{id}/data-scope"] = _ => null,
        ["PUT /api/admin/data-sources/{id}/data-scope"] = _ => new { objects = Array.Empty<object>() },
        ["PUT /api/admin/data-sources/{id}/scope-keys"] = _ => new { keys = new[] { "ProgramId" } },
        ["PUT /api/admin/data-sources/{id}/record-limit"] = _ => new { maxRecords = 5 },
        ["GET /api/dashboards/{id}"] = _ => null,
        ["PUT /api/dashboards/{id}"] = _ => new { name = "renamed", description = "x", version = 1 },
        ["PUT /api/dashboards/{id}/tiles"] = _ => new { version = 1, tiles = Array.Empty<object>() },
        ["POST /api/dashboards/{id}/tiles/chart"] = a => new { visualizationId = a.Visualization },
        ["DELETE /api/dashboards/{id}"] = _ => null,
        ["GET /api/dashboards/{id}/shares"] = _ => null,
        ["POST /api/dashboards/{id}/shares"] = a => new { sharedWithUserId = "x", sharedWithUserEmail = "x@x", accessLevel = "Viewer", alsoShareQueryIds = new[] { a.Query } },
        ["DELETE /api/dashboards/{id}/shares/{shareId}"] = _ => null,
        ["GET /api/dashboards/{id}/share-preview"] = _ => null,
        ["GET /api/data-sources/{id}/catalog"] = _ => null,
        ["GET /api/data-sources/{id}/app-users"] = _ => null,
        ["GET /api/queries/{id}"] = _ => null,
        ["DELETE /api/queries/{id}"] = _ => null,
        ["POST /api/queries/{id}/favorite"] = _ => null,
        ["POST /api/queries/{id}/disable"] = _ => null,
        ["GET /api/queries/{id}/parameters/{name}/options"] = _ => null,
        ["GET /api/queries/{id}/shares"] = _ => null,
        ["POST /api/queries/{id}/shares"] = _ => new { sharedWithUserId = "x", sharedWithUserEmail = "x@x", accessLevel = "Viewer" },
        ["DELETE /api/queries/{id}/shares/{shareId}"] = _ => null,
        ["GET /api/queries/{queryId}/visualizations"] = _ => null,
        ["POST /api/queries/{queryId}/visualizations"] = _ => new { name = "v", type = "Table", options = new { table = new { columns = new[] { "c" } } } },
        ["PUT /api/queries/{queryId}/visualizations/{visualizationId}"] = _ => new { name = "v", type = "Table", options = new { table = new { columns = new[] { "c" } } } },
        ["DELETE /api/queries/{queryId}/visualizations/{visualizationId}"] = _ => null,
        ["POST /api/queries/{queryId}/visualizations/{visualizationId}/data"] = _ => new { parameterValues = new { } }
    };

    // Endpoints that call a business database nobody can reach in this test: B's own control call may fail with a server error.
    private static readonly Dictionary<string, string> PositiveMayFailServerSide = new();

    private static object Definition(Guid? optionsQueryId = null) => new
    {
        source = new { schemaName = "dbo", objectName = "v", kind = "View", alias = "t" },
        columns = new[] { new { tableAlias = "t", columnName = "c" } },
        parameters = optionsQueryId is { } id
            ? new[] { new { name = "p", label = "p", dataType = "Text", type = "QueryList", isRequired = false, optionsQueryId = (Guid?)id, optionsLabelColumn = "c", optionsValueColumn = "c" } }
            : Array.Empty<object>()
    };

    private static string DefinitionJson(Guid optionsQueryId) =>
        JsonSerializer.Serialize(Definition(optionsQueryId), new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

    private static Call Get(string label, string url, Expect expect = Expect.Ok) => new(label, HttpMethod.Get, url, null, expect);

    private static Call Post(string label, string url, object? body, Expect expect = Expect.Ok, Call? twin = null) => new(label, HttpMethod.Post, url, body, expect, twin);

    private static object ParamOptionsBody(Ids a, Guid optionsQueryId) => new
    {
        dataSourceId = a.DataSource,
        parameter = new { name = "p", label = "p", dataType = "Text", type = "QueryList", optionsQueryId, optionsLabelColumn = "c", optionsValueColumn = "c" }
    };

    // Endpoints without a guid in the route. Each either takes A's ids in the body or the query string, or takes no id and
    // must neither leak A nor fail. "Ok" means 2xx; anything else is an explicit Exact with its reason.
    private static readonly Dictionary<string, Func<Ids, Ids, Call[]>> NoIdCalls = new()
    {
        ["GET /api/admin/access"] = (_, _) => [Get("access", "/api/admin/access")],
        ["GET /api/admin/data-source-management"] = (_, _) => [Get("mgmt", "/api/admin/data-source-management")],
        ["POST /api/admin/data-sources"] = (_, _) =>
        [
            new("create", HttpMethod.Post, "/api/admin/data-sources",
                new { name = "n", provider = "SqlServer", connection = new { host = "h", database = "d", username = "u", password = "p" } },
                Expect.Exact, Status: 201, Why: "creates a managed source in B, which stores the details without connecting")
        ],
        ["POST /api/admin/data-sources/test"] = (a, _) =>
        [
            Post("test-existing-A", "/api/admin/data-sources/test",
                new { provider = "SqlServer", existingId = a.DataSource, connection = new { host = "localhost", database = "d", username = "u" } }, Expect.NotFound)
        ],
        ["GET /api/admin/encryption-key"] = (_, _) => [Get("key", "/api/admin/encryption-key")],
        ["POST /api/admin/encryption-key/reencrypt"] = (_, _) => [Post("reencrypt", "/api/admin/encryption-key/reencrypt", null)],
        ["GET /api/admin/data-sources"] = (_, _) => [Get("admin list", "/api/admin/data-sources")],
        ["GET /api/audit"] = (a, _) =>
        [
            Get("audit-entity", $"/api/audit?entityType=SavedQuery&entityId={a.Query}", Expect.NotFound),
            Get("audit-datasource", $"/api/audit?dataSourceId={a.DataSource}"),
            Get("audit-all", "/api/audit")
        ],
        ["GET /api/visualizations/pickable"] = (_, _) => [Get("pickable", "/api/visualizations/pickable")],
        ["GET /api/dashboards"] = (_, _) => [Get("dashboards", "/api/dashboards")],
        ["POST /api/dashboards"] = (_, _) => [Post("create dashboard", "/api/dashboards", new { name = "mine", description = "x" })],
        ["GET /api/data-sources"] = (_, _) => [Get("sources", "/api/data-sources")],
        ["GET /api/me"] = (_, _) => [Get("me", "/api/me")],
        ["POST /api/parameters/options"] = (a, _) =>
        [
            Post("param options A query", "/api/parameters/options", ParamOptionsBody(a, a.Query), Expect.Refused,
                Post("param options missing query", "/api/parameters/options", ParamOptionsBody(a, Guid.NewGuid())))
        ],
        ["GET /api/queries"] = (_, _) => [Get("queries", "/api/queries")],
        ["POST /api/queries"] = (a, b) =>
        [
            Post("save onto A data source", "/api/queries", new { name = "n", dataSourceId = a.DataSource, definition = Definition() }, Expect.NotFound),
            Post("overwrite A query", "/api/queries", new { id = a.Query, name = "n", dataSourceId = b.DataSource, definition = Definition() }, Expect.NotFound),
            Post("save B query sourcing a parameter from A's query", "/api/queries",
                new { name = "n", dataSourceId = b.DataSource, definition = Definition(a.Query) }, Expect.Same,
                Post("save B query sourcing a parameter from a missing query", "/api/queries",
                    new { name = "n", dataSourceId = b.DataSource, definition = Definition(Guid.NewGuid()) }))
        ],
        ["POST /api/queries/preview-sql"] = (a, b) => RunLike("preview-sql", a, b),
        ["POST /api/queries/run"] = (a, b) => RunLike("run", a, b),
        ["POST /api/queries/export"] = (a, b) => RunLike("export", a, b, export: true)
    };

    // B parent with an A child or an A id in the body.
    private static Call[] Extras(Ids a, Ids b)
    {
        object Tiles(Guid? optionsQueryId) => new
        {
            version = 1,
            tiles = Array.Empty<object>(),
            parameters = new[] { new { name = "dp", title = "dp", type = "QueryList", options = (string[]?)null, optionsQueryId, optionsLabelColumn = "c", optionsValueColumn = "c", allowMultiple = false, value = (string?)null } }
        };

        return
        [
            new("tile chart: B dashboard, A visualization", HttpMethod.Post, $"/api/dashboards/{b.Dashboard}/tiles/chart", new { visualizationId = a.Visualization }, Expect.NotFound),
            new("tiles: B dashboard, A visualization", HttpMethod.Put, $"/api/dashboards/{b.Dashboard}/tiles",
                new { version = 1, tiles = new[] { new { kind = "Chart", order = 0, x = 0, y = 0, width = 6, height = 4, autoHeight = false, visualizationId = a.Visualization } } }, Expect.NotFound),
            new("tiles: B dashboard, parameter sourced from A's query", HttpMethod.Put, $"/api/dashboards/{b.Dashboard}/tiles", Tiles(a.Query), Expect.Same,
                new("tiles: B dashboard, parameter sourced from a missing query", HttpMethod.Put, $"/api/dashboards/{b.Dashboard}/tiles", Tiles(Guid.NewGuid()), Expect.Ok)),
            new("share dashboard + A query", HttpMethod.Post, $"/api/dashboards/{b.Dashboard}/shares",
                new { sharedWithUserId = "x", sharedWithUserEmail = "x@x", accessLevel = "Viewer", alsoShareQueryIds = new[] { a.Query } }, Expect.BadRequest,
                new("share dashboard + missing query", HttpMethod.Post, $"/api/dashboards/{b.Dashboard}/shares",
                    new { sharedWithUserId = "x", sharedWithUserEmail = "x@x", accessLevel = "Viewer", alsoShareQueryIds = new[] { Guid.NewGuid() } }, Expect.Ok)),
            new("B query, A share", HttpMethod.Delete, $"/api/queries/{b.Query}/shares/{a.QueryShare}", null, Expect.NotFound),
            new("B dashboard, A share", HttpMethod.Delete, $"/api/dashboards/{b.Dashboard}/shares/{a.DashboardShare}", null, Expect.NotFound),
            new("B query, A visualization update", HttpMethod.Put, $"/api/queries/{b.Query}/visualizations/{a.Visualization}", new { name = "v", type = "Table", options = new { table = new { columns = new[] { "c" } } } }, Expect.NotFound),
            new("B query, A visualization delete", HttpMethod.Delete, $"/api/queries/{b.Query}/visualizations/{a.Visualization}", null, Expect.NotFound),
            new("B query, A visualization data", HttpMethod.Post, $"/api/queries/{b.Query}/visualizations/{a.Visualization}/data", new { parameterValues = new { } }, Expect.NotFound),
            new("A query, B visualization", HttpMethod.Delete, $"/api/queries/{a.Query}/visualizations/{b.Visualization}", null, Expect.NotFound)
        ];
    }

    private static Call[] RunLike(string label, Ids a, Ids b, bool export = false)
    {
        object Body(Guid dataSource, Guid? saved, Guid? optionsQuery = null, string? p = null) => export
            ? new { dataSourceId = dataSource, definition = Definition(optionsQuery), parameterValues = p is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["p"] = p }, format = "csv", fileName = "f", savedQueryId = saved }
            : new { dataSourceId = dataSource, definition = Definition(optionsQuery), parameterValues = p is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["p"] = p }, maxRows = 5, savedQueryId = saved };
        string url = $"/api/queries/{label}";

        // Preview takes no saved query, so a B data source (which has no real connection here) says nothing about isolation.
        Call[] cases =
        [
            Post($"{label} A data source", url, Body(a.DataSource, null), Expect.NotFound),
            Post($"{label} A data source + A query", url, Body(a.DataSource, a.Query), Expect.NotFound),
            Post($"{label} B data source + A query", url, Body(b.DataSource, a.Query), Expect.NotFound),
            Post($"{label} B data source, parameter sourced from A's query", url, Body(b.DataSource, null, a.Query, "x"), Expect.Same,
                Post($"{label} B data source, parameter sourced from a missing query", url, Body(b.DataSource, null, Guid.NewGuid(), "x")))
        ];
        return label == "preview-sql" ? [cases[0], cases[1], cases[3]] : cases;
    }

    private static async Task<Ids> SeedAsync(AppDbContext db, Guid app, string tag, string owner, AesGcmConnectionSecretProtector? oldKey = null)
    {
        var ids = new Ids(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.DataSources.Add(new DataSource { Id = ids.DataSource, AppId = app, Name = $"{tag}-source", ConnectionStringName = "biz", AllowedObjects = ["dbo.v"], ScopeKeys = ["ProgramId"] });
        db.SavedQueries.Add(new SavedQuery { Id = ids.Query, AppId = app, Name = $"{tag}-query", DataSourceId = ids.DataSource, OwnerId = owner, DefinitionJson = DefinitionJson(ids.Query) });
        db.Dashboards.Add(new Dashboard { Id = ids.Dashboard, AppId = app, Name = $"{tag}-dashboard", OwnerId = owner });
        if (oldKey is not null)
        {
            var details = new ManagedConnectionDetails("db.example", 1433, "d", "u", "pw", ConnectionSslMode.Require);
            db.DataSources.Add(new DataSource
            {
                Id = ids.Managed, AppId = app, Name = $"{tag}-managed", ConnectionStringName = DataSource.ManagedConnectionSentinel,
                EncryptedConnection = oldKey.Protect(ids.Managed, JsonSerializer.Serialize(details, QueryBuilderJson.Options))
            });
        }

        await db.SaveChangesAsync();
        db.QueryShares.Add(new QueryShare { Id = ids.QueryShare, AppId = app, SavedQueryId = ids.Query, SharedWithUserId = "other", SharedWithUserEmail = $"{tag}@x" });
        db.QueryVisualizations.Add(new QueryVisualization { Id = ids.Visualization, AppId = app, SavedQueryId = ids.Query, Name = $"{tag}-viz" });
        db.DashboardShares.Add(new DashboardShare { Id = ids.DashboardShare, AppId = app, DashboardId = ids.Dashboard, SharedWithUserId = "other", SharedWithUserEmail = $"{tag}@x" });
        db.DataScopeRules.Add(new DataScopeRule { Id = ids.Rule, AppId = app, DataSourceId = ids.DataSource, ObjectName = "dbo.v", ScopeKey = "k", ColumnName = "c" });
        db.SeenUsers.Add(new SeenUser { AppId = app, UserId = $"{tag}-seen-{ids.Query:N}", Name = $"{tag}-seen-name", Email = $"{tag}-seen@x" });
        await db.SaveChangesAsync();
        db.DashboardTiles.Add(new DashboardTile { Id = ids.Tile, AppId = app, DashboardId = ids.Dashboard, Kind = DashboardTileKind.Chart, VisualizationId = ids.Visualization });
        db.AuditLogEntries.Add(new AuditLogEntry { AppId = app, EntityType = "SavedQuery", EntityId = ids.Query, Summary = $"{tag}-audit" });
        await db.SaveChangesAsync();
        return ids;
    }

    // Every column of every row that belongs to the app, shadow properties and list columns included.
    private static async Task<string> SnapshotAsync(AppDbContext db, Guid app)
    {
        var parts = new List<string>();
        foreach (var type in db.Model.GetEntityTypes()
                     .Where(t => t.FindProperty("AppId") is not null || t.ClrType == typeof(App))
                     .OrderBy(t => t.ClrType.Name))
        {
            var rows = await (Task<List<string>>)typeof(CrossAppEndpointMatrixTests)
                .GetMethod(nameof(RowsAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(type.ClrType).Invoke(null, [db, app])!;
            parts.Add($"{type.ClrType.Name}: {rows.Count}\n" + string.Join("\n", rows.Order(StringComparer.Ordinal)));
        }

        return string.Join("\n--\n", parts);
    }

    private static string Render(object? value) => value switch
    {
        null => "null",
        string s => s,
        byte[] bytes => Convert.ToBase64String(bytes),
        System.Collections.IEnumerable list => JsonSerializer.Serialize(list),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
    };

    private static async Task<List<string>> RowsAsync<T>(AppDbContext db, Guid app) where T : class
    {
        var type = db.Model.FindEntityType(typeof(T))!;
        var idOf = type.ClrType == typeof(App) ? "Id" : "AppId";
        var rows = new List<string>();
        foreach (var row in await db.Set<T>().ToListAsync())
        {
            var entry = db.Entry(row);
            if (!Equals(entry.Property(idOf).CurrentValue, app))
            {
                continue;
            }

            rows.Add(string.Join("|", type.GetProperties().Select(p => $"{p.Name}={Render(entry.Property(p.Name).CurrentValue)}")));
        }

        db.ChangeTracker.Clear();
        return rows;
    }

    [GeneratedRegex(@"\{(\w+)(:guid)?\}")]
    private static partial Regex RouteParameter();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidText();

    private static string Normalize(string raw) => RouteParameter().Replace(raw, "{$1}").TrimEnd('/');

    private static IEnumerable<(string Method, string Raw)> Discover(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith(Mount + "/api", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"]).Select(m => (m, e.RoutePattern.RawText![Mount.Length..])))
            .Distinct();

    private static string IdFor(string normalizedPath, string name, Ids ids)
    {
        var dashboards = normalizedPath.StartsWith("/api/dashboards", StringComparison.Ordinal);
        return name switch
        {
            "queryId" => ids.Query.ToString(),
            "visualizationId" => ids.Visualization.ToString(),
            "shareId" => (dashboards ? ids.DashboardShare : ids.QueryShare).ToString(),
            "name" => "p",
            "id" when dashboards => ids.Dashboard.ToString(),
            "id" when normalizedPath.Contains("data-sources", StringComparison.Ordinal) => ids.DataSource.ToString(),
            "id" => ids.Query.ToString(),
            _ => throw new InvalidOperationException($"Route parameter {{{name}}} of {normalizedPath} has no mapping in the matrix.")
        };
    }

    private static IEnumerable<Call> GuidCalls(string method, string raw, Ids a, Ids b, bool withMixed)
    {
        var normalized = Normalize(raw);
        var body = Bodies[$"{method} {normalized}"](a);
        var names = RouteParameter().Matches(raw).Select(m => m.Groups[1].Value).ToList();

        string Url(bool mixed) => RouteParameter().Replace(raw, m =>
        {
            var useB = mixed && m.Index == RouteParameter().Match(raw).Index;
            return IdFor(normalized, m.Groups[1].Value, useB ? b : a);
        });

        var query = normalized.EndsWith("/share-preview", StringComparison.Ordinal) ? "?userId=someone" : "";
        yield return new Call($"{method} {normalized}", new HttpMethod(method), Url(false).TrimEnd('/') + query, body, Expect.NotFound);
        if (withMixed && names.Count(n => n != "name") >= 2)
        {
            yield return new Call($"{method} {normalized} (B parent, A child)", new HttpMethod(method), Url(true).TrimEnd('/') + query, body, Expect.NotFound);
        }
    }

    private static async Task<(int Status, string Text)> SendAsync(HubApiHost.Browser caller, Call call)
    {
        var response = await caller.SendAsync(call.Method, Mount + call.Url, call.Body);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string Blur(string text) => Regex.Replace(GuidText().Replace(text, "<id>"), @"""executionTimeMs"":\d+", @"""executionTimeMs"":0");

    private static async Task RunMatrixAsync(HubApiHost env, HubApiHost.Browser caller, string who, Ids a, Ids b, Func<Task<Ids>> newBSet)
    {
        var endpoints = Discover(env.Host.Services).ToList();
        var calls = new List<Call>();
        var positives = new List<Call>();
        var unknown = new List<string>();
        var covered = new HashSet<string>();
        var excludedSeen = new HashSet<string>();
        foreach (var (method, raw) in endpoints)
        {
            var normalized = Normalize(raw);
            var key = $"{method} {normalized}";
            if (Excluded.ContainsKey(key))
            {
                excludedSeen.Add(key);
                continue;
            }

            if (raw.Contains(":guid", StringComparison.Ordinal) && Bodies.ContainsKey(key))
            {
                calls.AddRange(GuidCalls(method, raw, a, b, withMixed: true));
                var set = await newBSet();
                positives.AddRange(GuidCalls(method, raw, set, set, withMixed: false)
                    .Select(c => c with { Expect = Expect.Positive, Label = $"{c.Label} [B's own ids]" }));
                covered.Add(key);
            }
            else if (!raw.Contains("{", StringComparison.Ordinal) && NoIdCalls.TryGetValue(key, out var build))
            {
                calls.AddRange(build(a, b));
                covered.Add(key);
            }
            else
            {
                unknown.Add(key);
            }
        }

        Assert.True(unknown.Count == 0, $"Endpoints missing from the cross-app matrix (add a body row, a NoIdCalls row or an Excluded reason): {string.Join("; ", unknown)}");
        var stale = Bodies.Keys.Concat(NoIdCalls.Keys).Where(k => !covered.Contains(k)).Concat(Excluded.Keys.Where(k => !excludedSeen.Contains(k))).ToList();
        Assert.True(stale.Count == 0, $"Matrix rows that no longer match an endpoint: {string.Join("; ", stale)}");
        calls.AddRange(Extras(a, b));

        var secrets = new[] { "alpha-source", "alpha-query", "alpha-dashboard", "alpha-viz", "alpha-owner", "alpha-audit", "alpha@x", "alpha-managed", "alpha-seen" };
        var failures = new List<string>();

        async Task Check(Call call)
        {
            var (status, text) = await SendAsync(caller, call);
            var sent = Mount + call.Url + (call.Body is null ? "" : JsonSerializer.Serialize(call.Body));
            // A 404 legitimately echoes the id the caller typed; anything else must not carry any of A's ids.
            var ids = a.All.Select(i => i.ToString()).Where(i => status != 404 || !sent.Contains(i, StringComparison.OrdinalIgnoreCase));
            var leaked = secrets.Concat(ids).FirstOrDefault(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));

            string? problem = call.Expect switch
            {
                Expect.NotFound => status == 404 ? null : "expected 404",
                Expect.Ok => status is >= 200 and < 300 ? null : "expected 2xx",
                Expect.Exact => status == call.Status ? null : $"expected {call.Status} ({call.Why})",
                Expect.Positive => status == 404 || (status >= 500 && !PositiveMayFailServerSide.ContainsKey(call.Label)) ? "B's own ids must reach the handler (not 404, not 5xx)" : null,
                _ => await TwinProblem(call, status, text)
            };

            if (problem is not null || leaked is not null)
            {
                failures.Add($"[{who}] {call.Label}: {call.Method} {call.Url} -> {status}: {problem}{(leaked is not null ? $" leaked '{leaked}'" : "")} {(text.Length > 160 ? text[..160] : text)}");
            }
        }

        async Task<string?> TwinProblem(Call call, int status, string text)
        {
            var wanted = call.Expect switch { Expect.Refused => 403, Expect.BadRequest => 400, _ => 0 };
            if (wanted != 0 && status != wanted)
            {
                return $"expected {wanted}";
            }

            if (wanted == 0 && status >= 500)
            {
                return "expected below 500";
            }

            var (twinStatus, twinText) = await SendAsync(caller, call.Twin!);
            if (call.Expect == Expect.Same && twinStatus >= 500)
            {
                return $"twin answered {twinStatus}";
            }

            return twinStatus == status && Blur(twinText) == Blur(text) ? null : $"differs from the missing-id twin ({twinStatus}): {twinText}";
        }

        foreach (var call in calls)
        {
            await Check(call);
        }

        foreach (var call in positives)
        {
            await Check(call);
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static Task Scenario(bool superViewingB) => HubApiHost.RunAsync(async env =>
    {
        var caller = env.NewBrowser();
        if (superViewingB)
        {
            await caller.SignInSuperAsync(env.AppB);
        }
        else
        {
            await caller.SignInAppAsync(env.AppB, HubRole.Admin);
        }

        // App admins don't yet see others' private rows, so B's rows belong to the caller: a 404 on A's id must come
        // from the app boundary, not from an owner check that would have refused anyway.
        var me = await HubApiHost.Browser.JsonAsync(await caller.GetAsync($"{Mount}/api/me"));
        var callerId = me.GetProperty("userId").GetString()!;
        var oldKey = new AesGcmConnectionSecretProtector(new ConnectionSecretKey(OldKey));

        // Both apps' data sources point at the metadata database and read this view, so B's own queries, options and charts
        // really run and a cross-app leak would return real rows.
        await using (var setup = env.HubWideDb())
        {
            await setup.Database.ExecuteSqlRawAsync("CREATE VIEW dbo.v AS SELECT CAST('x' AS nvarchar(10)) AS c");
        }

        Ids a, b;
        string before;
        await using (var db = env.HubWideDb())
        {
            a = await SeedAsync(db, env.AppA, "alpha", "alpha-owner", oldKey);
            b = await SeedAsync(db, env.AppB, "bravo", callerId, oldKey);
            db.AppReturnUrls.Add(new AppReturnUrl { AppId = env.AppA, Url = "https://alpha.example/back" });
            await db.SaveChangesAsync();
        }

        await using (var db = env.HubWideDb())
        {
            before = await SnapshotAsync(db, env.AppA);
        }

        foreach (var expected in new[] { "SavedQuery: 1", "DashboardTile: 1", "DataScopeRule: 1", "AuditLogEntry: 1", "SeenUser: 1", "AppReturnUrl: 1", "AppSigningKey: 1", "App: 1", "DataSource: 2" })
        {
            Assert.Contains(expected, before);
        }

        Assert.Contains("[\"ProgramId\"]", before);
        Assert.Contains("[\"dbo.v\"]", before);

        // Sanity: the caller really is B, so a 404 on A's id is not just "signed out".
        var own = await caller.GetAsync($"{Mount}/api/data-sources");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var listed = await own.Content.ReadAsStringAsync();
        Assert.Contains("bravo-source", listed);
        Assert.DoesNotContain("alpha-source", listed);

        // Key rotation as B sees and moves only B's managed source; A's blob stays under the old key (the snapshot proves it).
        var keyStatus = await HubApiHost.Browser.JsonAsync(await caller.GetAsync($"{Mount}/api/admin/encryption-key"));
        Assert.Equal(1, keyStatus.GetProperty("managedCount").GetInt32());
        Assert.Equal(1, keyStatus.GetProperty("onPreviousKey").GetInt32());
        Assert.DoesNotContain(a.Managed.ToString(), keyStatus.ToString());
        Assert.DoesNotContain("alpha-managed", keyStatus.ToString());
        var reencrypt = await HubApiHost.Browser.JsonAsync(await caller.PostAsync($"{Mount}/api/admin/encryption-key/reencrypt", null));
        Assert.Equal(1, reencrypt.GetProperty("reencrypted").GetInt32());

        async Task<Ids> NewBSet()
        {
            await using var db = env.HubWideDb();
            var set = await SeedAsync(db, env.AppB, "bravo", callerId);
            return set;
        }

        await RunMatrixAsync(env, caller, superViewingB ? "super viewing B" : "B admin", a, b, NewBSet);

        await using var after = env.HubWideDb();
        Assert.Equal(before, await SnapshotAsync(after, env.AppA));
    }, configure: o =>
    {
        o.DataSourceManagement.Enabled = true;
        o.DataSourceManagement.AllowAnonymousAdmins = true;
        o.DataSourceManagement.PreviousEncryptionKeys = [Convert.ToBase64String(OldKey)];
    });

    [MetadataSqlFact]
    public Task App_b_admin_cannot_see_or_change_app_a_through_any_endpoint() => Scenario(superViewingB: false);

    [MetadataSqlFact]
    public Task Super_admin_viewing_app_b_cannot_see_or_change_app_a_through_any_endpoint() => Scenario(superViewingB: true);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class FakeUser : ICurrentUserService
    {
        public string UserId => "mary";
        public string DisplayName => "Mary";
        public string? Email => "m@x";
        public HubRole? Role => HubRole.Admin;
        public bool IsSuperAdmin => false;
    }

    private sealed class ThrowingUser : ICurrentUserService
    {
        public string UserId => throw new InvalidOperationException("host resolver failed");
        public string DisplayName => "";
        public string? Email => null;
        public HubRole? Role => null;
        public bool IsSuperAdmin => false;
    }

    private static async Task<(int Status, List<(LogLevel Level, string Message)> Log)> HandleAsync(Exception exception, ICurrentUserService? user = null)
    {
        var log = new CapturingLogger<GlobalExceptionHandler>();
        var services = new ServiceCollection().AddSingleton(user ?? new FakeUser()).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = new MemoryStream();
        await new GlobalExceptionHandler(log).TryHandleAsync(http, exception, CancellationToken.None);
        return (http.Response.StatusCode, log.Entries);
    }

    [Fact]
    public async Task A_refused_cross_app_write_is_logged_at_warning_with_ids_only()
    {
        var app = Guid.NewGuid();
        var rowApp = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var (status, log) = await HandleAsync(new CrossAppWriteException("That row belongs to another app.", app, "SavedQuery", entity.ToString(), rowApp));

        Assert.Equal(404, status);
        var warning = Assert.Single(log, e => e.Level == LogLevel.Warning);
        Assert.Contains(app.ToString(), warning.Message);
        Assert.Contains(rowApp.ToString(), warning.Message);
        Assert.Contains("mary", warning.Message);
        Assert.Contains("SavedQuery", warning.Message);
        Assert.Contains(entity.ToString(), warning.Message);
    }

    [Fact]
    public async Task A_throwing_host_resolver_does_not_turn_the_404_into_a_500()
    {
        var (status, log) = await HandleAsync(new CrossAppWriteException("x", Guid.NewGuid(), "SavedQuery", "1"), new ThrowingUser());

        Assert.Equal(404, status);
        Assert.Contains(log, e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain(log, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task A_plain_not_found_is_not_a_warning()
    {
        var (status, log) = await HandleAsync(new NotFoundException("SavedQuery", Guid.NewGuid()));

        Assert.Equal(404, status);
        Assert.DoesNotContain(log, e => e.Level >= LogLevel.Warning);
    }

    [MetadataSqlFact]
    public async Task The_save_guard_names_app_entity_type_row_app_and_id_without_contents() =>
        await MetadataDb.WithDatabaseAsync(async (options, _) =>
        {
            var appA = Guid.NewGuid();
            var appB = Guid.NewGuid();
            var rowId = Guid.NewGuid();
            await using (var hub = new AppDbContext(options, new FixedAppContext(Guid.Empty, hubWide: true)))
            {
                hub.Apps.AddRange(new App { Id = appA, Key = "a", Name = "A" }, new App { Id = appB, Key = "b", Name = "B" });
                await hub.SaveChangesAsync();
                hub.DataSources.Add(new DataSource { Id = rowId, AppId = appA, Name = "secret-name" });
                await hub.SaveChangesAsync();
            }

            await using var asB = new AppDbContext(options, new FixedAppContext(appB));
            var row = new DataSource { Id = rowId, AppId = appA, Name = "secret-name-2" };
            asB.DataSources.Attach(row);
            asB.Entry(row).State = EntityState.Modified;
            var ex = await Assert.ThrowsAsync<CrossAppWriteException>(() => asB.SaveChangesAsync());

            Assert.Equal(appB, ex.AppId);
            Assert.Equal(appA, ex.RowAppId);
            Assert.Equal(nameof(DataSource), ex.EntityType);
            Assert.Equal(rowId.ToString(), ex.EntityId);
            Assert.DoesNotContain("secret", ex.Message);

            // No app at all: the refusal still names what was attempted.
            await using var none = new AppDbContext(options, new FixedAppContext(Guid.Empty));
            var created = Guid.NewGuid();
            none.DataSources.Add(new DataSource { Id = created, Name = "secret-name-3" });
            var noApp = await Assert.ThrowsAsync<CrossAppWriteException>(() => none.SaveChangesAsync());
            Assert.Equal(nameof(DataSource), noApp.EntityType);
            Assert.Equal(created.ToString(), noApp.EntityId);
            Assert.DoesNotContain("secret", noApp.Message);
        });
}
