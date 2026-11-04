using System.Text.Json;
using System.Text.Json.Serialization;
using QueryBuilder.Application.Dashboards;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Tests;

public class DashboardTests
{
    // Same shape as the Editor's QueryBuilderJson.Options, which is internal to that assembly.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static async Task<DashboardDetailDto> Detail(DashFixture f, string user)
    {
        f.User.UserId = user;
        return await new GetDashboardQueryHandler(f.Dashboards, f.User).Handle(new GetDashboardQuery(f.Dashboard.Id), default);
    }

    // ---- restricted tiles ----

    [Fact]
    public async Task Restricted_tile_exposes_only_position_and_size_and_leaks_no_names_or_ids()
    {
        var f = new DashFixture();

        var detail = await Detail(f, DashFixture.Guest);

        var restricted = detail.Tiles.Where(t => t.Restricted).ToList();
        Assert.Equal(2, restricted.Count);
        foreach (var tile in restricted)
        {
            Assert.Null(tile.Chart);
            Assert.Null(tile.Title);
            Assert.Null(tile.Text);
        }

        var wire = JsonSerializer.Serialize(detail, Json);
        Assert.DoesNotContain(DashFixture.SecretQueryName, wire);
        Assert.DoesNotContain(DashFixture.SecretChartName, wire);
        Assert.DoesNotContain("Open sales", wire);
        Assert.DoesNotContain(f.QuerySecret.Id.ToString(), wire);
        Assert.DoesNotContain(f.VizSecret.Id.ToString(), wire);
        Assert.DoesNotContain(f.VizOpen.Id.ToString(), wire);
    }

