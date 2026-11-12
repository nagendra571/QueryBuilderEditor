using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Dtos;

namespace QueryBuilder.Application.Queries.Admin;

public sealed record GetAdminDataSourcesQuery : IRequest<List<AdminDataSourceSummaryDto>>;

public sealed class GetAdminDataSourcesQueryHandler(IDataSourceRepository repository, ICurrentUserService currentUser, ICurrentAppReader apps, DataSourceManagementSettings settings)
    : IRequestHandler<GetAdminDataSourcesQuery, List<AdminDataSourceSummaryDto>>
{
    public async Task<List<AdminDataSourceSummaryDto>> Handle(GetAdminDataSourcesQuery request, CancellationToken cancellationToken)
    {
        var sources = await repository.GetAllAsync(cancellationToken);
        var actor = AccessActor.From(currentUser);
        var canManage = await DataSourceLock.CanManageConnectionsAsync(apps, actor, settings, cancellationToken);
        return sources.Select(s => new AdminDataSourceSummaryDto(
            s.Id, s.Name, s.Provider, s.IsActive, s.LockedBySuperAdmin, DataSourceLock.CanEdit(s, actor), canManage && DataSourceLock.CanEdit(s, actor))).ToList();
    }
}
