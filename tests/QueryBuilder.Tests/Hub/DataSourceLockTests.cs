using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Editor;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Security;
using Xunit;

namespace QueryBuilder.Tests.Hub;

/// <summary>Hub Part 3, task 6: who may change a data source, the connection switch, moving a source between apps, and
/// the hub-wide encryption key.</summary>
[Collection(MetadataDatabaseCollection.Name)]
public partial class DataSourceLockTests
{
    private const string Api = "/querybuilder/api";
    private static readonly byte[] OldKey = Enumerable.Repeat((byte)0x63, 32).ToArray();

    [GeneratedRegex(@"\{(\w+)(:guid)?\}")]
    private static partial Regex RouteParameter();

    private static async Task<DataSource> AddSourceAsync(HubApiHost env, Guid app, string name, bool locked = false, bool managed = false, AesGcmConnectionSecretProtector? key = null)
    {
        await using var db = env.HubWideDb();
        var source = new DataSource { AppId = app, Name = name, ConnectionStringName = managed ? DataSource.ManagedConnectionSentinel : "biz", AllowedObjects = ["dbo.v"], LockedBySuperAdmin = locked };
        if (managed)
        {
            var details = new ManagedConnectionDetails("db.example", 1433, "d", "u", "pw", ConnectionSslMode.Require);
            source.EncryptedConnection = key!.Protect(source.Id, JsonSerializer.Serialize(details, QueryBuilderJson.Options));
        }

        db.DataSources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    private static async Task SetSwitchAsync(HubApiHost env, Guid app, bool on)
    {
        await using var db = env.HubWideDb();
        (await db.Apps.FirstAsync(a => a.Id == app)).AppAdminsCanManageConnections = on;
        await db.SaveChangesAsync();
    }

    private static async Task<HubApiHost.Browser> AdminOf(HubApiHost env, Guid app)
    {
        var b = env.NewBrowser();
        await b.SignInAppAsync(app, HubRole.Admin, "boss");
        return b;
    }

    private static async Task<HubApiHost.Browser> SuperViewing(HubApiHost env, Guid? app = null)
    {
        var b = env.NewBrowser();
        await b.SignInSuperAsync(app);
        return b;
    }

    private static object CreateBody(string name) =>
        new { name, provider = "SqlServer", connection = new { host = "db.example", database = "d", username = "u", password = "p" } };

    // A valid body for every data-source write route, so a refusal can only come from the lock, never from validation.
    private static readonly Dictionary<string, object?> WriteBodies = new()
    {
        ["PUT /api/admin/data-sources/{id}"] = new { name = "renamed", description = "x" },
        ["PUT /api/admin/data-sources/{id}/active"] = new { isActive = false },
        ["PUT /api/admin/data-sources/{id}/lock"] = new { locked = false },
        ["POST /api/admin/data-sources/{id}/test"] = null,
        ["PUT /api/admin/data-sources/{id}/catalog-policy"] = new { catalogScope = "Views", allowedObjects = Array.Empty<string>() },
        ["PUT /api/admin/data-sources/{id}/data-scope"] = new { objects = Array.Empty<object>() },
        ["PUT /api/admin/data-sources/{id}/scope-keys"] = new { keys = new[] { "ProgramId" } },
        ["PUT /api/admin/data-sources/{id}/record-limit"] = new { maxRecords = 5 }
    };

    private static IEnumerable<(string Method, string Key, string Raw)> WriteRoutes(HubApiHost env) =>
        env.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/querybuilder/api/admin/data-sources/{id", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Where(m => m != "GET")
                .Select(m => (m, $"{m} {RouteParameter().Replace(e.RoutePattern.RawText!["/querybuilder".Length..], "{$1}")}", e.RoutePattern.RawText!)));

    private static string UrlFor(string raw, Guid id) => RouteParameter().Replace(raw, id.ToString());

    [MetadataSqlFact]
    public Task Every_admin_write_on_a_locked_source_is_403_for_app_admins() => HubApiHost.RunAsync(async env =>
    {
        // The switch is on, so a refusal can only be the lock.
        await SetSwitchAsync(env, env.AppA, true);
        var source = await AddSourceAsync(env, env.AppA, "locked-source", locked: true);
        var admin = await AdminOf(env, env.AppA);

        var routes = WriteRoutes(env).ToList();
        Assert.NotEmpty(routes);
        var missing = routes.Where(r => !WriteBodies.ContainsKey(r.Key)).Select(r => r.Key).ToList();
        Assert.True(missing.Count == 0, "Add a valid body for: " + string.Join("; ", missing));

        var failures = new List<string>();
        foreach (var (method, key, raw) in routes)
        {
            var response = await admin.SendAsync(new HttpMethod(method), UrlFor(raw, source.Id), WriteBodies[key]);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                failures.Add($"{key} -> {(int)response.StatusCode}");
            }
        }

        Assert.True(failures.Count == 0, "Not refused with 403: " + string.Join("; ", failures));

        // The unsaved-values test names the locked source as the one whose stored password it may reuse.
        var viaExisting = await admin.PostAsync($"{Api}/admin/data-sources/test",
            new { provider = "SqlServer", existingId = source.Id, connection = new { host = "127.0.0.1", database = "d", username = "u" } });
        Assert.Equal(HttpStatusCode.Forbidden, viaExisting.StatusCode);

        await using var db = env.HubWideDb();
        var after = await db.DataSources.AsNoTracking().FirstAsync(d => d.Id == source.Id);
        Assert.Equal("locked-source", after.Name);
        Assert.True(after.IsActive);
        Assert.Null(after.MaxRecords);

        // Reading stays open: the admin can see the source, only not change it.
        var detail = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Api}/admin/data-sources/{source.Id}"));
        Assert.True(detail.GetProperty("locked").GetBoolean());
        Assert.False(detail.GetProperty("canEdit").GetBoolean());
        Assert.False(detail.GetProperty("canManageConnection").GetBoolean());
        var list = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Api}/admin/data-sources"));
        var row = list.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == source.Id);
        Assert.True(row.GetProperty("locked").GetBoolean());
        Assert.False(row.GetProperty("canEdit").GetBoolean());
    });

    [MetadataSqlFact]
    public Task Super_admin_changes_locked_source() => HubApiHost.RunAsync(async env =>
    {
        var source = await AddSourceAsync(env, env.AppA, "locked-source", locked: true);
        var super = await SuperViewing(env, env.AppA);

        Assert.Equal(HttpStatusCode.NoContent, (await super.PutAsync($"{Api}/admin/data-sources/{source.Id}/record-limit", new { maxRecords = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await super.PutAsync($"{Api}/admin/data-sources/{source.Id}/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await super.PutAsync($"{Api}/admin/data-sources/{source.Id}/lock", new { locked = false })).StatusCode);

        await using var db = env.HubWideDb();
        var after = await db.DataSources.AsNoTracking().FirstAsync(d => d.Id == source.Id);
        Assert.Equal(7, after.MaxRecords);
        Assert.False(after.IsActive);
        Assert.False(after.LockedBySuperAdmin);

        var detail = await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Api}/admin/data-sources/{source.Id}"));
        Assert.True(detail.GetProperty("canEdit").GetBoolean());
        Assert.True(detail.GetProperty("canManageConnection").GetBoolean());
    });

    [MetadataSqlFact]
    public Task Super_admin_created_source_is_locked() => HubApiHost.RunAsync(async env =>
    {
        await SetSwitchAsync(env, env.AppA, true);
        var super = await SuperViewing(env, env.AppA);
        var created = await super.PostAsync($"{Api}/admin/data-sources", CreateBody("by-super"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await HubApiHost.Browser.JsonAsync(created);
        Assert.True(createdBody.GetProperty("locked").GetBoolean());

        var admin = await AdminOf(env, env.AppA);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync($"{Api}/admin/data-sources", CreateBody("by-admin"))).StatusCode);

        await using var db = env.HubWideDb();
        var sources = await db.DataSources.AsNoTracking().Where(d => d.AppId == env.AppA).ToListAsync();
        Assert.True(sources.Single(d => d.Name == "by-super").LockedBySuperAdmin);
        Assert.False(sources.Single(d => d.Name == "by-admin").LockedBySuperAdmin);
    });

    [MetadataSqlFact]
    public Task Lock_and_unlock_are_audited() => HubApiHost.RunAsync(async env =>
    {
        var source = await AddSourceAsync(env, env.AppA, "plain");
        var super = await SuperViewing(env, env.AppA);
        var admin = await AdminOf(env, env.AppA);

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync($"{Api}/admin/data-sources/{source.Id}/lock", new { locked = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await super.PutAsync($"{Api}/admin/data-sources/{source.Id}/lock", new { locked = true })).StatusCode);
        // Saying the same thing again changes nothing, so it leaves no second entry.
        Assert.Equal(HttpStatusCode.NoContent, (await super.PutAsync($"{Api}/admin/data-sources/{source.Id}/lock", new { locked = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await super.PutAsync($"{Api}/admin/data-sources/{source.Id}/lock", new { locked = false })).StatusCode);

        await using var db = env.HubWideDb();
        var entries = await db.AuditLogEntries.AsNoTracking().Where(a => a.Action == AuditAction.DataSourceLockChanged).OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(env.AppA, e.AppId));
        Assert.All(entries, e => Assert.Equal(source.Id, e.EntityId));
        Assert.Contains("Locked", entries[0].Summary);
        Assert.Contains("Unlocked", entries[1].Summary);
    });

    [MetadataSqlFact]
    public Task Lock_route_is_absent_in_embedded_mode() => HubApiHost.RunAsync(async env =>
    {
        var browser = env.NewBrowser();
        // No sessions exist in embedded mode; the handler itself answers 404 whoever asks.
        var response = await browser.PutAsync($"{Api}/admin/data-sources/{Guid.NewGuid()}/lock", new { locked = true });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }, hub: false);

    [MetadataSqlFact]
    public Task Connection_routes_need_the_app_switch_for_app_admins() => HubApiHost.RunAsync(async env =>
    {
        var source = await AddSourceAsync(env, env.AppA, "unlocked");
        var admin = await AdminOf(env, env.AppA);

        // Off (the default): add, edit, turn on or off and test are refused; the rest of an unlocked source stays open.
        var management = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Api}/admin/data-source-management"));
        Assert.False(management.GetProperty("canManageConnections").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"{Api}/admin/data-sources", CreateBody("n"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync($"{Api}/admin/data-sources/{source.Id}", new { name = "n" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync($"{Api}/admin/data-sources/{source.Id}/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"{Api}/admin/data-sources/{source.Id}/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"{Api}/admin/data-sources/test",
            new { provider = "SqlServer", connection = new { host = "127.0.0.1", port = 1, database = "d", username = "u", password = "p" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"{Api}/admin/data-sources/{source.Id}/record-limit", new { maxRecords = 9 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"{Api}/admin/data-sources/{source.Id}/data-scope", new { objects = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"{Api}/admin/data-sources/{source.Id}/catalog-policy", new { catalogScope = "Views", allowedObjects = Array.Empty<string>() })).StatusCode);

        var detail = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Api}/admin/data-sources/{source.Id}"));
        Assert.True(detail.GetProperty("canEdit").GetBoolean());
        Assert.False(detail.GetProperty("canManageConnection").GetBoolean());

        // On: the same admin may manage connections of unlocked sources.
        await SetSwitchAsync(env, env.AppA, true);
        management = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Api}/admin/data-source-management"));
        Assert.True(management.GetProperty("canManageConnections").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync($"{Api}/admin/data-sources", CreateBody("n"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"{Api}/admin/data-sources/{source.Id}", new { name = "renamed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"{Api}/admin/data-sources/{source.Id}/active", new { isActive = false })).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await admin.PostAsync($"{Api}/admin/data-sources/test",
            new { provider = "SqlServer", connection = new { host = "127.0.0.1", port = 1, database = "d", username = "u", password = "p" } })).StatusCode);

        // A super admin never needs the switch, and sees it as usable.
        await SetSwitchAsync(env, env.AppA, false);
        var super = await SuperViewing(env, env.AppA);
        management = await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Api}/admin/data-source-management"));
        Assert.True(management.GetProperty("canManageConnections").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await super.PostAsync($"{Api}/admin/data-sources", CreateBody("by-super"))).StatusCode);
    });

    [MetadataSqlFact]
    public Task Embedded_management_flag_is_unchanged() => HubApiHost.RunAsync(async env =>
    {
        var browser = env.NewBrowser();
        var json = await HubApiHost.Browser.JsonAsync(await browser.GetAsync($"{Api}/admin/data-source-management"));
        Assert.Equal(JsonValueKind.False, json.GetProperty("enabled").ValueKind);
        Assert.False(json.TryGetProperty("canManageConnections", out _));
    }, hub: false);


    private static async Task<HubApiHost.Browser> SuperAsync(HubApiHost env)
    {
        var b = env.NewBrowser();
        await b.SignInSuperAsync();
        return b;
    }

    private static string MoveUrl(Guid fromApp, Guid source) => $"{Api}/hub/apps/{fromApp}/data-sources/{source}/move";

    [MetadataSqlFact]
    public Task Move_refused_while_used() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);

        // A saved query, its chart on a dashboard tile, and a parameter dropdown (on a query of another source) that reads it.
        var used = await AddSourceAsync(env, env.AppA, "used");
        var other = await AddSourceAsync(env, env.AppA, "other");
        Guid queryId;
        await using (var db = env.HubWideDb())
        {
            var query = new SavedQuery { AppId = env.AppA, Name = "q", DataSourceId = used.Id, OwnerId = "m", DefinitionJson = "{}" };
            db.SavedQueries.Add(query);
            queryId = query.Id;
            var dropdownOwner = new SavedQuery
            {
                AppId = env.AppA, Name = "q2", DataSourceId = other.Id, OwnerId = "m",
                DefinitionJson = JsonSerializer.Serialize(new { parameters = new[] { new { name = "p", type = "QueryList", optionsQueryId = query.Id } } })
            };
            var dashboard = new Dashboard { AppId = env.AppA, Name = "d", OwnerId = "m" };
            db.SavedQueries.Add(dropdownOwner);
            db.Dashboards.Add(dashboard);
            await db.SaveChangesAsync();
            var viz = new QueryVisualization { AppId = env.AppA, SavedQueryId = query.Id, Name = "chart" };
            db.QueryVisualizations.Add(viz);
            await db.SaveChangesAsync();
            db.DashboardTiles.Add(new DashboardTile { AppId = env.AppA, DashboardId = dashboard.Id, Kind = DashboardTileKind.Chart, VisualizationId = viz.Id });
            await db.SaveChangesAsync();
        }

        var refused = await super.PostAsync(MoveUrl(env.AppA, used.Id), new { targetAppId = env.AppB });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var counts = (await HubApiHost.Browser.JsonAsync(refused)).GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("savedQueries").GetInt32());
        Assert.Equal(1, counts.GetProperty("dashboardTiles").GetInt32());
        Assert.Equal(1, counts.GetProperty("parameterDropdowns").GetInt32());
        Assert.Equal(0, counts.GetProperty("dataScopeRules").GetInt32());

        // A scope rule alone is a blocker too.
        var ruled = await AddSourceAsync(env, env.AppA, "ruled");
        await using (var db = env.HubWideDb())
        {
            db.DataScopeRules.Add(new DataScopeRule { AppId = env.AppA, DataSourceId = ruled.Id, ObjectName = "dbo.v", ScopeKey = "k", ColumnName = "c" });
            await db.SaveChangesAsync();
        }

        var ruleRefused = await super.PostAsync(MoveUrl(env.AppA, ruled.Id), new { targetAppId = env.AppB });
        Assert.Equal(HttpStatusCode.Conflict, ruleRefused.StatusCode);
        var ruleCounts = (await HubApiHost.Browser.JsonAsync(ruleRefused)).GetProperty("counts");
        Assert.Equal(1, ruleCounts.GetProperty("dataScopeRules").GetInt32());
        Assert.Equal(0, ruleCounts.GetProperty("savedQueries").GetInt32());

        // Nothing moved.
        await using var check = env.HubWideDb();
        Assert.Equal(env.AppA, (await check.DataSources.AsNoTracking().FirstAsync(d => d.Id == used.Id)).AppId);
        Assert.Equal(env.AppA, (await check.DataSources.AsNoTracking().FirstAsync(d => d.Id == ruled.Id)).AppId);
        Assert.Equal(0, await check.AuditLogEntries.CountAsync(a => a.Action == AuditAction.DataSourceMoved));
    });

    [MetadataSqlFact]
    public Task Move_checks_its_arguments() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        var source = await AddSourceAsync(env, env.AppA, "free");
        Guid disabled;
        await using (var db = env.HubWideDb())
        {
            var off = new App { Key = "app-off", Name = "Off", IsEnabled = false };
            db.Apps.Add(off);
            await db.SaveChangesAsync();
            disabled = off.Id;
        }

        // The source is in A, not B; and a missing source is the same 404.
        Assert.Equal(HttpStatusCode.NotFound, (await super.PostAsync(MoveUrl(env.AppB, source.Id), new { targetAppId = env.AppA })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await super.PostAsync(MoveUrl(env.AppA, Guid.NewGuid()), new { targetAppId = env.AppB })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(MoveUrl(env.AppA, source.Id), new { targetAppId = App.DefaultId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(MoveUrl(env.AppA, source.Id), new { targetAppId = Guid.Empty })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(MoveUrl(env.AppA, source.Id), new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(MoveUrl(env.AppA, source.Id), new { targetAppId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(MoveUrl(env.AppA, source.Id), new { targetAppId = disabled })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(MoveUrl(env.AppA, source.Id), new { targetAppId = env.AppA })).StatusCode);

        // An app admin, even of the source's own app, never reaches it.
        var admin = await AdminOf(env, env.AppA);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync(MoveUrl(env.AppA, source.Id), new { targetAppId = env.AppB })).StatusCode);

        await using var db2 = env.HubWideDb();
        Assert.Equal(env.AppA, (await db2.DataSources.AsNoTracking().FirstAsync(d => d.Id == source.Id)).AppId);
    });

    [MetadataSqlFact]
    public Task Move_unused_source_keeps_history_in_old_app() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        var source = await AddSourceAsync(env, env.AppA, "free");
        await using (var db = env.HubWideDb())
        {
            db.AuditLogEntries.Add(new AuditLogEntry { AppId = env.AppA, Action = AuditAction.DataSourceUpdated, EntityType = nameof(DataSource), EntityId = source.Id, DataSourceId = source.Id, Summary = "earlier history" });
            await db.SaveChangesAsync();
        }

        var cache = env.Host.Services.GetRequiredService<IMemoryCache>();
        cache.Set($"catalog:{source.Id}", "stale");
        cache.Set($"datascope:{source.Id}", "stale");

        var moved = await super.PostAsync(MoveUrl(env.AppA, source.Id), new { targetAppId = env.AppB });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        Assert.False(cache.TryGetValue($"catalog:{source.Id}", out _));
        Assert.False(cache.TryGetValue($"datascope:{source.Id}", out _));

        await using var check = env.HubWideDb();
        var row = await check.DataSources.AsNoTracking().FirstAsync(d => d.Id == source.Id);
        Assert.Equal(env.AppB, row.AppId);
        Assert.Equal("free", row.Name);

        var entries = await check.AuditLogEntries.AsNoTracking().Where(a => a.DataSourceId == source.Id).ToListAsync();
        Assert.Contains(entries, e => e.Summary == "earlier history" && e.AppId == env.AppA);
        var moves = entries.Where(e => e.Action == AuditAction.DataSourceMoved).ToList();
        Assert.Equal(2, moves.Count);
        Assert.Equal(new[] { env.AppA, env.AppB }.Order(), moves.Select(m => m.AppId!.Value).Order());

        // The old app no longer sees it; the new one does.
        var oldApp = await AdminOf(env, env.AppA);
        var newApp = await AdminOf(env, env.AppB);
        Assert.Equal(HttpStatusCode.NotFound, await oldApp.StatusAsync($"{Api}/admin/data-sources/{source.Id}"));
        Assert.Equal(HttpStatusCode.OK, await newApp.StatusAsync($"{Api}/admin/data-sources/{source.Id}"));
    });

    [MetadataSqlFact]
    public Task Reencrypt_in_hub_covers_every_app() => HubApiHost.RunAsync(async env =>
    {
        var oldKey = new AesGcmConnectionSecretProtector(new ConnectionSecretKey(OldKey));
        var a = await AddSourceAsync(env, env.AppA, "managed-a", managed: true, key: oldKey);
        var b = await AddSourceAsync(env, env.AppB, "managed-b", managed: true, key: oldKey);
        var super = await SuperAsync(env);

        var status = await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Api}/hub/encryption-key"));
        Assert.Equal(2, status.GetProperty("managedCount").GetInt32());
        Assert.Equal(2, status.GetProperty("onPreviousKey").GetInt32());
        Assert.True(status.GetProperty("canReencrypt").GetBoolean());

        var result = await HubApiHost.Browser.JsonAsync(await super.PostAsync($"{Api}/hub/encryption-key/reencrypt", null));
        Assert.Equal(2, result.GetProperty("reencrypted").GetInt32());

        await using var db = env.HubWideDb();
        var protector = env.Host.Services.GetRequiredService<QueryBuilder.Application.Abstractions.IConnectionSecretProtector>();
        foreach (var id in new[] { a.Id, b.Id })
        {
            var blob = (await db.DataSources.AsNoTracking().FirstAsync(d => d.Id == id)).EncryptedConnection!;
            Assert.Equal(SecretKeyMatch.Current, protector.Identify(id, blob));
        }

        var again = await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Api}/hub/encryption-key"));
        Assert.Equal(2, again.GetProperty("onCurrentKey").GetInt32());
    }, configure: o => o.DataSourceManagement.PreviousEncryptionKeys = [Convert.ToBase64String(OldKey)]);

    [MetadataSqlFact]
    public Task Admin_encryption_routes_404_in_hub() => HubApiHost.RunAsync(async env =>
    {
        var admin = await AdminOf(env, env.AppA);
        var super = await SuperViewing(env, env.AppA);
        foreach (var browser in new[] { admin, super })
        {
            Assert.Equal(HttpStatusCode.NotFound, await browser.StatusAsync($"{Api}/admin/encryption-key"));
            Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync($"{Api}/admin/encryption-key/reencrypt", null)).StatusCode);
        }

        // The hub routes are for super admins only.
        Assert.Equal(HttpStatusCode.Forbidden, await admin.StatusAsync($"{Api}/hub/encryption-key"));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"{Api}/hub/encryption-key/reencrypt", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await super.StatusAsync($"{Api}/hub/encryption-key"));
    });
}
