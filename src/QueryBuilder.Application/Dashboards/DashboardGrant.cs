using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Parameters;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Dashboards;

/// <summary>
/// The saved queries a caller may use during one request because they came through a dashboard they can see. Empty
/// until <see cref="DashboardGrantResolver"/> proves the dashboard, tile and chart belong together; it never outlives
/// the request and is never consulted by a path that opens a query for reading, running or exporting.
/// </summary>
public interface IDashboardGrant
{
    void Allow(IEnumerable<Guid> savedQueryIds);
    bool Covers(Guid savedQueryId);
}

public sealed class DashboardGrant : IDashboardGrant
{
    private readonly HashSet<Guid> _covered = [];

    public void Allow(IEnumerable<Guid> savedQueryIds) => _covered.UnionWith(savedQueryIds);

    public bool Covers(Guid savedQueryId) => _covered.Contains(savedQueryId);
}

public sealed class DashboardGrantResolver(IDashboardRepository dashboards, IDashboardGrant grant)
{
    /// <summary>
    /// Hub mode only. True (and the grant filled) when the dashboard loads in the caller's app, the caller can see it, the
    /// tile is a chart tile of that dashboard, it points at <paramref name="visualizationId"/> when one is given, and that
    /// visualization belongs to <paramref name="query"/>. The grant then covers that query and the source queries of
    /// the query-list parameters named by that query and by the dashboard — nothing else.
    /// </summary>
    public async Task<bool> TryGrantAsync(
        Guid dashboardId, Guid tileId, SavedQuery query, Guid? visualizationId, AccessActor actor, CancellationToken cancellationToken)
    {
        if (!actor.InHub)
        {
            return false;
        }

        var dashboard = await dashboards.GetByIdAsync(dashboardId, cancellationToken);
        if (dashboard is null || !DashboardAccess.CanView(dashboard, actor))
        {
            return false;
        }

        var tile = dashboard.Tiles.FirstOrDefault(t => t.Id == tileId && t.Kind == DashboardTileKind.Chart);
        if (tile?.VisualizationId is not { } tileVisualizationId || tile.Visualization is not { } visualization)
        {
            return false;
        }

        if (visualizationId is { } wanted && wanted != tileVisualizationId)
        {
            return false;
        }

        if (visualization.SavedQueryId != query.Id)
        {
            return false;
        }

        var covered = new List<Guid> { query.Id };
        covered.AddRange(QueryDefinitionSerializer.Deserialize(query.DefinitionJson).Parameters
            .Where(p => ParameterTypes.Effective(p) == ParameterType.QueryList && p.OptionsQueryId is { } id && id != Guid.Empty)
            .Select(p => p.OptionsQueryId!.Value));
        covered.AddRange(DashboardParameterStore.ReadParameters(dashboard.ParametersJson)
            .Where(p => p.Type == ParameterType.QueryList && p.OptionsQueryId is { } id && id != Guid.Empty)
            .Select(p => p.OptionsQueryId!.Value));

        grant.Allow(covered);
        return true;
    }
}
