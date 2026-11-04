using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Dashboards;

/// <summary>Who may see / change a dashboard. Independent of query access: sharing a dashboard never
/// grants access to the queries behind its charts.</summary>
public static class DashboardAccess
{
    public const int MaxTiles = 30;
    public const int MaxTextLength = 2000;
    public const int MaxNameLength = 200;
    public const int GridColumns = 12;
    public const int MinWidth = 2;
    public const int MinHeight = 2;
    public const int MaxHeight = 24;
    public const int SectionHeight = 1;
    public const int MaxSectionTitleLength = 80;
    public const int MaxSectionDescriptionLength = 160;
    public const int MaxY = 1000;
    public const int DefaultChartWidth = 6;
    public const int DefaultChartHeight = 6;
    public const int DefaultTextWidth = 6;
    public const int DefaultTextHeight = 3;

    public static bool IsOwner(Dashboard dashboard, string userId) => dashboard.OwnerId == userId;

    public static bool CanView(Dashboard dashboard, string userId) =>
        IsOwner(dashboard, userId) || dashboard.Shares.Any(s => s.SharedWithUserId == userId);

    public static bool CanEdit(Dashboard dashboard, string userId) =>
        IsOwner(dashboard, userId)
        || dashboard.Shares.Any(s => s.SharedWithUserId == userId && s.AccessLevel == QueryAccessLevel.Editor);

    public static QueryAccessLevel AccessLevelFor(Dashboard dashboard, string userId) =>
        IsOwner(dashboard, userId)
            ? QueryAccessLevel.Owner
            : dashboard.Shares.FirstOrDefault(s => s.SharedWithUserId == userId)?.AccessLevel ?? QueryAccessLevel.Viewer;
}

public static class DashboardMapper
{
    public static DashboardSummaryDto ToSummary(Dashboard d, string userId) => new(
        d.Id, d.Name, d.Description, d.OwnerId, d.OwnerName, DashboardAccess.IsOwner(d, userId),
        DashboardAccess.AccessLevelFor(d, userId), d.Tiles.Count, d.CreatedAtUtc, d.UpdatedAtUtc);

    public static DashboardDetailDto ToDetail(Dashboard d, string userId) => new(
        d.Id, d.Name, d.Description, d.OwnerId, d.OwnerName, DashboardAccess.IsOwner(d, userId),
        DashboardAccess.AccessLevelFor(d, userId), d.Version,
        ResolveBoxes(d).Select(r => ToTile(r.Item, r.Box, userId)).ToList(),
        DashboardParameterStore.ReadParameters(d.ParametersJson));

    /// <summary>Stored boxes for a positioned dashboard; a converted flow for a legacy one. Never mutates.</summary>
    internal static List<(DashboardTile Item, LayoutBox Box)> ResolveBoxes(Dashboard d)
    {
        var ordered = d.Tiles.OrderBy(t => t.Order).ToList();
        return d.GridVersion <= 1
            ? DashboardLayout.FromLegacy(ordered.Select(t => (t, t.Width, t.Height)))
            : ordered.Select(t => (t, new LayoutBox(t.X, t.Y, t.Width, t.Height)))
                .OrderBy(r => r.Item2.Y).ThenBy(r => r.Item2.X).ToList();
    }

    /// <summary>The only place a tile's chart and query identity is exposed. A chart whose query the
    /// viewer can't see is reduced to its position and size, so no name or id of it can leak.</summary>
    private static DashboardTileDto ToTile(DashboardTile tile, LayoutBox box, string userId)
    {
        if (tile.Kind is DashboardTileKind.Text or DashboardTileKind.Section)
        {
            return new DashboardTileDto(tile.Id, tile.Kind, tile.Order, box.X, box.Y, box.W, box.H, tile.AutoHeight,
                Restricted: false, Missing: false, tile.Title, tile.Text, Chart: null);
        }

        var viz = tile.Visualization;
        if (tile.VisualizationId is null || viz is null)
        {
            return new DashboardTileDto(tile.Id, tile.Kind, tile.Order, box.X, box.Y, box.W, box.H, tile.AutoHeight,
                Restricted: false, Missing: true, tile.Title, Text: null, Chart: null);
        }

        var query = viz.SavedQuery;
        if (query is null || !SavedQueryAccess.CanView(query, userId))
        {
            return new DashboardTileDto(tile.Id, tile.Kind, tile.Order, box.X, box.Y, box.W, box.H, tile.AutoHeight,
                Restricted: true, Missing: false, Title: null, Text: null, Chart: null);
        }

        return new DashboardTileDto(tile.Id, tile.Kind, tile.Order, box.X, box.Y, box.W, box.H, tile.AutoHeight,
            Restricted: false, Missing: false, tile.Title, Text: null,
            new DashboardTileChartDto(query.Id, query.Name, viz.Id, viz.Name, viz.Type),
            DashboardParameterStore.ReadMappings(tile.ParameterMappingsJson));
    }
}
