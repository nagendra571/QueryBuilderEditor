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
                AccessLevelFor(q, currentUser.UserId),
                q.IsFavorite,
                q.IsDisabled,
                q.CreatedAtUtc,
                q.UpdatedAtUtc))
            .ToList();
    }

    internal static QueryAccessLevel AccessLevelFor(Domain.Entities.SavedQuery query, string userId) =>
        query.OwnerId == userId
            ? QueryAccessLevel.Owner
            : query.Shares.FirstOrDefault(s => s.SharedWithUserId == userId)?.AccessLevel ?? QueryAccessLevel.Viewer;
}
