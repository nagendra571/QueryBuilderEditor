using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor.Middleware;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

/// <summary>
/// Calls every /querybuilder/api endpoint as App B (and as a super admin viewing App B) with App A's ids, and proves
/// nothing of App A was revealed or changed. The endpoint list is read from the running host, so an endpoint added
/// without a row in <see cref="Bodies"/>, <see cref="NoIdCalls"/> or <see cref="Excluded"/> fails the test.
/// </summary>
[Collection(MetadataDatabaseCollection.Name)]
public partial class CrossAppEndpointMatrixTests
{
    private const string Mount = "/querybuilder";

    private sealed record Ids(
        Guid DataSource, Guid Query, Guid Visualization, Guid Dashboard, Guid Tile, Guid QueryShare, Guid DashboardShare, Guid Rule);

    // Refused: 403 on purpose, the same answer as for a missing id, so existence is not revealed.
    // BadRequest: rejected by a rule that looks only at the caller's own app, before any other app's row is read.
    private enum Expect { NotFound, Harmless, Refused, BadRequest }

    private sealed record Call(string Label, HttpMethod Method, string Url, object? Body, Expect Expect);

    // Routes that never take one app's row by id, with the reason they are not part of the per-id matrix.
    private static readonly Dictionary<string, string> Excluded = new()
    {
        ["/api/hub/apps"] = "super-admin-only registry of apps, not app-scoped; gate covered by HubAuthorizationTests / HubRegistryTests",
        ["/api/hub/view-app"] = "super-admin-only choice of the app to view; covered by SuperAdminTests"
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

    private static readonly object EmptyDefinition = new { source = new { schemaName = "dbo", objectName = "v", kind = "View", alias = "t" }, columns = new[] { new { tableAlias = "t", columnName = "c" } } };

    // Endpoints without a guid in the route. Each either takes A's ids in the body or in the query string (expected 404),
    // or takes no id and must simply neither leak A nor fail (Harmless).
    private static readonly Dictionary<string, Func<Ids, Ids, Call[]>> NoIdCalls = new()
    {
        ["GET /api/admin/access"] = (_, _) => [Get("access", "/api/admin/access", Expect.Harmless)],
        ["GET /api/admin/data-source-management"] = (_, _) => [Get("mgmt", "/api/admin/data-source-management", Expect.Harmless)],
        ["POST /api/admin/data-sources"] = (_, _) => [new("create", HttpMethod.Post, "/api/admin/data-sources", new { name = "n", provider = "SqlServer", connection = new { host = "h", database = "d", username = "u", password = "p" } }, Expect.Harmless)],
        ["POST /api/admin/data-sources/test"] = (a, _) =>
        [
            new("test-existing-A", HttpMethod.Post, "/api/admin/data-sources/test", new { provider = "SqlServer", existingId = a.DataSource, connection = new { host = "localhost", database = "d", username = "u" } }, Expect.NotFound)
        ],
        ["GET /api/admin/encryption-key"] = (_, _) => [Get("key", "/api/admin/encryption-key", Expect.Harmless)],
        ["POST /api/admin/encryption-key/reencrypt"] = (_, _) => [new("reencrypt", HttpMethod.Post, "/api/admin/encryption-key/reencrypt", null, Expect.Harmless)],
        ["GET /api/admin/data-sources"] = (_, _) => [Get("admin list", "/api/admin/data-sources", Expect.Harmless)],
        ["GET /api/audit"] = (a, _) =>
        [
            Get("audit-entity", $"/api/audit?entityType=SavedQuery&entityId={a.Query}", Expect.NotFound),
            Get("audit-datasource", $"/api/audit?dataSourceId={a.DataSource}", Expect.Harmless),
            Get("audit-all", "/api/audit", Expect.Harmless)
        ],
        ["GET /api/visualizations/pickable"] = (_, _) => [Get("pickable", "/api/visualizations/pickable", Expect.Harmless)],
        ["GET /api/dashboards"] = (_, _) => [Get("dashboards", "/api/dashboards", Expect.Harmless)],
        ["POST /api/dashboards"] = (_, _) => [new("create dashboard", HttpMethod.Post, "/api/dashboards", new { name = "mine", description = "x" }, Expect.Harmless)],
        ["GET /api/data-sources"] = (_, _) => [Get("sources", "/api/data-sources", Expect.Harmless)],
        ["GET /api/me"] = (_, _) => [Get("me", "/api/me", Expect.Harmless)],
        ["POST /api/parameters/options"] = (a, _) =>
        [
            new("param options A query", HttpMethod.Post, "/api/parameters/options",
                new { dataSourceId = a.DataSource, parameter = new { name = "p", label = "p", dataType = "Text", type = "QueryList", optionsQueryId = a.Query, optionsLabelColumn = "a", optionsValueColumn = "a" } }, Expect.Refused),
            new("param options missing query", HttpMethod.Post, "/api/parameters/options",
                new { dataSourceId = a.DataSource, parameter = new { name = "p", label = "p", dataType = "Text", type = "QueryList", optionsQueryId = Guid.NewGuid(), optionsLabelColumn = "a", optionsValueColumn = "a" } }, Expect.Refused)
        ],
        ["GET /api/queries"] = (_, _) => [Get("queries", "/api/queries", Expect.Harmless)],
        ["POST /api/queries"] = (a, b) =>
        [
            new("save onto A data source", HttpMethod.Post, "/api/queries", new { name = "n", dataSourceId = a.DataSource, definition = EmptyDefinition }, Expect.NotFound),
            new("overwrite A query", HttpMethod.Post, "/api/queries", new { id = a.Query, name = "n", dataSourceId = b.DataSource, definition = EmptyDefinition }, Expect.NotFound)
        ],
        ["POST /api/queries/preview-sql"] = (a, b) => RunLike("preview-sql", a, b),
        ["POST /api/queries/run"] = (a, b) => RunLike("run", a, b),
        ["POST /api/queries/export"] = (a, b) => RunLike("export", a, b, export: true)
    };

    // Same endpoints, but a B-owned parent with an A child id in the body or a second route id.
    private static Call[] Extras(Ids a, Ids b) =>
    [
        new("tile chart: B dashboard, A visualization", HttpMethod.Post, $"/api/dashboards/{b.Dashboard}/tiles/chart", new { visualizationId = a.Visualization }, Expect.NotFound),
        new("tiles: B dashboard, A visualization", HttpMethod.Put, $"/api/dashboards/{b.Dashboard}/tiles",
            new { version = 1, tiles = new[] { new { kind = "Chart", order = 0, x = 0, y = 0, width = 6, height = 4, autoHeight = false, visualizationId = a.Visualization } } }, Expect.NotFound),
        new("share dashboard + A query", HttpMethod.Post, $"/api/dashboards/{b.Dashboard}/shares",
            new { sharedWithUserId = "x", sharedWithUserEmail = "x@x", accessLevel = "Viewer", alsoShareQueryIds = new[] { a.Query } }, Expect.BadRequest),
        new("B query, A share", HttpMethod.Delete, $"/api/queries/{b.Query}/shares/{a.QueryShare}", null, Expect.NotFound),
        new("B dashboard, A share", HttpMethod.Delete, $"/api/dashboards/{b.Dashboard}/shares/{a.DashboardShare}", null, Expect.NotFound),
        new("B query, A visualization update", HttpMethod.Put, $"/api/queries/{b.Query}/visualizations/{a.Visualization}", new { name = "v", type = "Table", options = new { table = new { columns = new[] { "c" } } } }, Expect.NotFound),
        new("B query, A visualization delete", HttpMethod.Delete, $"/api/queries/{b.Query}/visualizations/{a.Visualization}", null, Expect.NotFound),
        new("B query, A visualization data", HttpMethod.Post, $"/api/queries/{b.Query}/visualizations/{a.Visualization}/data", new { parameterValues = new { } }, Expect.NotFound),
        new("A query, B visualization", HttpMethod.Delete, $"/api/queries/{a.Query}/visualizations/{b.Visualization}", null, Expect.NotFound)
    ];

    private static Call Get(string label, string url, Expect expect) => new(label, HttpMethod.Get, url, null, expect);

    private static Call[] RunLike(string label, Ids a, Ids b, bool export = false)
    {
        object Body(Guid dataSource, Guid? saved) => export
            ? new { dataSourceId = dataSource, definition = EmptyDefinition, parameterValues = new { }, format = "csv", fileName = "f", savedQueryId = saved }
            : new { dataSourceId = dataSource, definition = EmptyDefinition, parameterValues = new { }, maxRows = 5, savedQueryId = saved };
        // Preview takes no saved query, so a B data source (which has no real connection here) says nothing about isolation.
        Call[] cases =
        [
            new($"{label} A data source", HttpMethod.Post, $"/api/queries/{label}", Body(a.DataSource, null), Expect.NotFound),
            new($"{label} A data source + A query", HttpMethod.Post, $"/api/queries/{label}", Body(a.DataSource, a.Query), Expect.NotFound),
            new($"{label} B data source + A query", HttpMethod.Post, $"/api/queries/{label}", Body(b.DataSource, a.Query), Expect.NotFound)
        ];
        return label == "preview-sql" ? cases[..2] : cases;
    }

    private static async Task<Ids> SeedAsync(AppDbContext db, Guid app, string tag, string owner)
    {
        var ids = new Ids(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.DataSources.Add(new DataSource { Id = ids.DataSource, AppId = app, Name = $"{tag}-source" });
        db.SavedQueries.Add(new SavedQuery { Id = ids.Query, AppId = app, Name = $"{tag}-query", DataSourceId = ids.DataSource, OwnerId = owner, DefinitionJson = """{"source":{"schemaName":"dbo","objectName":"v","kind":"View","alias":"t"},"columns":[{"tableAlias":"t","columnName":"c"}]}""" });
        db.Dashboards.Add(new Dashboard { Id = ids.Dashboard, AppId = app, Name = $"{tag}-dashboard", OwnerId = owner });
        await db.SaveChangesAsync();
        db.QueryShares.Add(new QueryShare { Id = ids.QueryShare, AppId = app, SavedQueryId = ids.Query, SharedWithUserId = "other", SharedWithUserEmail = $"{tag}@x" });
        db.QueryVisualizations.Add(new QueryVisualization { Id = ids.Visualization, AppId = app, SavedQueryId = ids.Query, Name = $"{tag}-viz" });
        db.DashboardShares.Add(new DashboardShare { Id = ids.DashboardShare, AppId = app, DashboardId = ids.Dashboard, SharedWithUserId = "other", SharedWithUserEmail = $"{tag}@x" });
        db.DataScopeRules.Add(new DataScopeRule { Id = ids.Rule, AppId = app, DataSourceId = ids.DataSource, ObjectName = "dbo.v", ScopeKey = "k", ColumnName = "c" });
        await db.SaveChangesAsync();
        db.DashboardTiles.Add(new DashboardTile { Id = ids.Tile, AppId = app, DashboardId = ids.Dashboard, Kind = DashboardTileKind.Chart, VisualizationId = ids.Visualization });
        db.AuditLogEntries.Add(new AuditLogEntry { AppId = app, EntityType = "SavedQuery", EntityId = ids.Query, Summary = $"{tag}-audit" });
        await db.SaveChangesAsync();
        return ids;
    }

    private static async Task<string> SnapshotAsync(AppDbContext db, Guid app)
    {
        var parts = new List<string>();
        foreach (var type in db.Model.GetEntityTypes().Where(t => typeof(IAppOwned).IsAssignableFrom(t.ClrType) || t.ClrType == typeof(AuditLogEntry)).OrderBy(t => t.ClrType.Name))
        {
            var rows = await (Task<List<string>>)typeof(CrossAppEndpointMatrixTests)
                .GetMethod(nameof(RowsAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(type.ClrType).Invoke(null, [db, app, type])!;
            parts.Add($"{type.ClrType.Name}: {rows.Count}\n" + string.Join("\n", rows.Order(StringComparer.Ordinal)));
        }

        return string.Join("\n--\n", parts);
    }

    private static async Task<List<string>> RowsAsync<T>(AppDbContext db, Guid app, Microsoft.EntityFrameworkCore.Metadata.IEntityType type) where T : class
    {
        var set = db.Set<T>().AsNoTracking();
        var properties = type.GetProperties().Where(p => p.PropertyInfo is not null).ToList();
        var appProperty = properties.Single(p => p.Name == "AppId");
        var all = await set.ToListAsync();
        return all
            .Where(r => Equals(appProperty.PropertyInfo!.GetValue(r), app) || (type.ClrType == typeof(AuditLogEntry) && Equals(appProperty.PropertyInfo!.GetValue(r), (Guid?)app)))
            .Select(r => string.Join("|", properties.Select(p => $"{p.Name}={p.PropertyInfo!.GetValue(r)}")))
            .ToList();
    }

    [GeneratedRegex(@"\{(\w+)(:guid)?\}")]
    private static partial Regex RouteParameter();

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

    private static IEnumerable<Call> GuidCalls(string method, string raw, Ids a, Ids b)
    {
        var normalized = Normalize(raw);
        var body = Bodies[$"{method} {normalized}"](a);
        var names = RouteParameter().Matches(raw).Select(m => m.Groups[1].Value).ToList();

        string Url(bool mixed) => RouteParameter().Replace(raw, m =>
        {
            var useB = mixed && m.Index == RouteParameter().Match(raw).Index;
            return IdFor(normalized, m.Groups[1].Value, useB ? b : a);
        });

        const Expect expect = Expect.NotFound;
        var query = normalized.EndsWith("/share-preview", StringComparison.Ordinal) ? "?userId=someone" : "";
        yield return new Call($"{method} {normalized}", new HttpMethod(method), Url(false).TrimEnd('/') + query, body, expect);
        if (names.Count(n => n != "name") >= 2)
        {
            yield return new Call($"{method} {normalized} (B parent, A child)", new HttpMethod(method), Url(true).TrimEnd('/') + query, body, expect);
        }
    }

    private static async Task RunMatrixAsync(HubApiHost env, HubApiHost.Browser caller, string who, Ids a, Ids b)
    {
        var endpoints = Discover(env.Host.Services).ToList();
        var calls = new List<Call>();
        var unknown = new List<string>();
        var covered = new HashSet<string>();
        foreach (var (method, raw) in endpoints)
        {
            var normalized = Normalize(raw);
            var key = $"{method} {normalized}";
            if (Excluded.Keys.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            if (raw.Contains(":guid", StringComparison.Ordinal) && Bodies.ContainsKey(key))
            {
                calls.AddRange(GuidCalls(method, raw, a, b));
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
        var stale = Bodies.Keys.Concat(NoIdCalls.Keys).Where(k => !covered.Contains(k)).ToList();
        Assert.True(stale.Count == 0, $"Matrix rows that no longer match an endpoint: {string.Join("; ", stale)}");
        calls.AddRange(Extras(a, b));

        var secrets = new[] { "alpha-source", "alpha-query", "alpha-dashboard", "alpha-viz", "alpha-owner", "alpha-audit", "alpha@x" };
        var failures = new List<string>();
        foreach (var call in calls)
        {
            var response = await caller.SendAsync(call.Method, Mount + call.Url, call.Body);
            var text = await response.Content.ReadAsStringAsync();
            var status = (int)response.StatusCode;
            var leaked = secrets.Concat(call.Expect == Expect.Harmless ? [a.Query.ToString(), a.DataSource.ToString(), a.Dashboard.ToString(), a.Visualization.ToString()] : [])
                .FirstOrDefault(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));
            var ok = call.Expect switch
            {
                Expect.NotFound => status == 404,
                Expect.Refused => status == 403,
                Expect.BadRequest => status == 400,
                _ => status < 500 && status != 403
            };
            if (!ok || leaked is not null)
            {
                failures.Add($"[{who}] {call.Label}: {call.Method} {call.Url} -> {status}{(leaked is not null ? $", leaked '{leaked}'" : "")} {(text.Length > 160 ? text[..160] : text)}");
            }
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

        Ids a, b;
        string before;
        await using (var db = env.HubWideDb())
        {
            a = await SeedAsync(db, env.AppA, "alpha", "alpha-owner");
            b = await SeedAsync(db, env.AppB, "bravo", callerId);
            before = await SnapshotAsync(db, env.AppA);
        }

        // B's catalog is cached so B's own queries and charts validate without a real business database; a 404 on A's
        // ids then comes from the app boundary rather than from "B's source can't be reached".
        env.Host.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Set($"catalog:{b.DataSource}", new Domain.Model.DataSourceCatalog
        {
            DataSourceId = b.DataSource,
            Schemas = [new Domain.Model.SchemaMetadata { Name = "dbo", Objects = [new Domain.Model.SchemaObjectMetadata
            {
                SchemaName = "dbo", Name = "v", Kind = SchemaObjectKind.View,
                Columns = [new Domain.Model.ColumnMetadata { Name = "c", SqlType = "nvarchar", DataType = ColumnDataType.Text, OrdinalPosition = 1 }]
            }] }]
        });

        Assert.Contains("SavedQuery: 1", before);
        Assert.Contains("DashboardTile: 1", before);
        Assert.Contains("DataScopeRule: 1", before);
        Assert.Contains("AuditLogEntry: 1", before);

        // Sanity: the caller really is B, so a 404 on A's id is not just "signed out".
        var own = await caller.GetAsync($"{Mount}/api/data-sources");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var listed = await own.Content.ReadAsStringAsync();
        Assert.Contains("bravo-source", listed);
        Assert.DoesNotContain("alpha-source", listed);

        await RunMatrixAsync(env, caller, superViewingB ? "super viewing B" : "B admin", a, b);

        await using var after = env.HubWideDb();
        Assert.Equal(before, await SnapshotAsync(after, env.AppA));
    }, configure: o => { o.DataSourceManagement.Enabled = true; o.DataSourceManagement.AllowAnonymousAdmins = true; });

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

    private static async Task<(int Status, List<(LogLevel Level, string Message)> Log)> HandleAsync(Exception exception)
    {
        var log = new CapturingLogger<GlobalExceptionHandler>();
        var services = new ServiceCollection().AddSingleton<ICurrentUserService>(new FakeUser()).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = new MemoryStream();
        await new GlobalExceptionHandler(log).TryHandleAsync(http, exception, CancellationToken.None);
        return (http.Response.StatusCode, log.Entries);
    }

    [Fact]
    public async Task A_refused_cross_app_write_is_logged_at_warning_with_ids_only()
    {
        var app = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var (status, log) = await HandleAsync(new CrossAppWriteException("That row belongs to another app.", app, "SavedQuery", entity.ToString()));

        Assert.Equal(404, status);
        var warning = Assert.Single(log, e => e.Level == LogLevel.Warning);
        Assert.Contains(app.ToString(), warning.Message);
        Assert.Contains("mary", warning.Message);
        Assert.Contains("SavedQuery", warning.Message);
        Assert.Contains(entity.ToString(), warning.Message);
    }

    [Fact]
    public async Task A_plain_not_found_is_not_a_warning()
    {
        var (status, log) = await HandleAsync(new NotFoundException("SavedQuery", Guid.NewGuid()));

        Assert.Equal(404, status);
        Assert.DoesNotContain(log, e => e.Level >= LogLevel.Warning);
    }

    [MetadataSqlFact]
    public async Task The_save_guard_names_app_entity_type_and_id_without_contents() =>
        await MetadataDb.WithDatabaseAsync(async (options, _) =>
        {
            var appA = Guid.NewGuid();
            var appB = Guid.NewGuid();
            var queryId = Guid.NewGuid();
            await using (var hub = new AppDbContext(options, new FixedAppContext(Guid.Empty, hubWide: true)))
            {
                hub.Apps.AddRange(new App { Id = appA, Key = "a", Name = "A" }, new App { Id = appB, Key = "b", Name = "B" });
                await hub.SaveChangesAsync();
                hub.DataSources.Add(new DataSource { Id = queryId, AppId = appA, Name = "secret-name" });
                await hub.SaveChangesAsync();
            }

            await using var asB = new AppDbContext(options, new FixedAppContext(appB));
            var row = new DataSource { Id = queryId, AppId = appA, Name = "secret-name-2" };
            asB.DataSources.Attach(row);
            asB.Entry(row).State = EntityState.Modified;
            var ex = await Assert.ThrowsAsync<CrossAppWriteException>(() => asB.SaveChangesAsync());

            Assert.Equal(appB, ex.AppId);
            Assert.Equal(nameof(DataSource), ex.EntityType);
            Assert.Equal(queryId.ToString(), ex.EntityId);
            Assert.DoesNotContain("secret", ex.Message);
        });
}
