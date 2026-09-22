using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;

namespace QueryBuilder.Application.DataSources;

public sealed record GetAppUsersQuery(Guid DataSourceId) : IRequest<AppUsersResult>;

public sealed class GetAppUsersQueryHandler(IAppUsersDirectory appUsersDirectory)
    : IRequestHandler<GetAppUsersQuery, AppUsersResult>
{
    public Task<AppUsersResult> Handle(GetAppUsersQuery request, CancellationToken cancellationToken) =>
        appUsersDirectory.GetUsersAsync(request.DataSourceId, cancellationToken);
}
