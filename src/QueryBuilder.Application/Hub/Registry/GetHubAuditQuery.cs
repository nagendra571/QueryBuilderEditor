using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Hub.Registry;

public sealed record HubAuditEntryDto(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Actor,
    AuditAction Action,
    string EntityType,
    Guid? EntityId,
    string? EntityName,
    string Summary,
    string? DetailsJson,
    string? IpAddress,
    Guid? AppId,
    string? AppKey);

/// <summary>Reads the audit across every app, run inside a hub-wide scope. Rows without an app (hub-level) have a null <c>AppKey</c>.</summary>
public interface IHubAuditReader
{
    Task<IReadOnlyList<HubAuditEntryDto>> ListAsync(Guid? appId, AuditAction? action, DateTimeOffset? before, int take, CancellationToken ct);
}

public sealed record GetHubAuditQuery(Guid? AppId, AuditAction? Action, DateTimeOffset? Before, int? Take) : IRequest<IReadOnlyList<HubAuditEntryDto>>;

public sealed class GetHubAuditQueryHandler(ICurrentUserService user, IHubWideScopeFactory factory)
    : IRequestHandler<GetHubAuditQuery, IReadOnlyList<HubAuditEntryDto>>
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    public async Task<IReadOnlyList<HubAuditEntryDto>> Handle(GetHubAuditQuery request, CancellationToken ct)
    {
        if (!user.IsSuperAdmin)
        {
            throw new ForbiddenException("Only a super admin may read the hub audit.");
        }

        var take = request.Take ?? DefaultTake;
        if (take is < 1 or > MaxTake)
        {
            throw new CatalogValidationException($"take must be between 1 and {MaxTake}.");
        }

        await using var scope = factory.Create();
        var reader = (IHubAuditReader)scope.ServiceProvider.GetService(typeof(IHubAuditReader))!;
        return await reader.ListAsync(request.AppId, request.Action, request.Before, take, ct);
    }
}
