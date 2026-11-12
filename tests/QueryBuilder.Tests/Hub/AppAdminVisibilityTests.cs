using System.Net;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class AppAdminVisibilityTests
{
    private const string Api = "/querybuilder/api";

    private sealed record Seeded(Guid Query, Guid Visualization, Guid Dashboard, Guid Share, Guid DashboardShare, Guid Other);

    // Mary owns two queries (one with a chart and a share to bob), a dashboard shared to bob, and an audit entry on the first query.
    private static async Task<Seeded> SeedAsync(HubApiHost env, Guid app)
    {
        await using var db = env.HubWideDb();
        var source = new DataSource { AppId = app, Name = "src", ConnectionStringName = "biz", AllowedObjects = ["dbo.v"] };
        db.DataSources.Add(source);
        await db.SaveChangesAsync();
        var query = new SavedQuery { AppId = app, Name = "marys-query", DataSourceId = source.Id, OwnerId = "mary", OwnerName = "Mary", DefinitionJson = "{}" };
        var other = new SavedQuery { AppId = app, Name = "marys-private", DataSourceId = source.Id, OwnerId = "mary", OwnerName = "Mary", DefinitionJson = "{}" };
        var dashboard = new Dashboard { AppId = app, Name = "marys-dashboard", OwnerId = "mary", OwnerName = "Mary" };
        db.SavedQueries.AddRange(query, other);
        db.Dashboards.Add(dashboard);
        await db.SaveChangesAsync();
        var viz = new QueryVisualization { AppId = app, SavedQueryId = query.Id, Name = "chart" };
        var share = new QueryShare { AppId = app, SavedQueryId = query.Id, SharedWithUserId = "bob", SharedWithUserEmail = "bob@x", AccessLevel = QueryAccessLevel.Viewer };
        var dashShare = new DashboardShare { AppId = app, DashboardId = dashboard.Id, SharedWithUserId = "bob", SharedWithUserEmail = "bob@x", AccessLevel = QueryAccessLevel.Viewer };
        db.QueryVisualizations.Add(viz);
        db.QueryShares.Add(share);
        db.DashboardShares.Add(dashShare);
        db.AuditLogEntries.Add(new AuditLogEntry { AppId = app, Actor = "mary", EntityType = "SavedQuery", EntityId = query.Id, Summary = "mary-ran-it" });
        await db.SaveChangesAsync();
        return new Seeded(query.Id, viz.Id, dashboard.Id, share.Id, dashShare.Id, other.Id);
    }

    private static async Task<List<Guid>> IdsAsync(HubApiHost.Browser b, string url)
    {
        var json = await HubApiHost.Browser.JsonAsync(await b.GetAsync(url));
        return json.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
    }

    [MetadataSqlFact]
    public Task App_admin_lists_opens_edits_disables_and_deletes_every_query_in_the_app() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "boss");

        var listed = await IdsAsync(admin, $"{Api}/queries");
        Assert.Contains(s.Query, listed);
        Assert.Contains(s.Other, listed);

        var detail = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Api}/queries/{s.Query}"));
        Assert.False(detail.GetProperty("isOwnedByCurrentUser").GetBoolean());
        Assert.Equal("owner", detail.GetProperty("myAccessLevel").GetString());

        Assert.Equal(HttpStatusCode.OK, await admin.StatusAsync($"{Api}/queries/{s.Query}/visualizations"));
        Assert.Contains(s.Dashboard, await IdsAsync(admin, $"{Api}/dashboards"));
        var dashboard = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Api}/dashboards/{s.Dashboard}"));
        var rename = await admin.PutAsync($"{Api}/dashboards/{s.Dashboard}", new { name = "renamed", description = "", version = dashboard.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        Assert.True((await admin.PostAsync($"{Api}/queries/{s.Query}/disable", null)).IsSuccessStatusCode);
        await using (var db = env.HubWideDb())
        {
            Assert.True((await db.SavedQueries.SingleAsync(q => q.Id == s.Query)).IsDisabled);
        }

        Assert.True((await admin.SendAsync(HttpMethod.Delete, $"{Api}/queries/{s.Query}")).IsSuccessStatusCode);
        Assert.True((await admin.SendAsync(HttpMethod.Delete, $"{Api}/dashboards/{s.Dashboard}")).IsSuccessStatusCode);
        await using (var db = env.HubWideDb())
        {
            Assert.False(await db.SavedQueries.AnyAsync(q => q.Id == s.Query));
            Assert.False(await db.Dashboards.AnyAsync(d => d.Id == s.Dashboard));
        }
    });

    [MetadataSqlFact]
    public Task App_admin_manages_shares_on_others_queries() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "boss");
        await using (var seeding = env.HubWideDb())
        {
            // A share target must be a known person of the app (hub mode).
            seeding.SeenUsers.Add(new SeenUser { AppId = env.AppA, UserId = "dave", Name = "Dave", Email = "dave@x" });
            await seeding.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, await admin.StatusAsync($"{Api}/queries/{s.Query}/shares"));
        Assert.Equal(HttpStatusCode.OK, await admin.StatusAsync($"{Api}/dashboards/{s.Dashboard}/shares"));
        var added = await admin.PostAsync($"{Api}/queries/{s.Query}/shares", new { sharedWithUserId = "dave", sharedWithUserEmail = "dave@x", accessLevel = "Viewer" });
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());
        Assert.True((await admin.SendAsync(HttpMethod.Delete, $"{Api}/queries/{s.Query}/shares/{s.Share}")).IsSuccessStatusCode);
        Assert.True((await admin.SendAsync(HttpMethod.Delete, $"{Api}/dashboards/{s.Dashboard}/shares/{s.DashboardShare}")).IsSuccessStatusCode);
        await using var db = env.HubWideDb();
        Assert.False(await db.QueryShares.AnyAsync(x => x.Id == s.Share));
        Assert.True(await db.QueryShares.AnyAsync(x => x.SharedWithUserId == "dave"));
    });

    [MetadataSqlFact]
    public Task Super_admin_viewing_A_opens_As_query() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var super = env.NewBrowser();
        await super.SignInSuperAsync(env.AppA);

        Assert.Equal(HttpStatusCode.OK, await super.StatusAsync($"{Api}/queries/{s.Query}"));
        Assert.Contains(s.Query, await IdsAsync(super, $"{Api}/queries"));
        Assert.Equal(HttpStatusCode.OK, await super.StatusAsync($"{Api}/dashboards/{s.Dashboard}"));
    });

    [MetadataSqlFact]
    public Task Author_still_sees_only_own_and_shared() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var bob = env.NewBrowser();
        await bob.SignInAppAsync(env.AppA, HubRole.Author, "bob");
        Assert.Equal([s.Query], await IdsAsync(bob, $"{Api}/queries"));
        Assert.Equal(HttpStatusCode.Forbidden, await bob.StatusAsync($"{Api}/queries/{s.Other}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsync($"{Api}/queries/{s.Query}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.SendAsync(HttpMethod.Delete, $"{Api}/queries/{s.Query}")).StatusCode);

        var carol = env.NewBrowser();
        await carol.SignInAppAsync(env.AppA, HubRole.Author, "carol");
        Assert.Empty(await IdsAsync(carol, $"{Api}/queries"));
        Assert.Empty(await IdsAsync(carol, $"{Api}/dashboards"));
        Assert.Equal(HttpStatusCode.Forbidden, await carol.StatusAsync($"{Api}/queries/{s.Query}"));
        Assert.Equal(HttpStatusCode.Forbidden, await carol.StatusAsync($"{Api}/dashboards/{s.Dashboard}"));
    });

    [MetadataSqlFact]
    public Task Embedded_mode_never_sees_whole_app() => HubApiHost.RunAsync(async env =>
    {
        Guid own, others;
        await using (var db = env.HubWideDb())
        {
            var source = new DataSource { AppId = App.DefaultId, Name = "src", ConnectionStringName = "biz", AllowedObjects = ["dbo.v"] };
            db.DataSources.Add(source);
            await db.SaveChangesAsync();
            var mine = new SavedQuery { AppId = App.DefaultId, Name = "mine", DataSourceId = source.Id, OwnerId = "Admin", DefinitionJson = "{}" };
            var theirs = new SavedQuery { AppId = App.DefaultId, Name = "theirs", DataSourceId = source.Id, OwnerId = "mary", DefinitionJson = "{}" };
            db.SavedQueries.AddRange(mine, theirs);
            await db.SaveChangesAsync();
            own = mine.Id;
            others = theirs.Id;
        }

        var browser = env.NewBrowser();
        Assert.Equal([own], await IdsAsync(browser, $"{Api}/queries"));
        Assert.Equal(HttpStatusCode.Forbidden, await browser.StatusAsync($"{Api}/queries/{others}"));
    }, hub: false, configure: o => o.ActorResolver = _ => "Admin");

    [MetadataSqlFact]
    public Task App_admin_sees_audit_of_others_queries() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "boss");
        var body = await (await admin.GetAsync($"{Api}/audit?entityType=SavedQuery&entityId={s.Query}")).Content.ReadAsStringAsync();
        Assert.Contains("mary-ran-it", body);

        var carol = env.NewBrowser();
        await carol.SignInAppAsync(env.AppA, HubRole.Author, "carol");
        Assert.Equal(HttpStatusCode.Forbidden, await carol.StatusAsync($"{Api}/audit?entityType=SavedQuery&entityId={s.Query}"));
    });
}
