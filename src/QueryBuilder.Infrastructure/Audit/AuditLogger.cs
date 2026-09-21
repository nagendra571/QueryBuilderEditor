using System.Text.Json;
using Microsoft.AspNetCore.Http;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Audit;

public sealed class AuditLogger(AppDbContext db, ICurrentUserService currentUser, IHttpContextAccessor httpContextAccessor)
    : IAuditLogger
{
    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        var record = new AuditLogEntry
        {
            Actor = currentUser.UserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            EntityName = entry.EntityName,
            DataSourceId = entry.DataSourceId,
            Summary = entry.Summary,
            DetailsJson = entry.Details is null ? null : JsonSerializer.Serialize(entry.Details, QueryDefinitionSerializer.Options),
            IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        };

        db.AuditLogEntries.Add(record);

        // A dedicated SaveChanges: audit writes must not be rolled back by, or interfere with,
        // whatever the caller's own SaveChanges call is doing.
        await db.SaveChangesAsync(cancellationToken);
    }
}
