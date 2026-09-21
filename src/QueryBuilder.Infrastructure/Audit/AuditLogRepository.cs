using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Audit;

public sealed class AuditLogRepository(AppDbContext db) : IAuditLogRepository
{
    public async Task<List<AuditLogEntry>> QueryAsync(AuditLogFilter filter, CancellationToken cancellationToken)
    {
        var query = db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(e => e.EntityType == filter.EntityType);
        }
        if (filter.EntityId is { } entityId)
        {
            query = query.Where(e => e.EntityId == entityId);
        }
        if (filter.DataSourceId is { } dataSourceId)
        {
            query = query.Where(e => e.DataSourceId == dataSourceId);
        }

        return await query
            .OrderByDescending(e => e.TimestampUtc)
            .Take(Math.Clamp(filter.Take, 1, 500))
            .ToListAsync(cancellationToken);
    }
}
