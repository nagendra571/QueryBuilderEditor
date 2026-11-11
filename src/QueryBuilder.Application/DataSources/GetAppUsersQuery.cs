using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Hub;

namespace QueryBuilder.Application.DataSources;

public sealed record GetAppUsersQuery(Guid DataSourceId) : IRequest<AppUsersResult>;

public sealed class GetAppUsersQueryHandler(IAppUsersDirectory appUsersDirectory, ICurrentUserService currentUser, ShareTargets targets)
    : IRequestHandler<GetAppUsersQuery, AppUsersResult>
{
    public async Task<AppUsersResult> Handle(GetAppUsersQuery request, CancellationToken cancellationToken)
    {
        if (!AccessActor.From(currentUser).InHub)
        {
            return await appUsersDirectory.GetUsersAsync(request.DataSourceId, cancellationToken);
        }

        // The directory call also proves the data source belongs to the caller's app (a missing row is a 404).
        var users = await targets.MergedAsync(request.DataSourceId, cancellationToken);
        return new AppUsersResult(Available: true, users);
    }
}

/// <summary>Hub mode only: the people launched into the caller's app, for sharing a dashboard.</summary>
public sealed record GetPeopleQuery : IRequest<List<AppUserDto>>;

public sealed class GetPeopleQueryHandler(ShareTargets targets) : IRequestHandler<GetPeopleQuery, List<AppUserDto>>
{
    public Task<List<AppUserDto>> Handle(GetPeopleQuery request, CancellationToken cancellationToken) =>
        targets.PeopleAsync(cancellationToken);
}
