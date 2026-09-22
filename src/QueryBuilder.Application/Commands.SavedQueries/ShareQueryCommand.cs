using FluentValidation;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.SavedQueries;

/// <summary>Grants (or updates) another user's access to a saved query. Owner-only. Sharing with
/// someone already shared with just updates their access level rather than duplicating the row.</summary>
public sealed record ShareQueryCommand(
    Guid SavedQueryId,
    string SharedWithUserId,
    string SharedWithUserEmail,
    QueryAccessLevel AccessLevel) : IRequest<Guid>;

public sealed class ShareQueryCommandValidator : AbstractValidator<ShareQueryCommand>
{
    public ShareQueryCommandValidator()
    {
        RuleFor(x => x.SavedQueryId).NotEmpty();
        RuleFor(x => x.SharedWithUserId).NotEmpty().MaximumLength(450);
        RuleFor(x => x.SharedWithUserEmail).NotEmpty().MaximumLength(256);
        RuleFor(x => x.AccessLevel)
            .Must(level => level is QueryAccessLevel.Viewer or QueryAccessLevel.Editor)
            .WithMessage("A share can only grant Viewer or Editor access.");
    }
}

public sealed class ShareQueryCommandHandler(
    ISavedQueryRepository repository, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<ShareQueryCommand, Guid>
{
    public async Task<Guid> Handle(ShareQueryCommand request, CancellationToken cancellationToken)
    {
        var query = await repository.GetByIdAsync(request.SavedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.SavedQueryId);

        if (query.OwnerId != currentUser.UserId)
        {
            throw new ForbiddenException("Only the owner can share this query.");
        }

        if (request.SharedWithUserId == query.OwnerId)
        {
            throw new CatalogValidationException("The owner already has full access — no need to share with themselves.");
        }

        var share = query.Shares.FirstOrDefault(s => s.SharedWithUserId == request.SharedWithUserId);
        if (share is null)
        {
            share = new QueryShare
            {
                SavedQueryId = query.Id,
                SharedWithUserId = request.SharedWithUserId,
                SharedWithUserEmail = request.SharedWithUserEmail,
                AccessLevel = request.AccessLevel,
                CreatedBy = currentUser.UserId,
            };
            await repository.AddShareAsync(share, cancellationToken);
        }
        else
        {
            share.SharedWithUserEmail = request.SharedWithUserEmail;
            share.AccessLevel = request.AccessLevel;
            share.UpdatedBy = currentUser.UserId;
            share.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await repository.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.QueryShared, nameof(SavedQuery), query.Id, query.Name, query.DataSourceId,
                $"Shared query '{query.Name}' with {request.SharedWithUserEmail} ({request.AccessLevel})"),
            cancellationToken);

        return share.Id;
    }
}
