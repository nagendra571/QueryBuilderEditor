using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.SavedQueries;

/// <summary>Revokes a share. Owner-only — the durable grant, not <c>AppUsers</c>, is the source of
/// truth for access, so this is the only way access is taken away once granted.</summary>
public sealed record RemoveShareCommand(Guid SavedQueryId, Guid ShareId) : IRequest<Unit>;

public sealed class RemoveShareCommandHandler(
    ISavedQueryRepository repository, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<RemoveShareCommand, Unit>
{
    public async Task<Unit> Handle(RemoveShareCommand request, CancellationToken cancellationToken)
    {
        var query = await repository.GetByIdAsync(request.SavedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.SavedQueryId);

        if (query.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenException("Only the owner can manage sharing for this query.");
        }

        var share = query.Shares.FirstOrDefault(s => s.Id == request.ShareId)
            ?? throw new NotFoundException(nameof(QueryShare), request.ShareId);

        repository.RemoveShare(share);
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.QueryUnshared, nameof(SavedQuery), query.Id, query.Name, query.DataSourceId,
                $"Removed {share.SharedWithUserEmail}'s access to query '{query.Name}'"),
            cancellationToken);

        return Unit.Value;
    }
}
