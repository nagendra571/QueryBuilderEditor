using QueryBuilder.Application.Dashboards;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;

namespace QueryBuilder.Tests;

public class DashboardLayoutTests
{
    private static (string, LayoutBox) T(string name, int x, int y, int w, int h) => (name, new LayoutBox(x, y, w, h));

    [Fact]
    public void Compact_removes_gaps()
    {
        var result = DashboardLayout.Compact([T("a", 0, 0, 6, 3), T("b", 0, 10, 6, 3), T("c", 6, 7, 6, 2)]);

        Assert.Equal(new LayoutBox(0, 3, 6, 3), result.Single(r => r.Item == "b").Box);
        Assert.Equal(new LayoutBox(6, 0, 6, 2), result.Single(r => r.Item == "c").Box);
    }

    [Fact]
    public void Compact_resolves_overlaps_and_returns_reading_order()
    {
        var result = DashboardLayout.Compact([T("a", 0, 0, 6, 4), T("b", 3, 0, 6, 4), T("c", 6, 0, 6, 2)]);

        Assert.Equal(["a", "c", "b"], result.Select(r => r.Item));
        for (var i = 0; i < result.Count; i++)
        {
            for (var j = i + 1; j < result.Count; j++)
            {
                Assert.False(result[i].Box.Overlaps(result[j].Box));
            }
        }
    }

    [Fact]
    public void FromLegacy_flows_left_to_right_and_wraps_below_the_tallest_tile()
    {
        var result = DashboardLayout.FromLegacy([("a", 6, 2), ("b", 6, 1), ("c", 12, 3), ("d", 4, 2)]);

        Assert.Equal(new LayoutBox(0, 0, 6, 6), result[0].Box);
        Assert.Equal(new LayoutBox(6, 0, 6, 3), result[1].Box);
        Assert.Equal(new LayoutBox(0, 6, 12, 9), result[2].Box);
        Assert.Equal(new LayoutBox(0, 15, 4, 6), result[3].Box);
    }

    [Fact]
    public void BottomY_of_nothing_is_zero()
    {
        Assert.Equal(0, DashboardLayout.BottomY([]));
        Assert.Equal(9, DashboardLayout.BottomY([new LayoutBox(0, 0, 6, 6), new LayoutBox(0, 6, 6, 3)]));
    }

    [Fact]
    public async Task Save_compacts_overlapping_input_and_derives_order()
    {
        var f = new DashFixture();
        var tiles = new List<SaveDashboardTileRequest>
        {
            DashFixture.Req(f.TileOpen) with { Order = 0, X = 0, Y = 0 },
            DashFixture.Req(f.TileSecret) with { Order = 1, X = 3, Y = 0 },
            DashFixture.Req(f.TileText) with { Order = 2, X = 0, Y = 500 }
        };

        var detail = await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default);

        Assert.Equal((0, 0), (detail.Tiles[0].X, detail.Tiles[0].Y));
        Assert.Equal((3, 6), (detail.Tiles[1].X, detail.Tiles[1].Y));
        Assert.Equal((0, 12), (detail.Tiles[2].X, detail.Tiles[2].Y));
        Assert.Equal([0, 1, 2], f.Dashboard.Tiles.OrderBy(t => t.Y).ThenBy(t => t.X).Select(t => t.Order));
    }

    [Fact]
    public async Task Refused_save_leaves_the_stored_layout_untouched()
    {
        var f = new DashFixture();
        var before = f.Dashboard.Tiles.Select(t => (t.Id, t.X, t.Y, t.Width, t.Height)).ToList();
        var bad = f.Dashboard.Tiles.Select(DashFixture.Req).ToList();
        bad[0] = bad[0] with { X = 8, Width = 6 };

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, bad), default));

        Assert.Equal(before, f.Dashboard.Tiles.Select(t => (t.Id, t.X, t.Y, t.Width, t.Height)).ToList());
        Assert.Equal(1, f.Dashboard.Version);
        Assert.Equal(0, f.Dashboards.SaveCalls);
    }

    [Fact]
    public async Task AutoHeight_round_trips()
    {
        var f = new DashFixture();
        var tiles = f.Dashboard.Tiles.Select(DashFixture.Req).ToList();
        tiles[2] = tiles[2] with { AutoHeight = true };

        var detail = await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default);

        Assert.True(detail.Tiles.Single(t => t.Id == f.TileText.Id).AutoHeight);
        Assert.True(f.TileText.AutoHeight);
        Assert.False(f.TileOpen.AutoHeight);
    }

    [Fact]
    public async Task Legacy_dashboard_reads_as_a_converted_flow_and_saves_as_version_two()
    {
        var f = new DashFixture();
        f.Dashboard.GridVersion = 1;
        f.TileSecret.Width = 6; f.TileSecret.Height = 2;
        f.TileOpen.Width = 6; f.TileOpen.Height = 1;
        f.TileText.Width = 12; f.TileText.Height = 1;

        var detail = await new GetDashboardQueryHandler(f.Dashboards, f.User).Handle(new GetDashboardQuery(f.Dashboard.Id), default);

        Assert.Equal([(0, 0, 6, 6), (6, 0, 6, 3), (0, 6, 12, 3)],
            detail.Tiles.Select(t => (t.X, t.Y, t.Width, t.Height)).ToList());
        Assert.Equal(1, f.Dashboard.GridVersion);

        var requests = detail.Tiles.Select(t => new SaveDashboardTileRequest(
            t.Id, t.Kind, t.Order, t.X, t.Y, t.Width, t.Height, t.AutoHeight, t.Title, t.Text,
            f.Dashboard.Tiles.Single(x => x.Id == t.Id).VisualizationId)).ToList();
        var saved = await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, requests), default);

        Assert.Equal(2, f.Dashboard.GridVersion);
        Assert.Equal(3, saved.Tiles.Count);
    }

    [Fact]
    public async Task Restricted_tile_moved_by_an_editor_keeps_its_chart_and_new_position()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;
        var tiles = f.Dashboard.Tiles
            .Select(t => DashFixture.ReqAsClientSees(t, t.Id == f.TileSecret.Id))
            .Select(t => t.Id == f.TileSecret.Id ? t with { X = 6, Y = 0, Width = 6, Height = 4 }
                : t.Id == f.TileOpen.Id ? t with { X = 0, Y = 0 } : t)
            .ToList();

        await f.SaveTilesHandler().Handle(new SaveDashboardTilesCommand(f.Dashboard.Id, 1, tiles), default);

        Assert.Equal(f.VizSecret.Id, f.TileSecret.VisualizationId);
        Assert.Equal((6, 0, 6, 4), (f.TileSecret.X, f.TileSecret.Y, f.TileSecret.Width, f.TileSecret.Height));
    }
}
