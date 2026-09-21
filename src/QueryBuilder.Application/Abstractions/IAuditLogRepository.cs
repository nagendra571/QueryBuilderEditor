using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Abstractions;

public sealed record AuditLogFilter(string? EntityType, Guid? EntityId, Guid? DataSourceId, int Take = 100);

public interface IAuditLogRepository
{
    Task<List<AuditLogEntry>> QueryAsync(AuditLogFilter filter, CancellationToken cancellationToken);
}
