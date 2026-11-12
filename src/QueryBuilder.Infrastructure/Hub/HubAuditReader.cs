using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Hub.Registry;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

/// <summary>Only ever resolved from a hub-wide scope, which is what lets the app filter see every app's rows.</summary>
internal sealed class HubAuditReader(AppDbContext db) : IHubAuditReader
{
    public async Task<IReadOnlyList<HubAuditEntryDto>> ListAsync(Guid? appId, AuditAction? action, DateTimeOffset? before, int take, CancellationToken ct)
    {
        var query = db.AuditLogEntries.AsNoTracking().AsQueryable();
        if (appId is { } app)
        {
            query = query.Where(e => e.AppId == app);
        }

        if (action is { } a)
        {
            query = query.Where(e => e.Action == a);
        }

        if (before is { } b)
        {
            query = query.Where(e => e.TimestampUtc < b);
        }

        var rows = await query.OrderByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id).Take(take).ToListAsync(ct);
        var ids = rows.Where(r => r.AppId is not null).Select(r => r.AppId!.Value).Distinct().ToList();
        var keys = await db.Apps.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Key, ct);

        return rows.Select(e => new HubAuditEntryDto(
            e.Id, e.TimestampUtc, e.Actor, e.Action, e.EntityType, e.EntityId, e.EntityName, e.Summary, e.DetailsJson, e.IpAddress,
            e.AppId, e.AppId is { } id && keys.TryGetValue(id, out var key) ? key : null)).ToList();
    }
}
