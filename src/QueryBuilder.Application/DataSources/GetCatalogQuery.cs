using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.DataSources;

public sealed record GetCatalogQuery(Guid DataSourceId) : IRequest<DataSourceCatalog>;

public sealed class GetCatalogQueryHandler(IDataCatalogService catalogService, IDataScopeGuard dataScopeGuard)
    : IRequestHandler<GetCatalogQuery, DataSourceCatalog>
{
    public async Task<DataSourceCatalog> Handle(GetCatalogQuery request, CancellationToken cancellationToken) =>
        await dataScopeGuard.FilterCatalogAsync(
            await catalogService.GetCatalogAsync(request.DataSourceId, cancellationToken), cancellationToken);
}
