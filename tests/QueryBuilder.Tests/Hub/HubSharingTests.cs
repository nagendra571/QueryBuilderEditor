using System.Net;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class HubSharingTests
{
    private const string Api = "/querybuilder/api";

    private sealed record Seeded(Guid DataSource, Guid Query, Guid Dashboard);

    // dbo.AppUsers lives in the metadata database itself: the "biz" connection string points back at it.
    private static async Task<Seeded> SeedAsync(HubApiHost env, Guid app, string owner, bool directoryView = true)
    {
        await using var db = env.HubWideDb();
        if (directoryView)
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE VIEW dbo.AppUsers AS " +
                "SELECT CAST(N'mary' AS nvarchar(450)) AS Id, CAST(N'Mary Directory' AS nvarchar(200)) AS DisplayName, CAST(N'other@x' AS nvarchar(256)) AS Email " +
                "UNION ALL SELECT N'dir-1', N'Dir One', N'dir1@x' " +
                "UNION ALL SELECT N'dir-2', N'Dir Two', N'MARY@X' " +
                "UNION ALL SELECT N'zed2', N'Dir Three', NULL");
        }

        var source = new DataSource { AppId = app, Name = "src", ConnectionStringName = "biz", AllowedObjects = ["dbo.v"] };
        db.DataSources.Add(source);
        await db.SaveChangesAsync();
        var query = new SavedQuery { AppId = app, Name = "q", DataSourceId = source.Id, OwnerId = owner, OwnerName = owner, DefinitionJson = "{}" };
        var dashboard = new Dashboard { AppId = app, Name = "d", OwnerId = owner, OwnerName = owner };
        db.SavedQueries.Add(query);
        db.Dashboards.Add(dashboard);
        await db.SaveChangesAsync();
        var viz = new QueryVisualization { AppId = app, SavedQueryId = query.Id, Name = "chart" };
        db.QueryVisualizations.Add(viz);
        await db.SaveChangesAsync();
        db.DashboardTiles.Add(new DashboardTile { AppId = app, DashboardId = dashboard.Id, Kind = DashboardTileKind.Chart, VisualizationId = viz.Id });
        await db.SaveChangesAsync();
        return new Seeded(source.Id, query.Id, dashboard.Id);
    }

    private static async Task SeeUsersAsync(HubApiHost env)
    {
        await using var db = env.HubWideDb();
        db.SeenUsers.AddRange(
            new SeenUser { AppId = env.AppA, UserId = "mary", Name = "Mary", Email = "mary@x" },
            new SeenUser { AppId = env.AppA, UserId = "zed", Name = null, Email = "zed@x" },
            new SeenUser { AppId = env.AppA, UserId = "vera", Name = "Vera", Email = "vera@x" },
            new SeenUser { AppId = env.AppB, UserId = "bee", Name = "Bee", Email = "bee@x" });
        await db.SaveChangesAsync();
    }

    private static object ShareBody(string user, string level = "Viewer") =>
        new { sharedWithUserId = user, sharedWithUserEmail = user + "@x", accessLevel = level };

    [MetadataSqlFact]
    public Task Picker_merges_seen_users_and_directory() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA, "owner");
        await SeeUsersAsync(env);
        var author = env.NewBrowser();
        await author.SignInAppAsync(env.AppA, HubRole.Author, "owner");

        var json = await HubApiHost.Browser.JsonAsync(await author.GetAsync($"{Api}/data-sources/{s.DataSource}/app-users"));
        Assert.True(json.GetProperty("available").GetBoolean());
        var users = json.GetProperty("users").EnumerateArray()
            .ToDictionary(u => u.GetProperty("id").GetString()!, u => (Source: u.GetProperty("source").GetString(), Name: u.GetProperty("displayName").GetString()));

        // mary (id) and dir-2 (email, ignoring case) duplicate a launched user; bee belongs to the other app.
        Assert.Equal(["dir-1", "mary", "vera", "zed", "zed2"], users.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(("launch", "Mary"), users["mary"]);
        Assert.Equal(("launch", "zed@x"), users["zed"]);
        Assert.Equal("directory", users["dir-1"].Source);
        Assert.Equal("directory", users["zed2"].Source);
    });

    [MetadataSqlFact]
    public Task Picker_works_from_seen_users_alone_when_there_is_no_directory_view() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA, "owner", directoryView: false);
        await SeeUsersAsync(env);
        var author = env.NewBrowser();
        await author.SignInAppAsync(env.AppA, HubRole.Author, "owner");

        var json = await HubApiHost.Browser.JsonAsync(await author.GetAsync($"{Api}/data-sources/{s.DataSource}/app-users"));
        Assert.True(json.GetProperty("available").GetBoolean());
        Assert.Equal(["mary", "vera", "zed"], json.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
    });

    [MetadataSqlFact]
    public Task People_lists_the_apps_seen_users_only() => HubApiHost.RunAsync(async env =>
    {
        await SeedAsync(env, env.AppA, "owner");
        await SeeUsersAsync(env);
        var author = env.NewBrowser();
        await author.SignInAppAsync(env.AppA, HubRole.Author, "owner");

        var json = await HubApiHost.Browser.JsonAsync(await author.GetAsync($"{Api}/people"));
        Assert.Equal(["mary", "vera", "zed"], json.EnumerateArray().Select(u => u.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(json.EnumerateArray(), u => Assert.Equal("launch", u.GetProperty("source").GetString()));

        var viewer = env.NewBrowser();
        await viewer.SignInAppAsync(env.AppA, HubRole.Viewer, "vera");
        Assert.Equal(HttpStatusCode.Forbidden, await viewer.StatusAsync($"{Api}/people"));
    });

    [MetadataSqlFact]
    public Task People_is_404_in_embedded_mode() => HubApiHost.RunAsync(async env =>
    {
        Assert.Equal(HttpStatusCode.NotFound, await env.NewBrowser().StatusAsync($"{Api}/people"));
    }, hub: false);

    [MetadataSqlFact]
    public Task Share_to_unknown_user_is_400_in_hub_mode() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA, "owner");
        await SeeUsersAsync(env);
        var owner = env.NewBrowser();
        await owner.SignInAppAsync(env.AppA, HubRole.Author, "owner");

        foreach (var who in new[] { "stranger", "bee", "MARY" })
        {
            var q = await owner.PostAsync($"{Api}/queries/{s.Query}/shares", ShareBody(who));
            Assert.Equal(HttpStatusCode.BadRequest, q.StatusCode);
            Assert.Equal("Choose someone from the list.", (await HubApiHost.Browser.JsonAsync(q)).GetProperty("detail").GetString());
            var d = await owner.PostAsync($"{Api}/dashboards/{s.Dashboard}/shares", ShareBody(who));
            Assert.Equal(HttpStatusCode.BadRequest, d.StatusCode);
        }

        await using var db = env.HubWideDb();
        Assert.Empty(await db.QueryShares.ToListAsync());
        Assert.Empty(await db.DashboardShares.ToListAsync());
    });

    [MetadataSqlFact]
    public Task Share_to_seen_user_works() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA, "owner");
        await SeeUsersAsync(env);
        var owner = env.NewBrowser();
        await owner.SignInAppAsync(env.AppA, HubRole.Author, "owner");

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"{Api}/queries/{s.Query}/shares", ShareBody("mary", "Editor"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"{Api}/dashboards/{s.Dashboard}/shares", ShareBody("zed"))).StatusCode);

        // A person only the AppUsers view lists is known too, for the query's own data source and for a dashboard behind it.
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"{Api}/queries/{s.Query}/shares", ShareBody("dir-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"{Api}/dashboards/{s.Dashboard}/shares", ShareBody("dir-1"))).StatusCode);
    });

    [MetadataSqlFact]
    public Task Embedded_share_rules_unchanged() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, App.DefaultId, "Admin", directoryView: false);
        var browser = env.NewBrowser();
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync($"{Api}/queries/{s.Query}/shares", ShareBody("stranger"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync($"{Api}/dashboards/{s.Dashboard}/shares", ShareBody("stranger"))).StatusCode);

        var users = await HubApiHost.Browser.JsonAsync(await browser.GetAsync($"{Api}/data-sources/{s.DataSource}/app-users"));
        Assert.False(users.GetProperty("available").GetBoolean());
        Assert.Equal(["available", "users"], users.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }, hub: false, configure: o => o.ActorResolver = _ => "Admin");

    [MetadataSqlFact]
    public Task Sharing_a_query_with_a_viewer_grants_no_query_access() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA, "owner");
        await SeeUsersAsync(env);
        var owner = env.NewBrowser();
        await owner.SignInAppAsync(env.AppA, HubRole.Author, "owner");
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"{Api}/queries/{s.Query}/shares", ShareBody("vera", "Editor"))).StatusCode);

        var viewer = env.NewBrowser();
        await viewer.SignInAppAsync(env.AppA, HubRole.Viewer, "vera");
        Assert.Equal(HttpStatusCode.Forbidden, await viewer.StatusAsync($"{Api}/queries/{s.Query}"));
        Assert.Equal(HttpStatusCode.Forbidden, await viewer.StatusAsync($"{Api}/queries"));
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"{Api}/queries/{s.Query}/shares", ShareBody("mary"))).StatusCode);
    });
}
