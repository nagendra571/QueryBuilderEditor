using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.SavedQueries;

public sealed record SetQueryDisabledCommand(Guid Id) : IRequest<bool>;

public sealed class SetQueryDisabledCommandHandler(
    ISavedQueryRepository repository, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<SetQueryDisabledCommand, bool>
{
    public async Task<bool> Handle(SetQueryDisabledCommand request, CancellationToken cancellationToken)
    {
        var existing = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.Id);

        if (existing.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenException("Only the owner can disable or enable this query.");
        }

        existing.IsDisabled = !existing.IsDisabled;
        repository.Update(existing);
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(
                existing.IsDisabled ? AuditAction.QueryDisabled : AuditAction.QueryEnabled,
                nameof(SavedQuery),
                existing.Id,
                existing.Name,
                existing.DataSourceId,
                existing.IsDisabled ? $"Disabled query '{existing.Name}'" : $"Enabled query '{existing.Name}'"),
            cancellationToken);

        return existing.IsDisabled;
    }
}
