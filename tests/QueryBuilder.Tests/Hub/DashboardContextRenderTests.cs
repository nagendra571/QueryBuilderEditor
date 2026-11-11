using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Common;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Editor.Hub;
using Xunit;

namespace QueryBuilder.Tests.Hub;

/// <summary>A hub Viewer draws a chart only through a dashboard they can see, and that grant opens nothing else.</summary>
[Collection(MetadataDatabaseCollection.Name)]
public class DashboardContextRenderTests
{
    private const string Api = "/querybuilder/api";

    private sealed record Seed(
        Guid Source, Guid Query, Guid Viz, Guid OtherViz, Guid OtherQuery, Guid OtherQueryViz,
        Guid Published, Guid PublishedTile, Guid OtherPublished, Guid OtherPublishedTile, Guid Private, Guid PrivateTile,
        Guid OptionsSource, Guid ListQuery, Guid ListViz, Guid ListDashboard, Guid ListTile);

    private static QueryDefinition Definition(params string[] columns) => new()
    {
        Source = new QuerySource { SchemaName = "dbo", ObjectName = "pv", Kind = SchemaObjectKind.View, Alias = "t" },
        Columns = columns.Select((c, i) => new QueryColumn { TableAlias = "t", ColumnName = c, OrderIndex = i, DataType = c == "ProgramId" ? ColumnDataType.Number : ColumnDataType.Text }).ToList()
    };

    private static string TableOptions(params string[] columns) =>
        VisualizationOptionsSerializer.Serialize(new VisualizationOptions { Table = new TableOptions { Columns = [.. columns] } });

