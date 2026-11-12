using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Repositories;

public sealed class DataScopeRuleRepository(AppDbContext db, IMemoryCache cache) : IDataScopeRuleRepository
{
    public async Task<IReadOnlyList<DataScopeRule>> GetForDataSourceAsync(Guid dataSourceId, CancellationToken cancellationToken)
    {
        // The cache is keyed by id alone and shared by every app: check visibility first, and never read or fill the
        // cache for an id this app can't see (a miss would otherwise cache an empty list for the owning app).
        if (!await db.DataSources.AnyAsync(d => d.Id == dataSourceId, cancellationToken))
        {
            return [];
        }

        var cacheKey = CacheKey(dataSourceId);
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<DataScopeRule>? cached) && cached is not null)
        {
            return cached;
        }

        var rules = await db.DataScopeRules.AsNoTracking()
            .Where(x => x.DataSourceId == dataSourceId)
            .ToListAsync(cancellationToken);

        cache.Set<IReadOnlyList<DataScopeRule>>(cacheKey, rules, TimeSpan.FromMinutes(5));
        return rules;
    }

    // Delete-then-insert in one transaction, with new rows added directly on the DbSet (see
    // SavedQueryRepository.AddShareAsync for why never via a parent navigation collection).
    public async Task ReplaceForDataSourceAsync(Guid dataSourceId, IReadOnlyList<DataScopeRule> rules, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.DataScopeRules.Where(x => x.DataSourceId == dataSourceId).ExecuteDeleteAsync(cancellationToken);
        await db.DataScopeRules.AddRangeAsync(rules, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            // The metadata database is case-insensitive, so names a case-sensitive source tells apart (Postgres
            // "Orders" vs "orders") collide on the unique index. The transaction rolls back on dispose.
            var firstLine = sql.Message.Split('\n', 2)[0].TrimEnd('\r');
            throw new CatalogValidationException(
                $"Couldn't save: the metadata database treats two object names as the same. {firstLine}");
        }

        await transaction.CommitAsync(cancellationToken);
        cache.Remove(CacheKey(dataSourceId));
    }

    private static string CacheKey(Guid dataSourceId) => $"datascope:{dataSourceId}";
}
