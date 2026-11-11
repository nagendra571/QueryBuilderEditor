using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor.Hub;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public partial class ViewerAndPublishingTests
{
    private const string Api = "/querybuilder/api";

    [GeneratedRegex(@"\{(\w+)(:guid)?\}")]
    private static partial Regex RouteParameter();

    private sealed record Seeded(Guid Published, Guid Plain, Guid SharedToViewer, Guid ViewersOwn, Guid Query, Guid Visualization);

    private static async Task<Seeded> SeedAsync(HubApiHost env, Guid app)
    {
        await using var db = env.HubWideDb();
        var source = new DataSource { AppId = app, Name = "src", ConnectionStringName = "biz", AllowedObjects = ["dbo.v"] };
        db.DataSources.Add(source);
        await db.SaveChangesAsync();
        var query = new SavedQuery { AppId = app, Name = "q", DataSourceId = source.Id, OwnerId = "mary", OwnerName = "Mary", DefinitionJson = "{}" };
        var published = new Dashboard { AppId = app, Name = "published", OwnerId = "mary", OwnerName = "Mary", PublishedToApp = true, PublishedBy = "boss", PublishedAtUtc = DateTimeOffset.UtcNow };
        var plain = new Dashboard { AppId = app, Name = "plain", OwnerId = "mary", OwnerName = "Mary" };
        var shared = new Dashboard { AppId = app, Name = "shared", OwnerId = "mary", OwnerName = "Mary" };
        var own = new Dashboard { AppId = app, Name = "viewers-own", OwnerId = "vera", OwnerName = "Vera" };
        db.SavedQueries.Add(query);
        db.Dashboards.AddRange(published, plain, shared, own);
        await db.SaveChangesAsync();
        var viz = new QueryVisualization { AppId = app, SavedQueryId = query.Id, Name = "chart" };
        db.QueryVisualizations.Add(viz);
        db.DashboardShares.Add(new DashboardShare { AppId = app, DashboardId = shared.Id, SharedWithUserId = "vera", SharedWithUserEmail = "vera@x", AccessLevel = QueryAccessLevel.Editor });
        db.QueryShares.Add(new QueryShare { AppId = app, SavedQueryId = query.Id, SharedWithUserId = "vera", SharedWithUserEmail = "vera@x", AccessLevel = QueryAccessLevel.Editor });
        await db.SaveChangesAsync();
        return new Seeded(published.Id, plain.Id, shared.Id, own.Id, query.Id, viz.Id);
    }

    private static async Task<HubApiHost.Browser> SignedIn(HubApiHost env, Guid app, HubRole role)
    {
        var b = env.NewBrowser();
        await b.SignInAppAsync(app, role, "u-" + role);
        return b;
    }

    private static async Task<List<Guid>> IdsAsync(HubApiHost.Browser b, string url)
    {
        var json = await HubApiHost.Browser.JsonAsync(await b.GetAsync(url));
        return json.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
    }

    [MetadataSqlFact]
    public Task Viewer_gets_403_on_every_endpoint_without_the_marker() => HubApiHost.RunAsync(async env =>
    {
        var viewer = env.NewBrowser();
        await viewer.SignInAppAsync(env.AppA, HubRole.Viewer, "vera");

        var endpoints = env.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/querybuilder/api", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(endpoints);

        var allowed = new List<string>();
        var failures = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var raw = endpoint.RoutePattern.RawText!;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            var marked = endpoint.Metadata.GetMetadata<ViewerAllowedMetadata>() is not null;
            var url = RouteParameter().Replace(raw, m => m.Groups[1].Value == "name" || m.Groups[1].Value == "kid" ? "p" : Guid.NewGuid().ToString()) + "?userId=x";
            foreach (var method in methods)
            {
                if (marked)
                {
                    allowed.Add($"{method} {RouteParameter().Replace(raw["/querybuilder".Length..], "{$1}").TrimEnd('/')}");
                    continue;
                }

                var response = await viewer.SendAsync(new HttpMethod(method), url, method is "GET" or "DELETE" ? null : new { });
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    failures.Add($"{method} {raw} -> {(int)response.StatusCode}");
                }
            }
        }

        Assert.True(failures.Count == 0, "Viewer was not refused with 403: " + string.Join("; ", failures));
        Assert.Equal(
            new[]
            {
                "GET /api/dashboards",
                "GET /api/dashboards/{id}",
                "GET /api/me",
                "GET /api/queries/{id}/parameters/{name}/options",
                "POST /api/queries/{queryId}/visualizations/{visualizationId}/data"
            }.Order(StringComparer.Ordinal),
            allowed.Order(StringComparer.Ordinal));
    });

    [MetadataSqlFact]
    public Task Viewer_lists_published_and_shared_dashboards_only() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var other = new Dashboard { AppId = env.AppB, Name = "b-published", OwnerId = "x", PublishedToApp = true };
        await using (var db = env.HubWideDb())
        {
            db.Dashboards.Add(other);
            await db.SaveChangesAsync();
        }

        var viewer = env.NewBrowser();
        await viewer.SignInAppAsync(env.AppA, HubRole.Viewer, "vera");
        var ids = await IdsAsync(viewer, $"{Api}/dashboards");
        Assert.Equivalent(new[] { s.Published, s.SharedToViewer, s.ViewersOwn }, ids);
        Assert.DoesNotContain(s.Plain, ids);
        Assert.DoesNotContain(other.Id, ids);
        Assert.Equal(HttpStatusCode.Forbidden, await viewer.StatusAsync($"{Api}/dashboards/{s.Plain}"));
        Assert.Equal(HttpStatusCode.OK, await viewer.StatusAsync($"{Api}/dashboards/{s.Published}"));
    });

    [MetadataSqlFact]
    public Task Viewer_with_a_query_share_cannot_open_queries_or_the_list() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var viewer = env.NewBrowser();
        await viewer.SignInAppAsync(env.AppA, HubRole.Viewer, "vera");
        Assert.Equal(HttpStatusCode.Forbidden, await viewer.StatusAsync($"{Api}/queries/{s.Query}"));
        Assert.Equal(HttpStatusCode.Forbidden, await viewer.StatusAsync($"{Api}/queries"));
    });

    [MetadataSqlFact]
    public Task A_viewers_own_or_edit_shared_dashboard_reports_viewer_and_refuses_edits() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var viewer = env.NewBrowser();
        await viewer.SignInAppAsync(env.AppA, HubRole.Viewer, "vera");
        foreach (var id in new[] { s.ViewersOwn, s.SharedToViewer })
        {
            var detail = await HubApiHost.Browser.JsonAsync(await viewer.GetAsync($"{Api}/dashboards/{id}"));
            Assert.Equal("viewer", detail.GetProperty("accessLevel").GetString());
            var put = await viewer.PutAsync($"{Api}/dashboards/{id}/tiles", new { version = detail.GetProperty("version").GetInt32(), tiles = Array.Empty<object>() });
            Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        }

        var list = await HubApiHost.Browser.JsonAsync(await viewer.GetAsync($"{Api}/dashboards"));
        Assert.All(list.EnumerateArray(), d => Assert.Equal("viewer", d.GetProperty("accessLevel").GetString()));
    });

    [MetadataSqlFact]
    public Task Author_sees_published_dashboards_read_only() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var author = env.NewBrowser();
        await author.SignInAppAsync(env.AppA, HubRole.Author, "bob");
        var ids = await IdsAsync(author, $"{Api}/dashboards");
        Assert.Equal([s.Published], ids);
        var detail = await HubApiHost.Browser.JsonAsync(await author.GetAsync($"{Api}/dashboards/{s.Published}"));
        Assert.Equal("viewer", detail.GetProperty("accessLevel").GetString());
        Assert.True(detail.GetProperty("published").GetBoolean());
        var put = await author.PutAsync($"{Api}/dashboards/{s.Published}/tiles", new { version = detail.GetProperty("version").GetInt32(), tiles = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    });

    [MetadataSqlFact]
    public Task Only_admins_and_super_admins_publish() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var url = $"{Api}/dashboards/{s.Plain}/published";

        var author = env.NewBrowser();
        await author.SignInAppAsync(env.AppA, HubRole.Author, "mary");
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PutAsync(url, new { published = true })).StatusCode);
        var viewer = env.NewBrowser();
        await viewer.SignInAppAsync(env.AppA, HubRole.Viewer, "vera");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsync(url, new { published = true })).StatusCode);

        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "boss");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync(url, new { published = true })).StatusCode);
        await using (var db = env.HubWideDb())
        {
            var row = await db.Dashboards.SingleAsync(d => d.Id == s.Plain);
            Assert.True(row.PublishedToApp);
            Assert.Equal("boss", row.PublishedBy);
            Assert.NotNull(row.PublishedAtUtc);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync(url, new { published = false })).StatusCode);
        var super = env.NewBrowser();
        await super.SignInSuperAsync(env.AppA);
        Assert.Equal(HttpStatusCode.NoContent, (await super.PutAsync(url, new { published = true })).StatusCode);
        await using (var db = env.HubWideDb())
        {
            Assert.True((await db.Dashboards.SingleAsync(d => d.Id == s.Plain)).PublishedToApp);
            Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.DashboardUnpublished && a.EntityId == s.Plain));
            Assert.Equal(2, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.DashboardPublished && a.EntityId == s.Plain));
        }

        var b = await SignedIn(env, env.AppB, HubRole.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsync(url, new { published = false })).StatusCode);
    });

    [MetadataSqlFact]
    public Task Unchanged_publish_writes_no_audit() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env, env.AppA);
        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "boss");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"{Api}/dashboards/{s.Published}/published", new { published = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"{Api}/dashboards/{s.Plain}/published", new { published = false })).StatusCode);
        await using var db = env.HubWideDb();
        Assert.False(await db.AuditLogEntries.AnyAsync(a => a.Action == AuditAction.DashboardPublished || a.Action == AuditAction.DashboardUnpublished));
    });

    [MetadataSqlFact]
    public Task Publish_is_404_in_embedded_mode() => HubApiHost.RunAsync(async env =>
    {
        var id = Guid.NewGuid();
        await using (var db = env.HubWideDb())
        {
            db.Dashboards.Add(new Dashboard { Id = id, AppId = App.DefaultId, Name = "d", OwnerId = "anonymous" });
            await db.SaveChangesAsync();
        }

        var browser = env.NewBrowser();
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PutAsync($"{Api}/dashboards/{id}/published", new { published = true })).StatusCode);
        await using var check = env.HubWideDb();
        Assert.False((await check.Dashboards.SingleAsync(d => d.Id == id)).PublishedToApp);
    }, hub: false);

    [MetadataSqlFact]
    public Task Me_permissions_per_role() => HubApiHost.RunAsync(async env =>
    {
        async Task<(bool Build, bool Manage, bool Publish, bool Admin, bool Viewer)> Of(HubApiHost.Browser b)
        {
            var me = await HubApiHost.Browser.JsonAsync(await b.GetAsync($"{Api}/me"));
            var p = me.GetProperty("permissions");
            return (p.GetProperty("canBuildQueries").GetBoolean(), p.GetProperty("canManageDashboards").GetBoolean(),
                p.GetProperty("canPublish").GetBoolean(), p.GetProperty("canAdmin").GetBoolean(), p.GetProperty("isViewer").GetBoolean());
        }

        Assert.Equal((false, false, false, false, true), await Of(await SignedIn(env, env.AppA, HubRole.Viewer)));
        Assert.Equal((true, true, false, false, false), await Of(await SignedIn(env, env.AppA, HubRole.Author)));
        Assert.Equal((true, true, true, true, false), await Of(await SignedIn(env, env.AppA, HubRole.Admin)));
        var super = env.NewBrowser();
        await super.SignInSuperAsync(env.AppA);
        Assert.Equal((true, true, true, true, false), await Of(super));
    });
}