    // Rows: program 10 twice, program 20 once. Both apps' data sources read this view of the metadata database.
    private static async Task<Seed> SeedAsync(HubApiHost env)
    {
        await using (var setup = env.HubWideDb())
        {
            await setup.Database.ExecuteSqlRawAsync(
                "CREATE VIEW dbo.pv AS SELECT ProgramId, label FROM (VALUES (10, N'ten-a'), (10, N'ten-b'), (20, N'twenty')) AS x(ProgramId, label)");
        }

        var ids = new Seed(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var app = env.AppA;
        var tableOptions = TableOptions("ProgramId", "label");

        await using var db = env.HubWideDb();
        db.DataSources.Add(new DataSource { Id = ids.Source, AppId = app, Name = "src", ConnectionStringName = "biz", AllowedObjects = ["dbo.pv"], ScopeKeys = ["ProgramId"] });
        await db.SaveChangesAsync();
        db.DataScopeRules.Add(new DataScopeRule { AppId = app, DataSourceId = ids.Source, ObjectName = "dbo.pv", ScopeKey = "ProgramId", ColumnName = "ProgramId" });

        SavedQuery Query(Guid id, string name, QueryDefinition definition) => new()
        {
            Id = id, AppId = app, Name = name, DataSourceId = ids.Source, OwnerId = "mary", OwnerName = "Mary",
            DefinitionJson = QueryDefinitionSerializer.Serialize(definition)
        };

        var listDefinition = Definition("ProgramId", "label");
        listDefinition.Filters.Conditions.Add(new FilterCondition
        {
            TableAlias = "t", ColumnName = "label", DataType = ColumnDataType.Text, Operator = FilterOperator.Equals,
            IsParameterized = true, ParameterName = "p"
        });
        listDefinition.Parameters.Add(new QueryParameter
        {
            Name = "p", Label = "p", DataType = ColumnDataType.Text, Type = ParameterType.QueryList,
            OptionsQueryId = ids.OptionsSource, OptionsLabelColumn = "label", OptionsValueColumn = "label"
        });

        db.SavedQueries.AddRange(
            Query(ids.Query, "secret-query-name", Definition("ProgramId", "label")),
            Query(ids.OtherQuery, "other", Definition("ProgramId", "label")),
            Query(ids.OptionsSource, "options-source", Definition("label")),
            Query(ids.ListQuery, "list-query", listDefinition));
        db.Dashboards.AddRange(
            new Dashboard { Id = ids.Published, AppId = app, Name = "published", OwnerId = "mary", OwnerName = "Mary", PublishedToApp = true },
            new Dashboard { Id = ids.OtherPublished, AppId = app, Name = "other-published", OwnerId = "mary", OwnerName = "Mary", PublishedToApp = true },
            new Dashboard { Id = ids.Private, AppId = app, Name = "private", OwnerId = "mary", OwnerName = "Mary" },
            new Dashboard { Id = ids.ListDashboard, AppId = app, Name = "list-dashboard", OwnerId = "mary", OwnerName = "Mary", PublishedToApp = true,
                ParametersJson = QueryBuilder.Application.Dashboards.DashboardParameterStore.WriteParameters(
                [new QueryBuilder.Application.Dtos.DashboardParameterDto("dp", "Pick", ParameterType.QueryList, null, ids.OptionsSource, "label", "label", false, null)]) });
        await db.SaveChangesAsync();

        db.QueryVisualizations.AddRange(
            new QueryVisualization { Id = ids.Viz, AppId = app, SavedQueryId = ids.Query, Name = "chart", Type = VisualizationType.Table, OptionsJson = tableOptions },
            new QueryVisualization { Id = ids.OtherViz, AppId = app, SavedQueryId = ids.Query, Name = "second chart", Type = VisualizationType.Table, OptionsJson = tableOptions },
            new QueryVisualization { Id = ids.OtherQueryViz, AppId = app, SavedQueryId = ids.OtherQuery, Name = "other chart", Type = VisualizationType.Table, OptionsJson = tableOptions },
            new QueryVisualization { Id = ids.ListViz, AppId = app, SavedQueryId = ids.ListQuery, Name = "list chart", Type = VisualizationType.Table, OptionsJson = tableOptions });
        await db.SaveChangesAsync();

        db.DashboardTiles.AddRange(
            new DashboardTile { Id = ids.PublishedTile, AppId = app, DashboardId = ids.Published, Kind = DashboardTileKind.Chart, VisualizationId = ids.Viz },
            new DashboardTile { Id = ids.OtherPublishedTile, AppId = app, DashboardId = ids.OtherPublished, Kind = DashboardTileKind.Chart, VisualizationId = ids.OtherQueryViz },
            new DashboardTile { Id = ids.PrivateTile, AppId = app, DashboardId = ids.Private, Kind = DashboardTileKind.Chart, VisualizationId = ids.Viz },
            new DashboardTile { Id = ids.ListTile, AppId = app, DashboardId = ids.ListDashboard, Kind = DashboardTileKind.Chart, VisualizationId = ids.ListViz });
        await db.SaveChangesAsync();
        return ids;
    }

    private static string DataUrl(Guid query, Guid viz) => $"{Api}/queries/{query}/visualizations/{viz}/data";

    private static async Task<HubApiHost.Browser> ViewerAsync(HubApiHost env, string? scope = null)
    {
        var viewer = env.NewBrowser();
        await viewer.SignInAppAsync(env.AppA, HubRole.Viewer, "vera", scope);
        return viewer;
    }

    private static async Task<int> RowsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await HubApiHost.Browser.JsonAsync(response)).GetProperty("rows").GetArrayLength();
    }

    [MetadataSqlFact]
    public Task Viewer_renders_published_tile_with_context() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var viewer = await ViewerAsync(env);
        var response = await viewer.PostAsync(DataUrl(s.Query, s.Viz), new { parameterValues = new { }, dashboardId = s.Published, tileId = s.PublishedTile });
        Assert.Equal(3, await RowsAsync(response));
    });

    [MetadataSqlFact]
    public Task Viewer_render_without_valid_dashboard_context_is_refused() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var viewer = await ViewerAsync(env);
        await using (var db = env.HubWideDb())
        {
            db.Dashboards.Add(new Dashboard { AppId = env.AppB, Name = "b", OwnerId = "x", PublishedToApp = true });
            await db.SaveChangesAsync();
        }

        var cases = new (string Label, Guid Query, Guid Viz, object Body)[]
        {
            ("no context", s.Query, s.Viz, new { parameterValues = new { } }),
            ("dashboard id only", s.Query, s.Viz, new { parameterValues = new { }, dashboardId = s.Published }),
            ("tile id only", s.Query, s.Viz, new { parameterValues = new { }, tileId = s.PublishedTile }),
            ("dashboard not visible to the viewer", s.Query, s.Viz, new { parameterValues = new { }, dashboardId = s.Private, tileId = s.PrivateTile }),
            ("tile of another dashboard", s.Query, s.Viz, new { parameterValues = new { }, dashboardId = s.Published, tileId = s.OtherPublishedTile }),
            ("tile points at another visualization", s.Query, s.OtherViz, new { parameterValues = new { }, dashboardId = s.Published, tileId = s.PublishedTile }),
            ("tile belongs to another query", s.OtherQuery, s.OtherQueryViz, new { parameterValues = new { }, dashboardId = s.Published, tileId = s.PublishedTile }),
            ("unknown dashboard", s.Query, s.Viz, new { parameterValues = new { }, dashboardId = Guid.NewGuid(), tileId = s.PublishedTile }),
            ("unknown tile", s.Query, s.Viz, new { parameterValues = new { }, dashboardId = s.Published, tileId = Guid.NewGuid() })
        };

        var failures = new List<string>();
        foreach (var (label, query, viz, body) in cases)
        {
            var response = await viewer.PostAsync(DataUrl(query, viz), body);
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.NotFound) || text.Contains("ten-a", StringComparison.Ordinal))
            {
                failures.Add($"{label} -> {(int)response.StatusCode}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));

        // A dashboard of another app is invisible: the filtered load finds nothing, so the grant stays empty.
        await using var check = env.HubWideDb();
        var foreign = await check.Dashboards.SingleAsync(d => d.AppId == env.AppB);
        var cross = await viewer.PostAsync(DataUrl(s.Query, s.Viz), new { parameterValues = new { }, dashboardId = foreign.Id, tileId = s.PublishedTile });
        Assert.Equal(HttpStatusCode.Forbidden, cross.StatusCode);
    });

    [MetadataSqlFact]
    public Task Granted_render_uses_the_viewers_scope() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var body = new { parameterValues = new { }, dashboardId = s.Published, tileId = s.PublishedTile };

        var ten = await ViewerAsync(env, "ProgramId=10");
        var tenData = await HubApiHost.Browser.JsonAsync(await ten.PostAsync(DataUrl(s.Query, s.Viz), body));
        var programs = tenData.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("ProgramId").GetRawText()).ToList();
        Assert.Equal(["10", "10"], programs);

        var twenty = await ViewerAsync(env, "ProgramId=20");
        Assert.Equal(1, await RowsAsync(await twenty.PostAsync(DataUrl(s.Query, s.Viz), body)));

        // No value for the key at all: scoped to nothing, so the data source's rule hides the view entirely.
        var nothing = await ViewerAsync(env, "Other=1");
        var refused = await nothing.PostAsync(DataUrl(s.Query, s.Viz), body);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.DoesNotContain("ten-a", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    });

    [MetadataSqlFact]
    public Task Published_dashboard_tile_shows_no_query_name_to_a_viewer() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var viewer = await ViewerAsync(env);
        var detail = await HubApiHost.Browser.JsonAsync(await viewer.GetAsync($"{Api}/dashboards/{s.Published}"));
        var tile = detail.GetProperty("tiles").EnumerateArray().Single();
        Assert.False(tile.GetProperty("restricted").GetBoolean());
        var chart = tile.GetProperty("chart");
        Assert.Equal(s.Query, chart.GetProperty("savedQueryId").GetGuid());
        Assert.Equal(s.Viz, chart.GetProperty("visualizationId").GetGuid());
        Assert.Equal("chart", chart.GetProperty("visualizationName").GetString());
        Assert.Equal(JsonValueKind.Null, chart.GetProperty("savedQueryName").ValueKind);
        Assert.DoesNotContain("secret-query-name", detail.GetRawText(), StringComparison.Ordinal);

        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "boss");
        var adminDetail = await HubApiHost.Browser.JsonAsync(await admin.GetAsync($"{Api}/dashboards/{s.Published}"));
        Assert.Equal("secret-query-name", adminDetail.GetProperty("tiles")[0].GetProperty("chart").GetProperty("savedQueryName").GetString());
    });

    [MetadataSqlFact]
    public Task Grant_does_not_open_the_query() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var viewer = await ViewerAsync(env);
        var context = new { parameterValues = new { }, dashboardId = s.Published, tileId = s.PublishedTile };
        Assert.Equal(3, await RowsAsync(await viewer.PostAsync(DataUrl(s.Query, s.Viz), context)));

        Assert.Equal(HttpStatusCode.Forbidden, await viewer.StatusAsync($"{Api}/queries/{s.Query}"));
        Assert.Equal(HttpStatusCode.Forbidden, await viewer.StatusAsync($"{Api}/queries"));
        var definition = Definition("ProgramId", "label");
        var run = new { dataSourceId = s.Source, definition, parameterValues = new { }, savedQueryId = s.Query };
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"{Api}/queries/run", run)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"{Api}/queries/export", run)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"{Api}/queries/preview-sql", run)).StatusCode);

        // The same dashboard context opens neither a second chart of that query nor another query's chart.
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync(DataUrl(s.Query, s.OtherViz), context)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync(DataUrl(s.OtherQuery, s.OtherQueryViz), context)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{Api}/queries/{s.ListQuery}/parameters/p/options?dashboardId={s.Published}&tileId={s.PublishedTile}")).StatusCode);
    });

    [MetadataSqlFact]
    public Task Dashboard_query_list_parameter_options_run_as_viewer() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var viewer = await ViewerAsync(env);
        var url = $"{Api}/queries/{s.ListQuery}/parameters/p/options";

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{url}?dashboardId={s.Published}&tileId={s.PublishedTile}")).StatusCode);

        var options = await HubApiHost.Browser.JsonAsync(await viewer.GetAsync($"{url}?dashboardId={s.ListDashboard}&tileId={s.ListTile}"));
        Assert.Equal(["ten-a", "ten-b", "twenty"], options.EnumerateArray().Select(o => o.GetProperty("value").GetString()!).Order(StringComparer.Ordinal));

        // A value for the parameter is checked against those options, which the viewer may only read through the grant.
        var rendered = await viewer.PostAsync(DataUrl(s.ListQuery, s.ListViz),
            new { parameterValues = new Dictionary<string, string> { ["p"] = "ten-a" }, dashboardId = s.ListDashboard, tileId = s.ListTile });
        Assert.Equal(1, await RowsAsync(rendered));

        // Choices come from the viewer's own scope.
        var scoped = await ViewerAsync(env, "ProgramId=20");
        var scopedOptions = await HubApiHost.Browser.JsonAsync(await scoped.GetAsync($"{url}?dashboardId={s.ListDashboard}&tileId={s.ListTile}"));
        Assert.Equal(["twenty"], scopedOptions.EnumerateArray().Select(o => o.GetProperty("value").GetString()!));
    });

    [MetadataSqlFact]
    public Task Viewer_gets_tile_chart_without_query_definition() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var viewer = await ViewerAsync(env);
        var text = await (await viewer.GetAsync($"{Api}/dashboards/{s.ListDashboard}/tiles/{s.ListTile}/chart")).Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(s.ListViz, root.GetProperty("visualization").GetProperty("id").GetGuid());
        Assert.Equal("table", root.GetProperty("visualization").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("savedQueryName").ValueKind);
        Assert.False(root.GetProperty("isDisabled").GetBoolean());
        var parameter = root.GetProperty("parameters").EnumerateArray().Single();
        Assert.Equal("p", parameter.GetProperty("name").GetString());
        Assert.Equal("queryList", parameter.GetProperty("type").GetString(), ignoreCase: true);

        foreach (var forbidden in new[] { "definition", "sql", "optionsQueryId", "optionsLabelColumn", "filters", "source", "list-query" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(s.OptionsSource.ToString(), text, StringComparison.OrdinalIgnoreCase);

        // Other dashboards' tiles, private dashboards and unknown ids give nothing.
        Assert.Equal(HttpStatusCode.Forbidden, await viewer.StatusAsync($"{Api}/dashboards/{s.Private}/tiles/{s.PrivateTile}/chart"));
        Assert.Equal(HttpStatusCode.NotFound, await viewer.StatusAsync($"{Api}/dashboards/{s.Published}/tiles/{Guid.NewGuid()}/chart"));
    });

    [MetadataSqlFact]
    public Task Tile_chart_for_tile_of_other_dashboard_is_404() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var viewer = await ViewerAsync(env);
        Assert.Equal(HttpStatusCode.NotFound, await viewer.StatusAsync($"{Api}/dashboards/{s.Published}/tiles/{s.OtherPublishedTile}/chart"));
        await using (var db = env.HubWideDb())
        {
            var foreign = new Dashboard { AppId = env.AppB, Name = "b", OwnerId = "x", PublishedToApp = true };
            db.Dashboards.Add(foreign);
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.NotFound, await viewer.StatusAsync($"{Api}/dashboards/{foreign.Id}/tiles/{s.PublishedTile}/chart"));
        }
    });

    [MetadataSqlFact]
    public Task Author_without_query_access_renders_published_tile() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var author = env.NewBrowser();
        await author.SignInAppAsync(env.AppA, HubRole.Author, "bob");
        Assert.Equal(HttpStatusCode.Forbidden, await author.StatusAsync($"{Api}/queries/{s.Query}"));
        var response = await author.PostAsync(DataUrl(s.Query, s.Viz), new { parameterValues = new { }, dashboardId = s.Published, tileId = s.PublishedTile });
        Assert.Equal(3, await RowsAsync(response));
        var chart = await HubApiHost.Browser.JsonAsync(await author.GetAsync($"{Api}/dashboards/{s.Published}/tiles/{s.PublishedTile}/chart"));
        Assert.Equal(JsonValueKind.Null, chart.GetProperty("savedQueryName").ValueKind);
    });

    [MetadataSqlFact]
    public Task Granted_render_of_a_disabled_query_is_409() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        await using (var db = env.HubWideDb())
        {
            (await db.SavedQueries.SingleAsync(q => q.Id == s.Query)).IsDisabled = true;
            await db.SaveChangesAsync();
        }

        var viewer = await ViewerAsync(env);
        var response = await viewer.PostAsync(DataUrl(s.Query, s.Viz), new { parameterValues = new { }, dashboardId = s.Published, tileId = s.PublishedTile });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var chart = await HubApiHost.Browser.JsonAsync(await viewer.GetAsync($"{Api}/dashboards/{s.Published}/tiles/{s.PublishedTile}/chart"));
        Assert.True(chart.GetProperty("isDisabled").GetBoolean());
    });

    [MetadataSqlFact]
    public Task Viewer_gets_dashboard_parameter_options_as_themselves() => HubApiHost.RunAsync(async env =>
    {
        var s = await SeedAsync(env);
        var url = $"{Api}/dashboards/{s.ListDashboard}/parameters/dp/options";
        var all = await ViewerAsync(env);
        var options = await HubApiHost.Browser.JsonAsync(await all.GetAsync(url));
        Assert.Equal(["ten-a", "ten-b", "twenty"], options.EnumerateArray().Select(o => o.GetProperty("value").GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.Forbidden, await all.StatusAsync($"{Api}/queries/{s.OptionsSource}"));

        var twenty = await ViewerAsync(env, "ProgramId=20");
        var scoped = await HubApiHost.Browser.JsonAsync(await twenty.GetAsync(url));
        Assert.Equal(["twenty"], scoped.EnumerateArray().Select(o => o.GetProperty("value").GetString()!));

        Assert.Equal(HttpStatusCode.NotFound, await all.StatusAsync($"{Api}/dashboards/{s.ListDashboard}/parameters/nope/options"));
        Assert.Equal(HttpStatusCode.Forbidden, await all.StatusAsync($"{Api}/dashboards/{s.Private}/parameters/dp/options"));
    });

    [MetadataSqlFact]
    public Task Embedded_tiles_stay_restricted() => HubApiHost.RunAsync(async env =>
    {
        await using (var setup = env.HubWideDb())
        {
            await setup.Database.ExecuteSqlRawAsync("CREATE VIEW dbo.pv AS SELECT 1 AS ProgramId, N'x' AS label");
        }

        Guid dashboard = Guid.NewGuid(), tile = Guid.NewGuid(), query = Guid.NewGuid(), viz = Guid.NewGuid(), source = Guid.NewGuid();
        await using (var db = env.HubWideDb())
        {
            db.DataSources.Add(new DataSource { Id = source, AppId = App.DefaultId, Name = "src", ConnectionStringName = "biz", AllowedObjects = ["dbo.pv"] });
            await db.SaveChangesAsync();
            db.SavedQueries.Add(new SavedQuery { Id = query, AppId = App.DefaultId, Name = "someone-elses", DataSourceId = source, OwnerId = "someone", OwnerName = "S", DefinitionJson = QueryDefinitionSerializer.Serialize(Definition("label")) });
            db.Dashboards.Add(new Dashboard { Id = dashboard, AppId = App.DefaultId, Name = "d", OwnerId = "anonymous", OwnerName = "A" });
            await db.SaveChangesAsync();
            db.QueryVisualizations.Add(new QueryVisualization { Id = viz, AppId = App.DefaultId, SavedQueryId = query, Name = "v", Type = VisualizationType.Table, OptionsJson = TableOptions("label") });
            await db.SaveChangesAsync();
            db.DashboardTiles.Add(new DashboardTile { Id = tile, AppId = App.DefaultId, DashboardId = dashboard, Kind = DashboardTileKind.Chart, VisualizationId = viz });
            await db.SaveChangesAsync();
        }

        var browser = env.NewBrowser();
        var detail = await HubApiHost.Browser.JsonAsync(await browser.GetAsync($"{Api}/dashboards/{dashboard}"));
        var t = detail.GetProperty("tiles").EnumerateArray().Single();
        Assert.True(t.GetProperty("restricted").GetBoolean());
        Assert.DoesNotContain("someone-elses", detail.GetRawText(), StringComparison.Ordinal);

        // Context is ignored outside hub mode: the owner rule alone decides.
        var response = await browser.PostAsync(DataUrl(query, viz), new { parameterValues = new { }, dashboardId = dashboard, tileId = tile });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await browser.StatusAsync($"{Api}/dashboards/{dashboard}/tiles/{tile}/chart"));
        Assert.Equal(HttpStatusCode.NotFound, await browser.StatusAsync($"{Api}/dashboards/{dashboard}/parameters/dp/options"));
    }, hub: false);

    [MetadataSqlFact]
    public Task Publish_without_published_field_is_400() => HubApiHost.RunAsync(async env =>
    {
        var id = Guid.NewGuid();
        await using (var db = env.HubWideDb())
        {
            db.Dashboards.Add(new Dashboard { Id = id, AppId = env.AppA, Name = "d", OwnerId = "mary", OwnerName = "M", PublishedToApp = true });
            await db.SaveChangesAsync();
        }

        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "boss");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"{Api}/dashboards/{id}/published", new { })).StatusCode);
        await using var check = env.HubWideDb();
        Assert.True((await check.Dashboards.SingleAsync(d => d.Id == id)).PublishedToApp);
    });
}
