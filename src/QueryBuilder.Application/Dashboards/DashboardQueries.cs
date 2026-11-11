using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Dashboards;

public sealed record GetDashboardsQuery : IRequest<List<DashboardSummaryDto>>;

public sealed class GetDashboardsQueryHandler(IDashboardRepository dashboards, ICurrentUserService currentUser)
    : IRequestHandler<GetDashboardsQuery, List<DashboardSummaryDto>>
{
    public async Task<List<DashboardSummaryDto>> Handle(GetDashboardsQuery request, CancellationToken cancellationToken)
    {
        var actor = AccessActor.From(currentUser);
        var items = await dashboards.ListForUserAsync(actor, cancellationToken);

        // Re-checked here so the list stays correct even if a repository returns more than it should.
        return items
            .Where(d => DashboardAccess.CanView(d, actor))
            .OrderByDescending(d => d.UpdatedAtUtc ?? d.CreatedAtUtc)
            .Select(d => DashboardMapper.ToSummary(d, actor))
            .ToList();
    }
}

public sealed record GetDashboardQuery(Guid DashboardId) : IRequest<DashboardDetailDto>;

/// <summary>Never returns tile data rows — only structure. Charts load through the visualization
/// render endpoint, which re-checks the viewer's query access and row scope on every call.</summary>
public sealed class GetDashboardQueryHandler(IDashboardRepository dashboards, ICurrentUserService currentUser)
    : IRequestHandler<GetDashboardQuery, DashboardDetailDto>
{
    public async Task<DashboardDetailDto> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!DashboardAccess.CanView(dashboard, AccessActor.From(currentUser)))
        {
            throw new ForbiddenException("You do not have access to this dashboard.");
        }

        return DashboardMapper.ToDetail(dashboard, AccessActor.From(currentUser));
    }
}

public sealed record GetPickableVisualizationsQuery : IRequest<List<PickableVisualizationDto>>;

/// <summary>Charts offered when adding a chart tile: only those on queries the caller can view.</summary>
public sealed class GetPickableVisualizationsQueryHandler(
    ISavedQueryRepository savedQueries, IVisualizationRepository visualizations, ICurrentUserService currentUser)
    : IRequestHandler<GetPickableVisualizationsQuery, List<PickableVisualizationDto>>
{
    public async Task<List<PickableVisualizationDto>> Handle(GetPickableVisualizationsQuery request, CancellationToken cancellationToken)
    {
        var actor = AccessActor.From(currentUser);
        var queries = (await savedQueries.ListForUserAsync(actor, cancellationToken))
            .Where(q => SavedQueryAccess.CanView(q, actor))
            .ToDictionary(q => q.Id);
        if (queries.Count == 0)
        {
            return [];
        }

        var items = await visualizations.GetForQueriesAsync(queries.Keys.ToList(), cancellationToken);
        return items
            .Where(v => queries.ContainsKey(v.SavedQueryId))
            .OrderBy(v => queries[v.SavedQueryId].Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.DisplayOrder)
            .Select(v => new PickableVisualizationDto(v.Id, v.Name, v.Type, v.SavedQueryId, queries[v.SavedQueryId].Name))
            .ToList();
    }
}

/// <summary>Hub mode only. The chart a tile draws and its parameter controls, for a caller who may see the dashboard but
/// not necessarily the query behind the tile.</summary>
public sealed record GetTileChartQuery(Guid DashboardId, Guid TileId) : IRequest<TileChartDto>;

public sealed class GetTileChartQueryHandler(IDashboardRepository dashboards, ICurrentUserService currentUser)
    : IRequestHandler<GetTileChartQuery, TileChartDto>
{
    public async Task<TileChartDto> Handle(GetTileChartQuery request, CancellationToken cancellationToken)
    {
        var actor = AccessActor.From(currentUser);
        if (!actor.InHub)
        {
            throw new NotFoundException(nameof(Dashboard), request.DashboardId);
        }

        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);
        if (!DashboardAccess.CanView(dashboard, actor))
        {
            throw new ForbiddenException("You do not have access to this dashboard.");
        }

        var tile = dashboard.Tiles.FirstOrDefault(t => t.Id == request.TileId && t.Kind == DashboardTileKind.Chart);
        if (tile?.Visualization is not { SavedQuery: { } query } visualization)
        {
            throw new NotFoundException(nameof(DashboardTile), request.TileId);
        }

        var parameters = QueryDefinitionSerializer.Deserialize(query.DefinitionJson).Parameters
            .Select(p => new TileChartParameterDto(
                p.Name, p.Label, p.DataType, Parameters.ParameterTypes.Effective(p), p.DefaultValue, p.IsRequired,
                [.. p.Options], p.AllowMultiple))
            .ToList();

        return new TileChartDto(
            query.Id,
            SavedQueryAccess.CanView(query, actor) ? query.Name : null,
            query.IsDisabled,
            new TileChartVisualizationDto(visualization.Id, visualization.Name, visualization.Type,
                VisualizationOptionsSerializer.Deserialize(visualization.OptionsJson)),
            parameters);
    }
}

/// <summary>Hub mode only. Choices for a query-list parameter of the dashboard itself, drawn from its source query as the
/// caller (their scope applies), even when they cannot open that query.</summary>
public sealed record GetDashboardParameterOptionsQuery(Guid DashboardId, string ParameterName) : IRequest<List<Parameters.ParameterOption>>;

public sealed class GetDashboardParameterOptionsQueryHandler(
    IDashboardRepository dashboards, ICurrentUserService currentUser, IDashboardGrant grant, Parameters.IParameterOptionsService optionsService)
    : IRequestHandler<GetDashboardParameterOptionsQuery, List<Parameters.ParameterOption>>
{
    public async Task<List<Parameters.ParameterOption>> Handle(GetDashboardParameterOptionsQuery request, CancellationToken cancellationToken)
    {
        var actor = AccessActor.From(currentUser);
        if (!actor.InHub)
        {
            throw new NotFoundException(nameof(Dashboard), request.DashboardId);
        }

        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);
        if (!DashboardAccess.CanView(dashboard, actor))
        {
            throw new ForbiddenException("You do not have access to this dashboard.");
        }

        var parameter = DashboardParameterStore.ReadParameters(dashboard.ParametersJson)
            .FirstOrDefault(p => string.Equals(p.Name, request.ParameterName, StringComparison.Ordinal))
            ?? throw new NotFoundException(nameof(DashboardParameterDto), request.ParameterName);
        if (parameter.Type != ParameterType.QueryList || parameter.OptionsQueryId is not { } sourceId || sourceId == Guid.Empty)
        {
            throw new CatalogValidationException($"{parameter.Name} does not take its choices from a query.");
        }

        grant.Allow([sourceId]);
        var asQuery = new Domain.Model.QueryParameter
        {
            Name = parameter.Name, Label = parameter.Title ?? parameter.Name, Type = parameter.Type,
            OptionsQueryId = sourceId, OptionsLabelColumn = parameter.OptionsLabelColumn, OptionsValueColumn = parameter.OptionsValueColumn
        };
        return (await optionsService.GetOptionsAsync(asQuery, cancellationToken)).ToList();
    }
}
