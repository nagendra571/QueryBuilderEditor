using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.DataSourceManagement;

public sealed record SetDataSourceLockCommand(Guid Id, bool Locked) : IRequest<Unit>;

public sealed class SetDataSourceLockCommandHandler(
    IDataSourceRepository repository,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<SetDataSourceLockCommand, Unit>
{
    public async Task<Unit> Handle(SetDataSourceLockCommand request, CancellationToken cancellationToken)
    {
        var actor = AccessActor.From(currentUser);
        // Embedded mode has no lock: the route does not exist there.
        if (!actor.InHub)
        {
            throw new NotFoundException("Locking data sources is only available in hub mode.");
        }

        // Looked up first so another app's id is a 404 for everyone; only then is the role checked.
        var dataSource = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.Id);

        if (!actor.IsSuperAdmin)
        {
            throw new ForbiddenException("Only a super admin can lock or unlock a data source.");
        }

        // Repeating the current state changes nothing and so writes no audit entry.
        if (dataSource.LockedBySuperAdmin == request.Locked)
        {
            return Unit.Value;
        }

        dataSource.LockedBySuperAdmin = request.Locked;
        dataSource.UpdatedBy = currentUser.UserId;
        dataSource.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.UpdateOnly(dataSource, nameof(DataSource.LockedBySuperAdmin));
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.DataSourceLockChanged, nameof(DataSource), dataSource.Id, dataSource.Name, dataSource.Id,
                $"{(request.Locked ? "Locked" : "Unlocked")} data source '{dataSource.Name}'",
                new { dataSource.Name, Locked = request.Locked }),
            cancellationToken);

        return Unit.Value;
    }
}