    [Theory]
    [InlineData(VisualizationType.Combo, "combo")]
    [InlineData(VisualizationType.Scatter, "scatter")]
    [InlineData(VisualizationType.Funnel, "funnel")]
    [InlineData(VisualizationType.Waterfall, "waterfall")]
    [InlineData(VisualizationType.Trend, "trend")]
    [InlineData(VisualizationType.Pivot, "pivot")]
    [InlineData(VisualizationType.Table, "table")]
    public async Task Restricted_tile_of_a_new_chart_type_reveals_no_type_name_or_data(VisualizationType type, string wireName)
    {
        var f = new DashFixture();
        f.VizSecret.Type = type;
        f.VizSecret.OptionsJson = """{"version":1,"x":"SecretColumn","measures":["SecretMeasure"]}""";

        var detail = await Detail(f, DashFixture.Guest);

        var tile = detail.Tiles.Single(t => t.Id == f.TileSecret.Id);
        Assert.True(tile.Restricted);
        Assert.Null(tile.Chart);
        var wire = JsonSerializer.Serialize(detail, Json);
        Assert.DoesNotContain(wireName, wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecretColumn", wire);
        Assert.DoesNotContain("SecretMeasure", wire);
        Assert.DoesNotContain(DashFixture.SecretChartName, wire);
    }

    [Fact]
    public async Task Viewer_with_query_access_sees_only_the_chart_they_can_view()
    {
        var f = new DashFixture();

        var detail = await Detail(f, DashFixture.ViewerUser);

        var secret = detail.Tiles.Single(t => t.Id == f.TileSecret.Id);
        var open = detail.Tiles.Single(t => t.Id == f.TileOpen.Id);
        Assert.True(secret.Restricted);
        Assert.False(open.Restricted);
        Assert.Equal(f.QueryOpen.Id, open.Chart!.SavedQueryId);
        Assert.DoesNotContain(DashFixture.SecretQueryName, JsonSerializer.Serialize(detail, Json));
    }

    [Fact]
    public async Task Owner_sees_every_chart_and_deleted_visualization_is_marked_missing()
    {
        var f = new DashFixture();
        f.TileOpen.VisualizationId = null;
        f.TileOpen.Visualization = null;

        var detail = await Detail(f, DashFixture.Owner);

        Assert.False(detail.Tiles.Single(t => t.Id == f.TileSecret.Id).Restricted);
        var gone = detail.Tiles.Single(t => t.Id == f.TileOpen.Id);
        Assert.True(gone.Missing);
        Assert.False(gone.Restricted);
        Assert.Null(gone.Chart);
    }

    [Fact]
    public async Task Stranger_cannot_open_the_dashboard()
    {
        var f = new DashFixture();
        await Assert.ThrowsAsync<ForbiddenException>(() => Detail(f, DashFixture.Stranger));
    }

    // ---- permissions ----

    [Fact]
    public async Task Viewer_cannot_save_tiles_or_rename()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.ViewerUser;

        await Assert.ThrowsAsync<ForbiddenException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [DashFixture.NewText("x")]), default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new RenameDashboardCommandHandler(f.Dashboards, f.User, f.Audit)
                .Handle(new RenameDashboardCommand(f.Dashboard.Id, "New", null, 1), default));

        Assert.Equal(3, f.Dashboard.Tiles.Count);
        Assert.Equal(1, f.Dashboard.Version);
        Assert.Equal("Ops overview", f.Dashboard.Name);
    }

    [Fact]
    public async Task Editor_can_rename_and_version_bumps()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;

        var detail = await new RenameDashboardCommandHandler(f.Dashboards, f.User, f.Audit)
            .Handle(new RenameDashboardCommand(f.Dashboard.Id, "  Renamed ", null, 1), default);

        Assert.Equal("Renamed", detail.Name);
        Assert.Equal(2, detail.Version);
        Assert.Equal(AuditAction.DashboardUpdated, Assert.Single(f.Audit.Entries).Action);
    }

    [Theory]
    [InlineData(DashFixture.EditorUser)]
    [InlineData(DashFixture.ViewerUser)]
    [InlineData(DashFixture.Stranger)]
    public async Task Non_owner_cannot_delete_or_manage_shares(string user)
    {
        var f = new DashFixture();
        f.User.UserId = user;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new DeleteDashboardCommandHandler(f.Dashboards, f.User, f.Audit).Handle(new DeleteDashboardCommand(f.Dashboard.Id), default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.ShareHandler().Handle(new ShareDashboardCommand(f.Dashboard.Id, "someone", "s@x.com", QueryAccessLevel.Viewer, null), default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new GetDashboardSharesQueryHandler(f.Dashboards, f.User).Handle(new GetDashboardSharesQuery(f.Dashboard.Id), default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new RemoveDashboardShareCommandHandler(f.Dashboards, f.User, f.Audit)
                .Handle(new RemoveDashboardShareCommand(f.Dashboard.Id, f.Dashboard.Shares[0].Id), default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new GetDashboardSharePreviewQueryHandler(f.Dashboards, f.User).Handle(new GetDashboardSharePreviewQuery(f.Dashboard.Id, "x"), default));

        Assert.Single(f.Dashboards.Items);
        Assert.Equal(3, f.Dashboard.Shares.Count);
    }

    [Fact]
    public async Task Owner_can_delete_and_it_is_audited()
    {
        var f = new DashFixture();

        await new DeleteDashboardCommandHandler(f.Dashboards, f.User, f.Audit).Handle(new DeleteDashboardCommand(f.Dashboard.Id), default);

        Assert.Empty(f.Dashboards.Items);
        Assert.Equal(AuditAction.DashboardDeleted, Assert.Single(f.Audit.Entries).Action);
    }

    // ---- listing ----

    [Fact]
    public async Task List_returns_only_owned_and_shared_dashboards()
    {
        var f = new DashFixture();
        f.Dashboards.Items.Add(new Dashboard { Name = "Private to owner", OwnerId = DashFixture.Owner });
        f.Dashboards.Items.Add(new Dashboard { Name = "Viewer's own", OwnerId = DashFixture.ViewerUser });

        f.User.UserId = DashFixture.ViewerUser;
        var viewerList = await new GetDashboardsQueryHandler(f.Dashboards, f.User).Handle(new GetDashboardsQuery(), default);
        Assert.Equal(["Ops overview", "Viewer's own"], viewerList.Select(d => d.Name).Order().ToList());

        f.User.UserId = DashFixture.Stranger;
        Assert.Empty(await new GetDashboardsQueryHandler(f.Dashboards, f.User).Handle(new GetDashboardsQuery(), default));

        f.User.UserId = DashFixture.Owner;
        Assert.Equal(2, (await new GetDashboardsQueryHandler(f.Dashboards, f.User).Handle(new GetDashboardsQuery(), default)).Count);
    }

    // ---- tile save ----

    [Fact]
    public async Task Stale_version_is_a_conflict_and_changes_nothing()
    {
        var f = new DashFixture();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 0, [DashFixture.NewText("x")]), default));

        Assert.Equal(3, f.Dashboard.Tiles.Count);
        Assert.Equal(1, f.Dashboard.Version);
    }

    [Fact]
    public async Task Second_save_with_the_old_version_conflicts()
    {
        var f = new DashFixture();
        var tiles = f.Dashboard.Tiles.Select(DashFixture.Req).ToList();

        await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default);
        Assert.Equal(2, f.Dashboard.Version);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default));
    }

    [Fact]
    public async Task Adding_a_chart_without_access_to_its_query_is_forbidden()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;
        var tiles = f.Dashboard.Tiles.Select(t => DashFixture.ReqAsClientSees(t, t.Id == f.TileSecret.Id)).ToList();
        tiles.Add(DashFixture.NewChart(f.VizSecret.Id));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default));

        Assert.Equal(3, f.Dashboard.Tiles.Count);
    }

    [Fact]
    public async Task Retargeting_an_existing_tile_to_an_inaccessible_chart_is_forbidden()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;
        var tiles = f.Dashboard.Tiles.Select(t => DashFixture.ReqAsClientSees(t, t.Id == f.TileSecret.Id)).ToList();
        var index = tiles.FindIndex(t => t.Id == f.TileOpen.Id);
        tiles[index] = tiles[index] with { VisualizationId = f.VizSecret.Id };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default));

        Assert.Equal(f.VizOpen.Id, f.TileOpen.VisualizationId);
    }

    [Fact]
    public async Task Editor_without_query_access_keeps_the_restricted_tile_chart_when_saving()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;
        // The browser cannot send the hidden chart's id, and reorders + resizes the restricted tile.
        var tiles = f.Dashboard.Tiles
            .Select(t => DashFixture.ReqAsClientSees(t, t.Id == f.TileSecret.Id))
            .Select(t => t.Id == f.TileSecret.Id ? t with { Order = 5, Width = 12, Height = 3 } : t)
            .ToList();
        // A hostile client tries to retarget the restricted tile to the other chart.
        var hostile = tiles.Select(t => t.Id == f.TileSecret.Id ? t with { VisualizationId = f.VizOpen.Id, Title = "pwn" } : t).ToList();

        await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, hostile), default);

        Assert.Equal(f.VizSecret.Id, f.TileSecret.VisualizationId);
        Assert.Null(f.TileSecret.Title);
        Assert.Equal(12, f.TileSecret.Width);
        Assert.Equal(3, f.TileSecret.Height);
        Assert.Contains(f.TileSecret, f.Dashboard.Tiles);
    }

    [Fact]
    public async Task Editor_can_remove_a_restricted_tile()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;
        var tiles = f.Dashboard.Tiles.Where(t => t.Id != f.TileSecret.Id).Select(DashFixture.Req).ToList();

        await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default);

        Assert.DoesNotContain(f.Dashboard.Tiles, t => t.Id == f.TileSecret.Id);
        Assert.Equal(2, f.Dashboard.Tiles.Count);
    }

    [Fact]
    public async Task Save_reorders_resizes_adds_a_text_tile_and_bumps_the_version()
    {
        var f = new DashFixture();
        var tiles = new List<SaveDashboardTileRequest>
        {
            DashFixture.Req(f.TileOpen) with { Order = 0, X = 0, Y = 0, Width = 12 },
            DashFixture.Req(f.TileSecret) with { Order = 1, X = 0, Y = 6, Width = 4, Height = 3 },
            DashFixture.NewText("  hello <script>alert(1)</script>  ", 2)
        };

        var detail = await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default);

        Assert.Equal(2, detail.Version);
        Assert.Equal([f.TileOpen.Id, f.TileSecret.Id], detail.Tiles.Take(2).Select(t => t.Id).ToList());
        Assert.Equal(12, detail.Tiles[0].Width);
        Assert.Equal("hello <script>alert(1)</script>", detail.Tiles[2].Text);
        Assert.DoesNotContain(f.Dashboard.Tiles, t => t.Id == f.TileText.Id);
        Assert.Equal(AuditAction.DashboardUpdated, Assert.Single(f.Audit.Entries).Action);
    }

    [Fact]
    public async Task Thirty_first_tile_is_rejected_and_thirty_is_allowed()
    {
        var f = new DashFixture();
        var thirty = Enumerable.Range(0, 30).Select(i => DashFixture.NewText("t", i)).ToList();
        await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, thirty), default);
        Assert.Equal(30, f.Dashboard.Tiles.Count);

        var thirtyOne = Enumerable.Range(0, 31).Select(i => DashFixture.NewText("t", i)).ToList();
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 2, thirtyOne), default));
    }

    [Fact]
    public async Task Text_over_2000_characters_is_rejected()
    {
        var f = new DashFixture();
        await Assert.ThrowsAsync<CatalogValidationException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [DashFixture.NewText(new string('a', 2001))]), default));

        await f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [DashFixture.NewText(new string('a', 2000))]), default);
    }

    [Theory]
    [InlineData(1, 2, 0, 0)]
    [InlineData(6, 25, 0, 0)]
    [InlineData(6, 1, 0, 0)]
    [InlineData(0, 3, 0, 0)]
    [InlineData(6, 3, 7, 0)]
    [InlineData(6, 3, -1, 0)]
    [InlineData(6, 3, 0, -1)]
    [InlineData(6, 3, 0, 1001)]
    public async Task Out_of_bounds_tile_geometry_is_rejected_and_nothing_is_saved(int width, int height, int x, int y)
    {
        var f = new DashFixture();
        var bad = DashFixture.NewText("x") with { Width = width, Height = height, X = x, Y = y };

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [bad]), default));
    }

    // ---- sharing ----

    [Fact]
    public async Task Share_with_valid_also_share_creates_viewer_share_on_the_query()
    {
        var f = new DashFixture();

        await f.ShareHandler().Handle(
            new ShareDashboardCommand(f.Dashboard.Id, "newbie", "n@x.com", QueryAccessLevel.Viewer, [f.QuerySecret.Id]), default);

        Assert.Contains(f.Dashboard.Shares, s => s.SharedWithUserId == "newbie");
        var queryShare = Assert.Single(f.QuerySecret.Shares, s => s.SharedWithUserId == "newbie");
        Assert.Equal(QueryAccessLevel.Viewer, queryShare.AccessLevel);
        Assert.Contains(f.Audit.Entries, e => e.Action == AuditAction.DashboardShared);
        Assert.Contains(f.Audit.Entries, e => e.Action == AuditAction.QueryShared);
    }

    [Fact]
    public async Task Also_share_with_a_query_not_on_the_dashboard_is_refused_and_shares_nothing()
    {
        var f = new DashFixture();
        var stray = new SavedQuery { Name = "Stray", OwnerId = DashFixture.Owner, DataSourceId = TestData.DataSourceId };
        var queries = new DashFixture.FakeQueries(f.QuerySecret, f.QueryOpen, stray);
        var handler = new ShareDashboardCommandHandler(f.Dashboards, queries, new QueryBuilder.Application.Commands.SavedQueries.ShareQueryCommandHandler(queries, f.User, f.Audit), f.User, f.Audit);

        await Assert.ThrowsAsync<CatalogValidationException>(() => handler.Handle(
            new ShareDashboardCommand(f.Dashboard.Id, "newbie", "n@x.com", QueryAccessLevel.Viewer, [f.QuerySecret.Id, stray.Id]), default));

        Assert.DoesNotContain(f.Dashboard.Shares, s => s.SharedWithUserId == "newbie");
        Assert.Empty(f.QuerySecret.Shares);
        Assert.Empty(stray.Shares);
    }

    [Fact]
    public async Task Also_share_with_a_query_the_caller_does_not_own_is_refused_and_shares_nothing()
    {
        var f = new DashFixture();
        f.QuerySecret.OwnerId = "someone-else";
        f.QuerySecret.Shares.Add(new QueryShare { SharedWithUserId = DashFixture.Owner, AccessLevel = QueryAccessLevel.Editor });

        await Assert.ThrowsAsync<ForbiddenException>(() => f.ShareHandler().Handle(
            new ShareDashboardCommand(f.Dashboard.Id, "newbie", "n@x.com", QueryAccessLevel.Viewer, [f.QueryOpen.Id, f.QuerySecret.Id]), default));

        Assert.DoesNotContain(f.Dashboard.Shares, s => s.SharedWithUserId == "newbie");
        Assert.DoesNotContain(f.QueryOpen.Shares, s => s.SharedWithUserId == "newbie");
    }

    [Fact]
    public async Task Also_share_never_downgrades_an_existing_editor_share()
    {
        var f = new DashFixture();

        // 'editor' already has an Editor share on QueryOpen.
        await f.ShareHandler().Handle(
            new ShareDashboardCommand(f.Dashboard.Id, DashFixture.EditorUser, "e@x.com", QueryAccessLevel.Viewer, [f.QueryOpen.Id]), default);

        Assert.Equal(QueryAccessLevel.Editor, f.QueryOpen.Shares.Single(s => s.SharedWithUserId == DashFixture.EditorUser).AccessLevel);
        Assert.Equal(QueryAccessLevel.Viewer, f.Dashboard.Shares.Single(s => s.SharedWithUserId == DashFixture.EditorUser).AccessLevel);
    }

    [Fact]
    public async Task Share_preview_lists_only_queries_the_target_cannot_view_with_can_share_for_the_caller_s_own()
    {
        var f = new DashFixture();
        f.QueryOpen.OwnerId = "someone-else";
        f.QueryOpen.Shares.Add(new QueryShare { SharedWithUserId = DashFixture.Owner, AccessLevel = QueryAccessLevel.Viewer });

        var preview = await new GetDashboardSharePreviewQueryHandler(f.Dashboards, f.User)
            .Handle(new GetDashboardSharePreviewQuery(f.Dashboard.Id, "newbie"), default);

        Assert.Equal(2, preview.InaccessibleQueries.Count);
        Assert.True(preview.InaccessibleQueries.Single(q => q.Id == f.QuerySecret.Id).CanShare);
        Assert.False(preview.InaccessibleQueries.Single(q => q.Id == f.QueryOpen.Id).CanShare);

        var forViewer = await new GetDashboardSharePreviewQueryHandler(f.Dashboards, f.User)
            .Handle(new GetDashboardSharePreviewQuery(f.Dashboard.Id, DashFixture.ViewerUser), default);
        Assert.Equal(f.QuerySecret.Id, Assert.Single(forViewer.InaccessibleQueries).Id);
    }

    [Fact]
    public async Task Share_preview_never_reveals_a_query_the_owner_cannot_view()
    {
        // An Editor added a chart from their own unshared query; the dashboard owner can't see it.
        var f = new DashFixture();
        f.QuerySecret.OwnerId = DashFixture.EditorUser;
        f.QuerySecret.Shares.Clear();

        var preview = await new GetDashboardSharePreviewQueryHandler(f.Dashboards, f.User)
            .Handle(new GetDashboardSharePreviewQuery(f.Dashboard.Id, "newbie"), default);

        Assert.DoesNotContain(preview.InaccessibleQueries, q => q.Id == f.QuerySecret.Id);
        Assert.DoesNotContain(preview.InaccessibleQueries, q => q.Name == DashFixture.SecretQueryName);
        Assert.Contains(preview.InaccessibleQueries, q => q.Id == Guid.Empty && !q.CanShare);
    }

    // ---- pickable ----

    [Fact]
    public async Task Pickable_visualizations_exclude_queries_the_user_cannot_view()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.ViewerUser;

        var picks = await new GetPickableVisualizationsQueryHandler(f.Queries, f.Visualizations, f.User)
            .Handle(new GetPickableVisualizationsQuery(), default);

        var pick = Assert.Single(picks);
        Assert.Equal(f.VizOpen.Id, pick.VisualizationId);
        Assert.Equal("Open sales", pick.SavedQueryName);

        f.User.UserId = DashFixture.Stranger;
        Assert.Empty(await new GetPickableVisualizationsQueryHandler(f.Queries, f.Visualizations, f.User)
            .Handle(new GetPickableVisualizationsQuery(), default));

        f.User.UserId = DashFixture.Owner;
        Assert.Equal(2, (await new GetPickableVisualizationsQueryHandler(f.Queries, f.Visualizations, f.User)
            .Handle(new GetPickableVisualizationsQuery(), default)).Count);
    }

    [Fact]
    public async Task Create_sets_owner_and_audits()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;

        var detail = await new CreateDashboardCommandHandler(f.Dashboards, f.User, f.Audit)
            .Handle(new CreateDashboardCommand("  Mine ", null), default);

        Assert.Equal("Mine", detail.Name);
        Assert.True(detail.IsOwner);
        Assert.Equal(1, detail.Version);
        Assert.Equal(AuditAction.DashboardCreated, Assert.Single(f.Audit.Entries).Action);
    }

    // ---- add a chart from the query page ----

    private static AddChartToDashboardCommandHandler AddChartHandler(DashFixture f) =>
        new(f.Dashboards, f.Visualizations, f.Queries, f.User, f.Audit);

    [Fact]
    public async Task Editor_with_query_access_appends_a_chart_tile_at_the_end()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;

        var summary = await AddChartHandler(f).Handle(new AddChartToDashboardCommand(f.Dashboard.Id, f.VizOpen.Id), default);

        Assert.Equal(4, summary.TileCount);
        var added = f.Dashboard.Tiles.Single(t => t.Order == 3);
        Assert.Equal(DashboardTileKind.Chart, added.Kind);
        Assert.Equal(f.VizOpen.Id, added.VisualizationId);
        Assert.Equal(2, f.Dashboard.Version);
        Assert.Equal(AuditAction.DashboardUpdated, Assert.Single(f.Audit.Entries).Action);
    }

    [Fact]
    public async Task Viewer_of_the_dashboard_cannot_add_a_chart()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.ViewerUser;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            AddChartHandler(f).Handle(new AddChartToDashboardCommand(f.Dashboard.Id, f.VizOpen.Id), default));

        Assert.Equal(3, f.Dashboard.Tiles.Count);
        Assert.Equal(1, f.Dashboard.Version);
    }

    [Fact]
    public async Task Editor_cannot_add_a_chart_from_a_query_they_cannot_view()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            AddChartHandler(f).Handle(new AddChartToDashboardCommand(f.Dashboard.Id, f.VizSecret.Id), default));

        Assert.Equal(3, f.Dashboard.Tiles.Count);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Adding_a_chart_to_a_full_dashboard_is_refused()
    {
        var f = new DashFixture();
        for (var i = f.Dashboard.Tiles.Count; i < DashboardAccess.MaxTiles; i++)
        {
            f.Dashboard.Tiles.Add(new DashboardTile { DashboardId = f.Dashboard.Id, Kind = DashboardTileKind.Text, Text = "t", Order = i, Y = i * 2, Width = 3, Height = 2 });
        }

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            AddChartHandler(f).Handle(new AddChartToDashboardCommand(f.Dashboard.Id, f.VizOpen.Id), default));

        Assert.Equal(DashboardAccess.MaxTiles, f.Dashboard.Tiles.Count);
    }

    [Fact]
    public async Task Added_chart_lands_at_column_zero_below_the_lowest_tile()
    {
        var f = new DashFixture();

        await AddChartHandler(f).Handle(new AddChartToDashboardCommand(f.Dashboard.Id, f.VizOpen.Id), default);

        var added = f.Dashboard.Tiles.Single(t => t.Order == 3);
        Assert.Equal((0, 9, 6, 6), (added.X, added.Y, added.Width, added.Height));
    }

    [Fact]
    public async Task Adding_a_chart_converts_a_legacy_dashboard_first()
    {
        var f = new DashFixture();
        f.Dashboard.GridVersion = 1;
        foreach (var t in f.Dashboard.Tiles) { t.X = 0; t.Y = 0; }
        f.TileSecret.Width = 6; f.TileSecret.Height = 2;
        f.TileOpen.Width = 6; f.TileOpen.Height = 1;
        f.TileText.Width = 12; f.TileText.Height = 1;

        await AddChartHandler(f).Handle(new AddChartToDashboardCommand(f.Dashboard.Id, f.VizOpen.Id), default);

        Assert.Equal(2, f.Dashboard.GridVersion);
        Assert.Equal((0, 0, 6, 6), (f.TileSecret.X, f.TileSecret.Y, f.TileSecret.Width, f.TileSecret.Height));
        Assert.Equal((6, 0, 6, 3), (f.TileOpen.X, f.TileOpen.Y, f.TileOpen.Width, f.TileOpen.Height));
        Assert.Equal((0, 6, 12, 3), (f.TileText.X, f.TileText.Y, f.TileText.Width, f.TileText.Height));
        var added = f.Dashboard.Tiles.Single(t => t.Order == 3);
        Assert.Equal((0, 9), (added.X, added.Y));
    }

    // ---- section headings ----

    [Theory]
    [InlineData("Enrolment", "By program, last 30 days")]
    [InlineData("Enrolment", null)]
    public async Task Section_saves_and_round_trips_with_and_without_a_description(string title, string? description)
    {
        var f = new DashFixture();

        var detail = await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1,
            [DashFixture.NewSection("  " + title + "  ", description is null ? null : "  " + description + "  ")]), default);

        var section = Assert.Single(detail.Tiles);
        Assert.Equal(DashboardTileKind.Section, section.Kind);
        Assert.Equal(title, section.Title);
        Assert.Equal(description, section.Text);
        Assert.Null(section.Chart);
        Assert.False(section.Restricted);
        Assert.Equal((0, 0, 12, 1), (section.X, section.Y, section.Width, section.Height));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Section_with_a_blank_title_is_rejected(string? title)
    {
        var f = new DashFixture();
        await Assert.ThrowsAsync<CatalogValidationException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [DashFixture.NewSection(title)]), default));
    }

    [Fact]
    public async Task Section_title_over_80_and_description_over_160_are_rejected_at_the_limits()
    {
        var f = new DashFixture();
        await Assert.ThrowsAsync<CatalogValidationException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [DashFixture.NewSection(new string('a', 81))]), default));
        await Assert.ThrowsAsync<CatalogValidationException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [DashFixture.NewSection("t", new string('a', 161))]), default));

        await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1,
            [DashFixture.NewSection(new string('a', 80), new string('b', 160))]), default);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public async Task Section_description_with_a_newline_is_rejected(string description)
    {
        var f = new DashFixture();
        await Assert.ThrowsAsync<CatalogValidationException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [DashFixture.NewSection("t", description)]), default));
    }

    [Theory]
    [InlineData(6, 1, 0)]
    [InlineData(12, 2, 0)]
    [InlineData(12, 3, 0)]
    [InlineData(12, 1, 2)]
    public async Task Section_must_be_full_width_one_row_high_and_at_x_zero(int width, int height, int x)
    {
        var f = new DashFixture();
        var bad = DashFixture.NewSection("t") with { Width = width, Height = height, X = x };

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [bad]), default));
    }

    [Fact]
    public async Task Section_carries_no_visualization_and_other_tiles_still_reject_height_one()
    {
        var f = new DashFixture();
        await Assert.ThrowsAsync<CatalogValidationException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1,
                [DashFixture.NewSection("t") with { VisualizationId = f.VizOpen.Id }]), default));
        await Assert.ThrowsAsync<CatalogValidationException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [DashFixture.NewText("x") with { Height = 1 }]), default));
        await Assert.ThrowsAsync<CatalogValidationException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 1, [DashFixture.NewChart(f.VizOpen.Id) with { Height = 1 }]), default));
    }

    [Fact]
    public async Task Section_counts_toward_the_thirty_tile_cap()
    {
        var f = new DashFixture();
        var tiles = Enumerable.Range(0, 30).Select(i => DashFixture.NewText("t", i)).ToList();
        tiles[0] = DashFixture.NewSection("s");
        await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default);

        tiles.Add(DashFixture.NewSection("one too many", null, 31));
        await Assert.ThrowsAsync<CatalogValidationException>(() => f.SaveTilesHandler().Handle(
            new SaveDashboardTilesCommand(f.Dashboard.Id, 2, tiles), default));
    }

    [Fact]
    public async Task Restricted_viewer_still_receives_the_section_title_and_description()
    {
        var f = new DashFixture();
        f.Dashboard.Tiles.Add(new DashboardTile
        {
            DashboardId = f.Dashboard.Id, Kind = DashboardTileKind.Section, Title = "Heading", Text = "Sub",
            Order = 3, X = 0, Y = 9, Width = 12, Height = 1
        });

        var detail = await Detail(f, DashFixture.Guest);

        var section = detail.Tiles.Single(t => t.Kind == DashboardTileKind.Section);
        Assert.False(section.Restricted);
        Assert.False(section.Missing);
        Assert.Equal(("Heading", "Sub"), (section.Title, section.Text));
    }

    [Fact]
    public async Task Saving_a_section_between_charts_compacts_height_one_tiles_without_gaps()
    {
        var f = new DashFixture();
        var tiles = new List<SaveDashboardTileRequest>
        {
            DashFixture.Req(f.TileOpen) with { Order = 0, X = 0, Y = 0, Width = 12, Height = 4 },
            DashFixture.NewSection("Mid", null, 1) with { Y = 9 },
            DashFixture.Req(f.TileSecret) with { Order = 2, X = 0, Y = 12, Width = 12, Height = 3 }
        };

        var detail = await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default);

        Assert.Equal([(0, 4, 12, 1), (0, 5, 12, 3)], detail.Tiles.Skip(1).Select(t => (t.X, t.Y, t.Width, t.Height)));
        Assert.Equal(DashboardTileKind.Section, detail.Tiles[1].Kind);
    }

}
