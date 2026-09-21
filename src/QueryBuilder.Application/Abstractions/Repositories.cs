using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Abstractions;

public interface ISavedQueryRepository
{
    Task<SavedQuery?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<List<SavedQuery>> GetForUserAsync(string userId, CancellationToken cancellationToken);
    Task AddAsync(SavedQuery query, CancellationToken cancellationToken);
    void Update(SavedQuery query);
    void Remove(SavedQuery query);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IDataSourceRepository
{
    Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken);
}
