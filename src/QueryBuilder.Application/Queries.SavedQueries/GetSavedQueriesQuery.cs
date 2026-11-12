using QueryBuilder.Application.Visualizations;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Queries.SavedQueries;

public sealed record GetSavedQueriesQuery : IRequest<List<SavedQuerySummaryDto>>;

public sealed class GetSavedQueriesQueryHandler(ISavedQueryRepository repository, ICurrentUserService currentUser)
    : IRequestHandler<GetSavedQueriesQuery, List<SavedQuerySummaryDto>>
{
    public async Task<List<SavedQuerySummaryDto>> Handle(GetSavedQueriesQuery request, CancellationToken cancellationToken)
    {
        var actor = AccessActor.From(currentUser);
        var queries = await repository.ListForUserAsync(actor, cancellationToken);

        return queries
            .OrderByDescending(q => q.UpdatedAtUtc ?? q.CreatedAtUtc)
            .Select(q => new SavedQuerySummaryDto(
                q.Id,
                q.Name,
                q.Description,
                q.DataSourceId,
                q.DataSource?.Name ?? string.Empty,
                SuperAdminMask.Actor(q.OwnerId, actor),
                SuperAdminMask.Name(q.OwnerId, q.OwnerName, actor),
                SavedQueryAccess.IsOwner(q, actor),
                SavedQueryAccess.AccessLevelFor(q, actor),
                q.IsFavorite,
                q.IsDisabled,
                q.CreatedAtUtc,
                q.UpdatedAtUtc))
            .ToList();
    }
}
