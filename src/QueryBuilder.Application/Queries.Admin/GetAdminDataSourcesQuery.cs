using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dtos;

namespace QueryBuilder.Application.Queries.Admin;

public sealed record GetAdminDataSourcesQuery : IRequest<List<AdminDataSourceSummaryDto>>;

public sealed class GetAdminDataSourcesQueryHandler(IDataSourceRepository repository)
    : IRequestHandler<GetAdminDataSourcesQuery, List<AdminDataSourceSummaryDto>>
{
    public async Task<List<AdminDataSourceSummaryDto>> Handle(GetAdminDataSourcesQuery request, CancellationToken cancellationToken)
    {
        var sources = await repository.GetAllAsync(cancellationToken);
        return sources.Select(s => new AdminDataSourceSummaryDto(s.Id, s.Name, s.Provider, s.IsActive)).ToList();
    }
}
