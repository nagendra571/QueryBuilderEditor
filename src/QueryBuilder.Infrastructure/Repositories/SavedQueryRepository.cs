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

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
