using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Tests;

/// <summary>For tests that pre-seed the catalog cache: validation now reads the data source row first, and
/// these tests only need it to exist and be on.</summary>
internal sealed class ActiveDataSources(bool isActive = true) : IDataSourceRepository
{
    public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<DataSource?>(new DataSource { Id = id, Name = "Test", IsActive = isActive });
    public Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddAsync(DataSource dataSource, CancellationToken cancellationToken) => throw new NotSupportedException();
    public void UpdateOnly(DataSource dataSource, params string[] propertyNames) { }
    public Task<bool> TryReplaceEncryptedConnectionAsync(Guid id, string expected, string replacement, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
