using FluentValidation;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Commands.SavedQueries;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Dashboards;

public sealed record GetDashboardSharesQuery(Guid DashboardId) : IRequest<List<QueryShareDto>>;

public sealed class GetDashboardSharesQueryHandler(IDashboardRepository dashboards, ICurrentUserService currentUser)
    : IRequestHandler<GetDashboardSharesQuery, List<QueryShareDto>>
{
    public async Task<List<QueryShareDto>> Handle(GetDashboardSharesQuery request, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!DashboardAccess.IsOwner(dashboard, currentUser.UserId))
        {
            throw new ForbiddenException("Only the owner can view sharing for this dashboard.");
        }

        return dashboard.Shares
            .OrderBy(s => s.SharedWithUserEmail)
            .Select(s => new QueryShareDto(s.Id, s.SharedWithUserId, s.SharedWithUserEmail, s.AccessLevel, s.CreatedAtUtc))
            .ToList();
    }
}

public sealed record ShareDashboardCommand(
    Guid DashboardId,
    string SharedWithUserId,
    string SharedWithUserEmail,
    QueryAccessLevel AccessLevel,
    IReadOnlyList<Guid>? AlsoShareQueryIds) : IRequest<Guid>;

public sealed class ShareDashboardCommandValidator : AbstractValidator<ShareDashboardCommand>
{
    public ShareDashboardCommandValidator()
    {
        RuleFor(x => x.DashboardId).NotEmpty();
        RuleFor(x => x.SharedWithUserId).NotEmpty().MaximumLength(450);
        RuleFor(x => x.SharedWithUserEmail).NotEmpty().MaximumLength(256);
        RuleFor(x => x.AccessLevel)
            .Must(level => level is QueryAccessLevel.Viewer or QueryAccessLevel.Editor)
            .WithMessage("A share can only grant Viewer or Editor access.");
    }
}

public sealed class ShareDashboardCommandHandler(
    IDashboardRepository dashboards,
    ISavedQueryRepository savedQueries,
    IRequestHandler<ShareQueryCommand, Guid> shareQuery,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<ShareDashboardCommand, Guid>
{
    public async Task<Guid> Handle(ShareDashboardCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!DashboardAccess.IsOwner(dashboard, userId))
        {
            throw new ForbiddenException("Only the owner can share this dashboard.");
        }

        if (request.SharedWithUserId == dashboard.OwnerId)
        {
            throw new CatalogValidationException("The owner already has full access — no need to share with themselves.");
        }

        // Every id is checked before anything is written, so one bad id shares nothing.
        var onDashboard = dashboard.Tiles
            .Where(t => t.Visualization is not null)
            .Select(t => t.Visualization!.SavedQueryId)
            .ToHashSet();
        var queriesToShare = new List<SavedQuery>();
        foreach (var queryId in (request.AlsoShareQueryIds ?? []).Distinct())
        {
            if (!onDashboard.Contains(queryId))
            {
                throw new CatalogValidationException("A query to share is not used by a chart on this dashboard.");
            }

            var query = await savedQueries.GetByIdAsync(queryId, cancellationToken)
                ?? throw new NotFoundException(nameof(SavedQuery), queryId);
            if (query.OwnerId != userId)
            {
                throw new ForbiddenException("You can only share queries you own.");
            }

            queriesToShare.Add(query);
        }

        var share = dashboard.Shares.FirstOrDefault(s => s.SharedWithUserId == request.SharedWithUserId);
        if (share is null)
        {
            share = new DashboardShare
            {
                DashboardId = dashboard.Id,
                SharedWithUserId = request.SharedWithUserId,
                SharedWithUserEmail = request.SharedWithUserEmail,
                AccessLevel = request.AccessLevel,
                CreatedBy = userId
            };
            await dashboards.AddShareAsync(share, cancellationToken);
        }
        else
        {
            share.SharedWithUserEmail = request.SharedWithUserEmail;
            share.AccessLevel = request.AccessLevel;
            share.UpdatedBy = userId;
            share.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await dashboards.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.DashboardShared, nameof(Dashboard), dashboard.Id, dashboard.Name, null,
                $"Shared dashboard '{dashboard.Name}' with {request.SharedWithUserEmail} ({request.AccessLevel})"),
            cancellationToken);

        foreach (var query in queriesToShare)
        {
            // An existing share of any level is left alone: ShareQueryCommand would overwrite an Editor with Viewer.
            if (query.OwnerId == request.SharedWithUserId
                || query.Shares.Any(s => s.SharedWithUserId == request.SharedWithUserId))
            {
                continue;
            }

            await shareQuery.Handle(
                new ShareQueryCommand(query.Id, request.SharedWithUserId, request.SharedWithUserEmail, QueryAccessLevel.Viewer),
                cancellationToken);
        }

        return share.Id;
    }
}

public sealed record RemoveDashboardShareCommand(Guid DashboardId, Guid ShareId) : IRequest<Unit>;

public sealed class RemoveDashboardShareCommandHandler(
    IDashboardRepository dashboards, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<RemoveDashboardShareCommand, Unit>
{
    public async Task<Unit> Handle(RemoveDashboardShareCommand request, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!DashboardAccess.IsOwner(dashboard, currentUser.UserId))
        {
            throw new ForbiddenException("Only the owner can manage sharing for this dashboard.");
        }

        var share = dashboard.Shares.FirstOrDefault(s => s.Id == request.ShareId)
            ?? throw new NotFoundException(nameof(DashboardShare), request.ShareId);

        dashboards.RemoveShare(share);
        await dashboards.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.DashboardUnshared, nameof(Dashboard), dashboard.Id, dashboard.Name, null,
                $"Removed {share.SharedWithUserEmail}'s access to dashboard '{dashboard.Name}'"),
            cancellationToken);

        return Unit.Value;
    }
}

public sealed record GetDashboardSharePreviewQuery(Guid DashboardId, string UserId) : IRequest<DashboardSharePreviewDto>;

/// <summary>For the share dialog: which of the dashboard's charts would show as restricted to that
/// person, and which of those queries the caller is able to share.</summary>
public sealed class GetDashboardSharePreviewQueryHandler(IDashboardRepository dashboards, ICurrentUserService currentUser)
    : IRequestHandler<GetDashboardSharePreviewQuery, DashboardSharePreviewDto>
{
    public async Task<DashboardSharePreviewDto> Handle(GetDashboardSharePreviewQuery request, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!DashboardAccess.IsOwner(dashboard, currentUser.UserId))
        {
            throw new ForbiddenException("Only the owner can manage sharing for this dashboard.");
        }

        var inaccessible = dashboard.Tiles
            .Select(t => t.Visualization?.SavedQuery)
            .Where(q => q is not null && !SavedQueryAccess.CanView(q, request.UserId))
            .DistinctBy(q => q!.Id)
            .OrderBy(q => q!.Name, StringComparer.OrdinalIgnoreCase)
            // An Editor may have added a chart from a query the owner can't see; never reveal its
            // name or id to the owner through this preview.
            .Select(q => SavedQueryAccess.CanView(q!, currentUser.UserId)
                ? new InaccessibleQueryDto(q!.Id, q.Name, q.OwnerId == currentUser.UserId)
                : new InaccessibleQueryDto(Guid.Empty, "A chart you don't have access to", CanShare: false))
            .ToList();

        return new DashboardSharePreviewDto(inaccessible);
    }
}
