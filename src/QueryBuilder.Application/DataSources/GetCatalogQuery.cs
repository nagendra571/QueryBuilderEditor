using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.DataSources;

public sealed record GetCatalogQuery(Guid DataSourceId) : IRequest<DataSourceCatalog>;

public sealed class GetCatalogQueryHandler(
    IDataCatalogService catalogService, IDataScopeGuard dataScopeGuard, IDataSourceRepository dataSources)
    : IRequestHandler<GetCatalogQuery, DataSourceCatalog>
{
    public async Task<DataSourceCatalog> Handle(GetCatalogQuery request, CancellationToken cancellationToken)
    {
        // The row is read fresh and first: the cached catalog can predate a turn-off, and a cache miss connects.
        var row = await dataSources.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.DataSource), request.DataSourceId);
        if (!row.IsActive)
        {
            throw new CatalogValidationException(DataSourceMessages.TurnedOff);
        }

        var catalog = await catalogService.GetCatalogAsync(request.DataSourceId, cancellationToken);
        return await dataScopeGuard.FilterCatalogAsync(catalog, cancellationToken);
    }
}
