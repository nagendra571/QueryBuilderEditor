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
            SuperAdminMask.Actor(query.OwnerId, actor),
            SuperAdminMask.Name(query.OwnerId, query.OwnerName, actor),
            SavedQueryAccess.IsOwner(query, actor),
            SavedQueryAccess.AccessLevelFor(query, actor),
            query.IsFavorite,
            query.IsDisabled,
            QueryDefinitionSerializer.Deserialize(query.DefinitionJson),
            query.CreatedAtUtc,
            query.UpdatedAtUtc,
            ParseLayout(query.LayoutJson));
    }

    // A bad stored value must never fail the query page; the grid just starts plain.
    private static System.Text.Json.JsonElement? ParseLayout(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
