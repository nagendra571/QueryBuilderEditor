using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Audit;

public sealed record GetAuditLogQuery(string? EntityType, Guid? EntityId, Guid? DataSourceId) : IRequest<List<AuditLogEntryDto>>;

public sealed class GetAuditLogQueryHandler(
    IAuditLogRepository auditLogRepository,
    ISavedQueryRepository savedQueryRepository,
    ICurrentUserService currentUser)
    : IRequestHandler<GetAuditLogQuery, List<AuditLogEntryDto>>
{
    public async Task<List<AuditLogEntryDto>> Handle(GetAuditLogQuery request, CancellationToken cancellationToken)
    {
        // Scoped to one saved query: anyone with access to the query can see its history.
        if (request.EntityType == "SavedQuery" && request.EntityId is { } queryId)
        {
            var savedQuery = await savedQueryRepository.GetByIdAsync(queryId, cancellationToken)
                ?? throw new NotFoundException(nameof(SavedQuery), queryId);

            var hasAccess = savedQuery.OwnerId == currentUser.UserId || savedQuery.Shares.Any(s => s.SharedWithUserId == currentUser.UserId);
            if (!hasAccess)
            {
                throw new ForbiddenException("You do not have access to this query's history.");
            }

            var scoped = await auditLogRepository.QueryAsync(new AuditLogFilter("SavedQuery", queryId, request.DataSourceId), cancellationToken);
            return scoped.Select(ToDto).ToList();
        }

        // No specific entity requested — without a broader admin role yet, only ever show the caller's own activity.
        var mine = await auditLogRepository.QueryAsync(new AuditLogFilter(request.EntityType, null, request.DataSourceId), cancellationToken);
        return mine.Where(e => e.Actor == currentUser.UserId).Select(ToDto).ToList();
    }

    private static AuditLogEntryDto ToDto(AuditLogEntry e) => new(
        e.Id, e.TimestampUtc, e.Actor, e.Action, e.EntityType, e.EntityId, e.EntityName, e.DataSourceId, e.Summary, e.DetailsJson, e.IpAddress);
}
