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
}
