using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Dashboards;

/// <summary>Hub mode only. Publishing shows a dashboard read-only to every Viewer and Author of the app; only an Admin or
/// a super admin may change it.</summary>
public sealed record PublishDashboardCommand(Guid DashboardId, bool Published) : IRequest<bool>;

public sealed class PublishDashboardCommandHandler(
    IDashboardRepository dashboards, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<PublishDashboardCommand, bool>
{
    /// <returns>Whether anything changed.</returns>
    public async Task<bool> Handle(PublishDashboardCommand request, CancellationToken cancellationToken)
    {
        // Embedded mode has no role, and CanView trusts the flag, so it must never be settable there.
        if (currentUser.Role is null)
        {
            throw new NotFoundException(nameof(Dashboard), request.DashboardId);
        }

        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!(currentUser.IsSuperAdmin || currentUser.Role == HubRole.Admin))
        {
            throw new ForbiddenException("Only an administrator can publish a dashboard.");
        }

        if (dashboard.PublishedToApp == request.Published)
        {
            return false;
        }

        dashboard.PublishedToApp = request.Published;
        dashboard.PublishedBy = request.Published ? currentUser.UserId : null;
        dashboard.PublishedAtUtc = request.Published ? DateTimeOffset.UtcNow : null;
        await dashboards.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(request.Published ? AuditAction.DashboardPublished : AuditAction.DashboardUnpublished,
                nameof(Dashboard), dashboard.Id, dashboard.Name, null,
                request.Published ? $"Published dashboard '{dashboard.Name}' to the app" : $"Unpublished dashboard '{dashboard.Name}'"),
            cancellationToken);
        return true;
    }
}
