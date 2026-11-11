using QueryBuilder.Application.Visualizations;
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

        var actor = AccessActor.From(currentUser);
        if (!SavedQueryAccess.CanView(query, actor))
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
            SavedQueryAccess.IsOwner(query, actor),
            SavedQueryAccess.AccessLevelFor(query, actor),
            query.IsFavorite,
            query.IsDisabled,
            QueryDefinitionSerializer.Deserialize(query.DefinitionJson),
            query.CreatedAtUtc,
            query.UpdatedAtUtc);
    }
}
