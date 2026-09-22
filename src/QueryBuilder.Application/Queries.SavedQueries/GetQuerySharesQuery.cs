using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Queries.SavedQueries;

public sealed record GetQuerySharesQuery(Guid SavedQueryId) : IRequest<List<QueryShareDto>>;

public sealed class GetQuerySharesQueryHandler(ISavedQueryRepository repository, ICurrentUserService currentUser)
    : IRequestHandler<GetQuerySharesQuery, List<QueryShareDto>>
{
    public async Task<List<QueryShareDto>> Handle(GetQuerySharesQuery request, CancellationToken cancellationToken)
    {
        var query = await repository.GetByIdAsync(request.SavedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.SavedQueryId);

        if (query.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenException("Only the owner can view sharing for this query.");
        }

        return query.Shares
            .OrderBy(s => s.SharedWithUserEmail)
            .Select(s => new QueryShareDto(s.Id, s.SharedWithUserId, s.SharedWithUserEmail, s.AccessLevel, s.CreatedAtUtc))
            .ToList();
    }
}
