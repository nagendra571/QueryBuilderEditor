using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Repositories;

public sealed class SavedQueryRepository(AppDbContext db) : ISavedQueryRepository
{
    public Task<SavedQuery?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.SavedQueries
            .Include(x => x.DataSource)
            .Include(x => x.Shares)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<List<SavedQuery>> GetForUserAsync(string userId, CancellationToken cancellationToken) =>
        db.SavedQueries
            .AsNoTracking()
            .Include(x => x.DataSource)
            .Include(x => x.Shares)
            .Where(x => x.OwnerId == userId || x.Shares.Any(s => s.SharedWithUserId == userId))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SavedQuery query, CancellationToken cancellationToken) =>
        await db.SavedQueries.AddAsync(query, cancellationToken);

    public void Update(SavedQuery query) => db.SavedQueries.Update(query);

    public void Remove(SavedQuery query) => db.SavedQueries.Remove(query);

    // Added/removed directly on the QueryShares DbSet — not via the SavedQuery.Shares navigation —
    // so EF's change tracker unambiguously treats these as Added/Deleted. Adding a new child to an
    // already-tracked parent's collection instead left EF inferring Modified for the new row (it
    // already has a non-default client-generated Guid key), producing an UPDATE that matched zero
    // rows.
    public async Task AddShareAsync(QueryShare share, CancellationToken cancellationToken) =>
        await db.QueryShares.AddAsync(share, cancellationToken);

    public void RemoveShare(QueryShare share) => db.QueryShares.Remove(share);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
