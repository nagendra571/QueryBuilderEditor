using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Parameters;

/// <summary>Choices for a query-list parameter declared on a saved query.</summary>
public sealed record GetParameterOptionsQuery(Guid SavedQueryId, string ParameterName, Guid? DashboardId = null, Guid? TileId = null) : IRequest<List<ParameterOption>>;

public sealed class GetParameterOptionsQueryHandler(
    ISavedQueryRepository savedQueries,
    ICurrentUserService currentUser,
    IParameterOptionsService optionsService,
    Dashboards.IDashboardGrant dashboardGrant,
    Dashboards.DashboardGrantResolver grantResolver)
    : IRequestHandler<GetParameterOptionsQuery, List<ParameterOption>>
{
    public async Task<List<ParameterOption>> Handle(GetParameterOptionsQuery request, CancellationToken cancellationToken)
    {
        var query = await savedQueries.GetByIdAsync(request.SavedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.SavedQueryId);
        var actor = AccessActor.From(currentUser);
        if (request.DashboardId is { } dashboardId && request.TileId is { } tileId)
        {
            await grantResolver.TryGrantAsync(dashboardId, tileId, query.Id, null, actor, cancellationToken);
        }

        if (!(SavedQueryAccess.CanView(query, actor) || dashboardGrant.Covers(query.Id)))
        {
            throw new ForbiddenException("You do not have access to this query.");
        }

        var parameter = QueryDefinitionSerializer.Deserialize(query.DefinitionJson).Parameters
            .FirstOrDefault(p => string.Equals(p.Name, request.ParameterName, StringComparison.Ordinal))
            ?? throw new NotFoundException(nameof(QueryParameter), request.ParameterName);
        if (ParameterTypes.Effective(parameter) != Domain.Enums.ParameterType.QueryList)
        {
            throw new CatalogValidationException($"{ParameterTypes.Title(parameter)} does not take its choices from a query.");
        }

        return (await optionsService.GetOptionsAsync(parameter, cancellationToken)).ToList();
    }
}

/// <summary>Choices for a query-list parameter that is still being edited (not saved yet), so the
/// settings dialog can preview them. Same access rules: the caller must be able to view the source query.</summary>
public sealed record PreviewParameterOptionsQuery(QueryParameter Parameter) : IRequest<List<ParameterOption>>;

public sealed class PreviewParameterOptionsQueryHandler(IParameterOptionsService optionsService)
    : IRequestHandler<PreviewParameterOptionsQuery, List<ParameterOption>>
{
    public async Task<List<ParameterOption>> Handle(PreviewParameterOptionsQuery request, CancellationToken cancellationToken) =>
        (await optionsService.GetOptionsAsync(request.Parameter, cancellationToken)).ToList();
}
