using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;

namespace QueryBuilder.Application.DataSourceManagement;

public sealed record MoveDataSourceResultDto(Guid DataSourceId, Guid FromAppId, Guid ToAppId);

/// <summary>
/// Hands a data source over to another app. Runs inside a hub-wide scope (see <see cref="MoveDataSourceCommandHandler"/>),
/// refuses while anything still uses the source, and writes the move and its two audit entries in one save.
/// </summary>
public interface IDataSourceMover
{
    Task<MoveDataSourceResultDto> MoveAsync(Guid appId, Guid dataSourceId, Guid targetAppId, CancellationToken ct);
}

public sealed record MoveDataSourceCommand(Guid AppId, Guid DataSourceId, Guid? TargetAppId) : IRequest<MoveDataSourceResultDto>;

public sealed class MoveDataSourceCommandHandler(ICurrentUserService user, IHubWideScopeFactory factory)
    : IRequestHandler<MoveDataSourceCommand, MoveDataSourceResultDto>
{
    public async Task<MoveDataSourceResultDto> Handle(MoveDataSourceCommand request, CancellationToken ct)
    {
        if (!user.IsSuperAdmin)
        {
            throw new ForbiddenException("Only a super admin may move a data source between apps.");
        }

        var target = request.TargetAppId is { } t && t != Guid.Empty
            ? t
            : throw new CatalogValidationException("Choose the app to move the data source to.");

        await using var scope = factory.Create();
        return await scope.ServiceProvider.GetRequiredService<IDataSourceMover>()
            .MoveAsync(request.AppId, request.DataSourceId, target, ct);
    }
}
