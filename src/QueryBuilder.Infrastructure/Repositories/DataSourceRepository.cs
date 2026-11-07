using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Repositories;

public sealed class DataSourceRepository(AppDbContext db) : IDataSourceRepository
{
    public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.DataSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken) =>
        db.DataSources.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken) =>
        db.DataSources.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(DataSource dataSource, CancellationToken cancellationToken) =>
        await db.DataSources.AddAsync(dataSource, cancellationToken);

    public void UpdateOnly(DataSource dataSource, params string[] propertyNames)
    {
        var entry = db.Entry(dataSource);
        if (entry.State == EntityState.Detached)
        {
            entry = db.Attach(dataSource);
        }

        foreach (var name in propertyNames.Concat([nameof(DataSource.UpdatedBy), nameof(DataSource.UpdatedAtUtc)]).Distinct())
        {
            entry.Property(name).IsModified = true;
        }
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
