using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Repositories;

public sealed class DataScopeRuleRepository(AppDbContext db, IMemoryCache cache) : IDataScopeRuleRepository
{
    public async Task<IReadOnlyList<DataScopeRule>> GetForDataSourceAsync(Guid dataSourceId, CancellationToken cancellationToken)
    {
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
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        cache.Remove(CacheKey(dataSourceId));
    }

    private static string CacheKey(Guid dataSourceId) => $"datascope:{dataSourceId}";
}
