using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dtos;

namespace QueryBuilder.Application.Queries.SavedQueries;

public sealed record GetSavedQueriesQuery : IRequest<List<SavedQuerySummaryDto>>;

public sealed class GetSavedQueriesQueryHandler(ISavedQueryRepository repository, ICurrentUserService currentUser)
    : IRequestHandler<GetSavedQueriesQuery, List<SavedQuerySummaryDto>>
{
    public async Task<List<SavedQuerySummaryDto>> Handle(GetSavedQueriesQuery request, CancellationToken cancellationToken)
    {
        var queries = await repository.GetForUserAsync(currentUser.UserId, cancellationToken);

        return queries
            .OrderByDescending(q => q.UpdatedAtUtc ?? q.CreatedAtUtc)
            .Select(q => new SavedQuerySummaryDto(
                q.Id,
                q.Name,
                q.Description,
                q.DataSourceId,
                q.DataSource?.Name ?? string.Empty,
                q.OwnerId,
                q.OwnerName,
                q.OwnerId == currentUser.UserId,
                q.IsFavorite,
                q.CreatedAtUtc,
                q.UpdatedAtUtc))
            .ToList();
    }
}
