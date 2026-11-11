using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

/// <summary>Only ever resolved from a hub-wide scope, which is what lets it read and rewrite another app's row.</summary>
internal sealed class DataSourceMover(AppDbContext db, ICurrentUserService user, IHttpContextAccessor http, IMemoryCache cache, TimeProvider clock)
    : IDataSourceMover
{
    public async Task<MoveDataSourceResultDto> MoveAsync(Guid appId, Guid dataSourceId, Guid targetAppId, CancellationToken ct)
    {
        var source = await db.DataSources.FirstOrDefaultAsync(d => d.Id == dataSourceId && d.AppId == appId, ct)
            ?? throw new NotFoundException(nameof(DataSource), dataSourceId);

        // Default is the embedded-mode app and None the "no app" marker: neither is a place to hand a source to.
        var target = targetAppId == App.DefaultId || targetAppId == App.NoneId || targetAppId == appId
            ? null
            : await db.Apps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == targetAppId, ct);
        if (target is null || !target.IsEnabled)
        {
            throw new CatalogValidationException("Choose another app that is turned on.");
        }

        var from = await db.Apps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appId, ct);
        var counts = await CountUsesAsync(source, ct);
        if (counts.Values.Any(c => c > 0))
        {
            throw new InUseException(
                "This data source is still used, so it can't move. Remove or repoint what uses it first.", counts);
        }

        source.AppId = targetAppId;
        source.UpdatedBy = user.UserId;
        source.UpdatedAtUtc = clock.GetUtcNow();
        Audit(appId, source, $"Moved data source '{source.Name}' to app '{target.Name}'", new { to = target.Key, toAppId = targetAppId });
        Audit(targetAppId, source, $"Received data source '{source.Name}' from app '{from?.Name ?? appId.ToString()}'", new { from = from?.Key, fromAppId = appId });
        await db.SaveChangesAsync(ct);

        // Keyed by id alone and shared by every app: a copy left behind would still answer for the old app.
        cache.Remove($"catalog:{dataSourceId}");
        cache.Remove($"datascope:{dataSourceId}");

        return new MoveDataSourceResultDto(dataSourceId, appId, targetAppId);
    }

    private async Task<Dictionary<string, int>> CountUsesAsync(DataSource source, CancellationToken ct)
    {
        var queries = await db.SavedQueries.AsNoTracking().CountAsync(q => q.DataSourceId == source.Id, ct);
        var tiles = await db.DashboardTiles.AsNoTracking()
            .CountAsync(t => t.Visualization != null && db.SavedQueries.Any(q => q.Id == t.Visualization.SavedQueryId && q.DataSourceId == source.Id), ct);
        var rules = await db.DataScopeRules.AsNoTracking().CountAsync(r => r.DataSourceId == source.Id, ct);

        // Queries and dashboards of the same app whose parameter dropdowns read a query of this source.
        var sourceQueryIds = (await db.SavedQueries.AsNoTracking().Where(q => q.DataSourceId == source.Id).Select(q => q.Id).ToListAsync(ct)).ToHashSet();
        var dropdowns = 0;
        if (sourceQueryIds.Count > 0)
        {
            foreach (var json in await db.SavedQueries.AsNoTracking().Where(q => q.AppId == source.AppId).Select(q => q.DefinitionJson).ToListAsync(ct))
            {
                dropdowns += PointsAtAny(json, sourceQueryIds) ? 1 : 0;
            }

            foreach (var json in await db.Dashboards.AsNoTracking().Where(d => d.AppId == source.AppId).Select(d => d.ParametersJson).ToListAsync(ct))
            {
                dropdowns += PointsAtAny(json, sourceQueryIds) ? 1 : 0;
            }
        }

        return new Dictionary<string, int>
        {
            ["savedQueries"] = queries,
            ["dashboardTiles"] = tiles,
            ["parameterDropdowns"] = dropdowns,
            ["dataScopeRules"] = rules
        };
    }

    private static bool PointsAtAny(string? json, HashSet<Guid> queryIds)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return Walk(doc.RootElement, queryIds);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Walk(JsonElement element, HashSet<Guid> queryIds)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "optionsQueryId", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String
                        && Guid.TryParse(property.Value.GetString(), out var id)
                        && queryIds.Contains(id))
                    {
                        return true;
                    }

                    if (Walk(property.Value, queryIds))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(item => Walk(item, queryIds));
            default:
                return false;
        }
    }

    private void Audit(Guid appId, DataSource source, string summary, object details) =>
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = user.UserId,
            Action = AuditAction.DataSourceMoved,
            EntityType = nameof(DataSource),
            EntityId = source.Id,
            EntityName = source.Name,
            DataSourceId = source.Id,
            // Explicit on both: the entry written for the old app stays with it, which is the history the old app keeps.
            AppId = appId,
            Summary = summary.Length > 500 ? summary[..500] : summary,
            DetailsJson = JsonSerializer.Serialize(details, QueryDefinitionSerializer.Options),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            TimestampUtc = clock.GetUtcNow(),
        });
}
