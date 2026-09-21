using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;

namespace QueryBuilder.Application.Queries.SavedQueries;

public sealed record GetSavedQueryByIdQuery(Guid Id) : IRequest<SavedQueryDetailDto>;

public sealed class GetSavedQueryByIdQueryHandler(ISavedQueryRepository repository, ICurrentUserService currentUser)
    : IRequestHandler<GetSavedQueryByIdQuery, SavedQueryDetailDto>
{
    public async Task<SavedQueryDetailDto> Handle(GetSavedQueryByIdQuery request, CancellationToken cancellationToken)
    {
        var query = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.SavedQuery), request.Id);

        var isOwner = query.OwnerId == currentUser.UserId;
        var isSharedWithMe = query.Shares.Any(s => s.SharedWithUserId == currentUser.UserId);
        if (!isOwner && !isSharedWithMe)
        {
            throw new ForbiddenException("You do not have access to this query.");
        }

        return new SavedQueryDetailDto(
            query.Id,
            query.Name,
            query.Description,
            query.DataSourceId,
            query.DataSource?.Name ?? string.Empty,
            query.OwnerId,
            query.OwnerName,
            isOwner,
            query.IsFavorite,
            QueryDefinitionSerializer.Deserialize(query.DefinitionJson),
            query.CreatedAtUtc,
            query.UpdatedAtUtc);
    }
}
