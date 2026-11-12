using System.Text.Json;
using System.Text.Json.Nodes;
using QueryBuilder.Application.Visualizations;
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
        // Staff object ids are not the app's business (hub Decision 2): only a super admin sees who the staff member was.
        var mask = AccessActor.From(currentUser) is { InHub: true, IsSuperAdmin: false };

        // Scoped to one saved query: anyone with access to the query can see its history.
        if (request.EntityType == "SavedQuery" && request.EntityId is { } queryId)
        {
            var savedQuery = await savedQueryRepository.GetByIdAsync(queryId, cancellationToken)
                ?? throw new NotFoundException(nameof(SavedQuery), queryId);

            var hasAccess = SavedQueryAccess.CanView(savedQuery, AccessActor.From(currentUser));
            if (!hasAccess)
            {
                throw new ForbiddenException("You do not have access to this query's history.");
            }

            var scoped = await auditLogRepository.QueryAsync(new AuditLogFilter("SavedQuery", queryId, request.DataSourceId), cancellationToken);
            return scoped.Select(e => ToDto(e, mask)).ToList();
        }

        // No specific entity requested: a caller who sees the whole app (hub Admin, super admin viewing it) gets the app's
        // audit, everyone else only their own activity.
        var all = await auditLogRepository.QueryAsync(new AuditLogFilter(request.EntityType, null, request.DataSourceId), cancellationToken);
        var wholeApp = AccessActor.From(currentUser).SeesWholeApp;
        return all.Where(e => wholeApp || e.Actor == currentUser.UserId).Select(e => ToDto(e, mask)).ToList();
    }

    public const string SuperAdminLabel = "Super admin";

    private static AuditLogEntryDto ToDto(AuditLogEntry e, bool mask) => new(
        e.Id, e.TimestampUtc, mask ? MaskActor(e.Actor) : e.Actor, e.Action, e.EntityType, e.EntityId, e.EntityName, e.DataSourceId, e.Summary,
        mask ? MaskDetails(e.DetailsJson) : e.DetailsJson, e.IpAddress);

    private static string MaskActor(string actor) => actor.StartsWith("super:", StringComparison.Ordinal) ? SuperAdminLabel : actor;

    private static readonly HashSet<string> StaffIdKeys = new(StringComparer.OrdinalIgnoreCase) { "oid", "objectId" };

    /// <summary>Drops any <c>oid</c>/<c>objectId</c> property at any depth; text that is not JSON is left alone.</summary>
    internal static string? MaskDetails(string? json)
    {
        if (string.IsNullOrEmpty(json) || !json.Contains("oid", StringComparison.OrdinalIgnoreCase) && !json.Contains("objectId", StringComparison.OrdinalIgnoreCase))
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            Strip(node);
            return node?.ToJsonString();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(StaffIdKeys.Contains).ToList())
                {
                    obj.Remove(key);
                }

                foreach (var child in obj.Select(p => p.Value).ToList())
                {
                    Strip(child);
                }

                break;
            case JsonArray arr:
                foreach (var child in arr)
                {
                    Strip(child);
                }

                break;
        }
    }
}
