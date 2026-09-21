using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.SavedQueries;

public sealed record DeleteSavedQueryCommand(Guid Id) : IRequest<Unit>;

public sealed class DeleteSavedQueryCommandHandler(
    ISavedQueryRepository repository, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<DeleteSavedQueryCommand, Unit>
{
    public async Task<Unit> Handle(DeleteSavedQueryCommand request, CancellationToken cancellationToken)
    {
        var existing = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.Id);

        if (existing.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenException("Only the owner can delete this query.");
        }

        // Snapshot the name before the row disappears — the audit trail must still read cleanly afterward.
        var name = existing.Name;
        var dataSourceId = existing.DataSourceId;

        repository.Remove(existing);
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.QueryDeleted, nameof(SavedQuery), request.Id, name, dataSourceId, $"Deleted query '{name}'"),
            cancellationToken);

        return Unit.Value;
    }
}
