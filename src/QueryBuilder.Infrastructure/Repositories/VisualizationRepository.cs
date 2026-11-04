using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Repositories;

public sealed class VisualizationRepository(AppDbContext db) : IVisualizationRepository
{
    public Task<List<QueryVisualization>> GetForQueryAsync(Guid savedQueryId, CancellationToken cancellationToken) =>
        db.QueryVisualizations
            .Where(x => x.SavedQueryId == savedQueryId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<List<QueryVisualization>> GetForQueriesAsync(IReadOnlyCollection<Guid> savedQueryIds, CancellationToken cancellationToken) =>
        db.QueryVisualizations
            .AsNoTracking()
            .Where(x => savedQueryIds.Contains(x.SavedQueryId))
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<QueryVisualization?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.QueryVisualizations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    // Added on the DbSet itself (see SavedQueryRepository.AddShareAsync for why).
    public async Task AddAsync(QueryVisualization visualization, CancellationToken cancellationToken) =>
        await db.QueryVisualizations.AddAsync(visualization, cancellationToken);

    public void Update(QueryVisualization visualization) => db.QueryVisualizations.Update(visualization);

    public void Remove(QueryVisualization visualization) => db.QueryVisualizations.Remove(visualization);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
