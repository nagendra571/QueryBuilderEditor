using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.DataSources;

public sealed record GetCatalogQuery(Guid DataSourceId) : IRequest<DataSourceCatalog>;

public sealed class GetCatalogQueryHandler(IDataCatalogService catalogService)
    : IRequestHandler<GetCatalogQuery, DataSourceCatalog>
{
    public Task<DataSourceCatalog> Handle(GetCatalogQuery request, CancellationToken cancellationToken) =>
        catalogService.GetCatalogAsync(request.DataSourceId, cancellationToken);
}
