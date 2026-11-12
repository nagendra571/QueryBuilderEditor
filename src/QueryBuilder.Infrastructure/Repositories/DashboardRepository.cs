using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Repositories;

public sealed class DashboardRepository(AppDbContext db) : IDashboardRepository
{
    public Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Dashboards
            .Include(d => d.Shares)
            .Include(d => d.Tiles).ThenInclude(t => t.Visualization).ThenInclude(v => v!.SavedQuery).ThenInclude(q => q!.Shares)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<List<Dashboard>> ListForUserAsync(AccessActor actor, CancellationToken cancellationToken)
    {
        var query = db.Dashboards.AsNoTracking().Include(d => d.Shares).Include(d => d.Tiles).AsSplitQuery().AsQueryable();
        if (!actor.SeesWholeApp)
        {
            var userId = actor.UserId;
            query = query.Where(d => d.OwnerId == userId || d.PublishedToApp || d.Shares.Any(s => s.SharedWithUserId == userId));
        }

        return query.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken) =>
        await db.Dashboards.AddAsync(dashboard, cancellationToken);

    public void Remove(Dashboard dashboard) => db.Dashboards.Remove(dashboard);

    // Children go through their own DbSets, never the parent's collection (see SavedQueryRepository.AddShareAsync).
    public async Task AddTileAsync(DashboardTile tile, CancellationToken cancellationToken) =>
        await db.DashboardTiles.AddAsync(tile, cancellationToken);

    public void RemoveTile(DashboardTile tile) => db.DashboardTiles.Remove(tile);

    public async Task AddShareAsync(DashboardShare share, CancellationToken cancellationToken) =>
        await db.DashboardShares.AddAsync(share, cancellationToken);

    public void RemoveShare(DashboardShare share) => db.DashboardShares.Remove(share);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("This dashboard was changed by someone else — reload to see the latest.");
        }
    }
}
