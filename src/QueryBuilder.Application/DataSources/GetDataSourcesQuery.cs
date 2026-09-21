using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dtos;

namespace QueryBuilder.Application.DataSources;

public sealed record GetDataSourcesQuery : IRequest<List<DataSourceDto>>;

public sealed class GetDataSourcesQueryHandler(IDataSourceRepository repository)
    : IRequestHandler<GetDataSourcesQuery, List<DataSourceDto>>
{
    public async Task<List<DataSourceDto>> Handle(GetDataSourcesQuery request, CancellationToken cancellationToken)
    {
        var sources = await repository.GetActiveAsync(cancellationToken);
        return sources.Select(s => new DataSourceDto(s.Id, s.Name, s.Description, s.ViewsOnly)).ToList();
    }
}
