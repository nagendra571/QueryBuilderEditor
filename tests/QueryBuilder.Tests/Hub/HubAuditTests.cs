using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class HubAuditTests
{
    private const string Mount = "/querybuilder";

    private static async Task<HubApiHost.Browser> SuperAsync(HubApiHost env, Guid? viewing = null)
    {
        var browser = env.NewBrowser();
        await browser.SignInSuperMatchedAsync("ObjectId", "admin-oid");
        if (viewing is { } app)
        {
            Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync($"{Mount}/api/hub/view-app", new { appId = app })).StatusCode);
        }

        return browser;
    }

    private static async Task SeedAuditAsync(HubApiHost env, Guid app, string actor, string summary, string? details = null, AuditAction action = AuditAction.SuperAdminViewedApp)
    {
        await using var db = env.HubWideDb();
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            AppId = app, Actor = actor, Action = action, EntityType = nameof(App), Summary = summary, DetailsJson = details, TimestampUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [MetadataSqlFact]
    public Task View_app_is_audited_inside_the_target_app() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        Assert.Equal(HttpStatusCode.OK, (await super.PostAsync($"{Mount}/api/hub/view-app", new { appId = env.AppA })).StatusCode);

        await using var db = env.HubWideDb();
        var entry = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.Action == AuditAction.SuperAdminViewedApp);
        Assert.Equal(env.AppA, entry.AppId);
        Assert.Equal("super:admin-oid", entry.Actor);
        Assert.Equal(env.AppA, entry.EntityId);

        // Leaving an app for the hub overview is not a view of any app.
        Assert.Equal(HttpStatusCode.OK, (await super.PostAsync($"{Mount}/api/hub/view-app", new { })).StatusCode);
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.SuperAdminViewedApp));

        // Another app's admin never sees A's entry.
        var adminB = env.NewBrowser();
        await adminB.SignInAppAsync(env.AppB, HubRole.Admin, "bob");
        var listB = await HubApiHost.Browser.JsonAsync(await adminB.GetAsync($"{Mount}/api/audit"));
        Assert.DoesNotContain(listB.EnumerateArray(), e => e.GetProperty("action").GetString() == "SuperAdminViewedApp");
    });

    [MetadataSqlFact]
    public Task Hub_audit_lists_all_apps_for_super_admin_only() => HubApiHost.RunAsync(async env =>
    {
        await SeedAuditAsync(env, env.AppA, "super:admin-oid", "viewed A");
        await SeedAuditAsync(env, env.AppB, "super:admin-oid", "viewed B");
        await using (var db = env.HubWideDb())
        {
            db.AuditLogEntries.Add(new AuditLogEntry { Actor = "anonymous", Action = AuditAction.SuperAdminSignInRefused, EntityType = "SuperAdmin", Summary = "refused", TimestampUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var super = await SuperAsync(env);
        var all = (await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Mount}/api/hub/audit"))).EnumerateArray().ToList();
        Assert.Contains(all, e => e.GetProperty("appKey").GetString() == "app-a" && e.GetProperty("summary").GetString() == "viewed A");
        Assert.Contains(all, e => e.GetProperty("appKey").GetString() == "app-b");
        Assert.Contains(all, e => e.GetProperty("appKey").ValueKind == JsonValueKind.Null && e.GetProperty("summary").GetString() == "refused");
        Assert.Contains(all, e => e.GetProperty("actor").GetString() == "super:admin-oid");

        var onlyA = (await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Mount}/api/hub/audit?appId={env.AppA}"))).EnumerateArray().ToList();
        Assert.NotEmpty(onlyA);
        Assert.All(onlyA, e => Assert.Equal("app-a", e.GetProperty("appKey").GetString()));

        var byAction = (await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Mount}/api/hub/audit?action=SuperAdminSignInRefused"))).EnumerateArray().ToList();
        Assert.Single(byAction);

        var page = (await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Mount}/api/hub/audit?take=1"))).EnumerateArray().ToList();
        Assert.Single(page);
        var newest = page[0].GetProperty("timestampUtc").GetDateTimeOffset();
        var older = (await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Mount}/api/hub/audit?take=200&before={Uri.EscapeDataString(newest.ToString("O"))}"))).EnumerateArray().ToList();
        Assert.NotEmpty(older);
        Assert.All(older, e => Assert.True(e.GetProperty("timestampUtc").GetDateTimeOffset() < newest));

        Assert.Equal(HttpStatusCode.BadRequest, (await super.GetAsync($"{Mount}/api/hub/audit?take=201")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.GetAsync($"{Mount}/api/hub/audit?action=NoSuchAction")).StatusCode);

        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, await admin.StatusAsync($"{Mount}/api/hub/audit"));
    });

    [MetadataSqlFact]
    public Task App_admin_sees_super_admin_actor_masked() => HubApiHost.RunAsync(async env =>
    {
        await SeedAuditAsync(env, env.AppA, "super:admin-oid", "viewed A", "{\"oid\":\"admin-oid\",\"kept\":\"x\"}");

        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin);
        var seen = (await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Mount}/api/audit"))).EnumerateArray()
            .Single(e => e.GetProperty("summary").GetString() == "viewed A");
        Assert.Equal("Super admin", seen.GetProperty("actor").GetString());
        var details = seen.GetProperty("detailsJson").GetString()!;
        Assert.DoesNotContain("admin-oid", details);
        Assert.Contains("\"kept\"", details);

        var super = await SuperAsync(env, env.AppA);
        var raw = (await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Mount}/api/audit"))).EnumerateArray()
            .First(e => e.GetProperty("summary").GetString() == "viewed A");
        Assert.Equal("super:admin-oid", raw.GetProperty("actor").GetString());
        Assert.Contains("admin-oid", raw.GetProperty("detailsJson").GetString());
    });

    [MetadataSqlFact]
    public Task App_admin_sees_super_admin_owner_masked_on_queries_and_dashboards() => HubApiHost.RunAsync(async env =>
    {
        Guid queryId, dashboardId;
        await using (var db = env.HubWideDb())
        {
            var source = new DataSource { AppId = env.AppA, Name = "src", ConnectionStringName = "biz", AllowedObjects = ["dbo.v"] };
            db.DataSources.Add(source);
            await db.SaveChangesAsync();
            var query = new SavedQuery { AppId = env.AppA, Name = "staff-query", DataSourceId = source.Id, OwnerId = "super:admin-oid", OwnerName = "admin-oid", DefinitionJson = "{}" };
            var dashboard = new Dashboard { AppId = env.AppA, Name = "staff-dashboard", OwnerId = "super:admin-oid", OwnerName = "admin-oid" };
            db.SavedQueries.Add(query);
            db.Dashboards.Add(dashboard);
            await db.SaveChangesAsync();
            queryId = query.Id;
            dashboardId = dashboard.Id;
        }

        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin);
        foreach (var url in new[] { $"{Mount}/api/queries", $"{Mount}/api/dashboards" })
        {
            var row = (await HubApiHost.Browser.JsonAsync(await admin.GetAsync(url))).EnumerateArray().Single();
            Assert.Equal("Super admin", row.GetProperty("ownerId").GetString());
            Assert.Equal("Super admin", row.GetProperty("ownerName").GetString());
        }

        var detail = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Mount}/api/queries/{queryId}"));
        Assert.Equal("Super admin", detail.GetProperty("ownerId").GetString());
        var dash = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Mount}/api/dashboards/{dashboardId}"));
        Assert.Equal("Super admin", dash.GetProperty("ownerName").GetString());

        var super = await SuperAsync(env, env.AppA);
        var raw = (await HubApiHost.Browser.JsonAsync(await super.GetAsync($"{Mount}/api/queries"))).EnumerateArray().Single();
        Assert.Equal("super:admin-oid", raw.GetProperty("ownerId").GetString());
        Assert.Equal("admin-oid", raw.GetProperty("ownerName").GetString());
    });

    [MetadataSqlFact]
    public Task App_admin_lists_app_wide_audit() => HubApiHost.RunAsync(async env =>
    {
        await SeedAuditAsync(env, env.AppA, "someone-else", "other person did a thing", action: AuditAction.QueryRun);
        await SeedAuditAsync(env, env.AppA, "mary", "mary did a thing", action: AuditAction.QueryRun);
        await SeedAuditAsync(env, env.AppB, "someone-else", "in app B", action: AuditAction.QueryRun);

        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "adam");
        var summaries = (await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Mount}/api/audit"))).EnumerateArray()
            .Select(e => e.GetProperty("summary").GetString()).ToList();
        Assert.Contains("other person did a thing", summaries);
        Assert.Contains("mary did a thing", summaries);
        Assert.DoesNotContain("in app B", summaries);

        // An author keeps today's own-actions-only list.
        var author = env.NewBrowser();
        await author.SignInAppAsync(env.AppA, HubRole.Author, "mary");
        var own = (await HubApiHost.Browser.JsonAsync(await author.GetAsync($"{Mount}/api/audit"))).EnumerateArray()
            .Select(e => e.GetProperty("summary").GetString()).ToList();
        Assert.Contains("mary did a thing", own);
        Assert.DoesNotContain("other person did a thing", own);
    });
}
