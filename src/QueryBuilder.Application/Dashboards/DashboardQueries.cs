using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Dashboards;

public sealed record GetDashboardsQuery : IRequest<List<DashboardSummaryDto>>;

public sealed class GetDashboardsQueryHandler(IDashboardRepository dashboards, ICurrentUserService currentUser)
    : IRequestHandler<GetDashboardsQuery, List<DashboardSummaryDto>>
{
    public async Task<List<DashboardSummaryDto>> Handle(GetDashboardsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var items = await dashboards.GetForUserAsync(userId, cancellationToken);

        // Re-checked here so the list stays correct even if a repository returns more than it should.
        return items
            .Where(d => DashboardAccess.CanView(d, userId))
            .OrderByDescending(d => d.UpdatedAtUtc ?? d.CreatedAtUtc)
            .Select(d => DashboardMapper.ToSummary(d, userId))
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

        if (!DashboardAccess.CanView(dashboard, currentUser.UserId))
        {
            throw new ForbiddenException("You do not have access to this dashboard.");
        }

        return DashboardMapper.ToDetail(dashboard, currentUser.UserId);
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
        var userId = currentUser.UserId;
        var queries = (await savedQueries.GetForUserAsync(userId, cancellationToken))
            .Where(q => SavedQueryAccess.CanView(q, userId))
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
