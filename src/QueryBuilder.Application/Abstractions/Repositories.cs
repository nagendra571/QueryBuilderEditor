using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Abstractions;

public interface ISavedQueryRepository
{
    Task<SavedQuery?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<List<SavedQuery>> GetForUserAsync(string userId, CancellationToken cancellationToken);
    Task AddAsync(SavedQuery query, CancellationToken cancellationToken);
    void Update(SavedQuery query);
    void Remove(SavedQuery query);
    Task AddShareAsync(QueryShare share, CancellationToken cancellationToken);
    void RemoveShare(QueryShare share);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IDataSourceRepository
{
    Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken);
    Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken);
    Task AddAsync(DataSource dataSource, CancellationToken cancellationToken);
    /// <summary>Writes only the named columns (plus UpdatedBy and UpdatedAtUtc). The row was read untracked, possibly
    /// long ago, so a whole-row write could restore a turn-off or an older password changed in between.</summary>
    void UpdateOnly(DataSource dataSource, params string[] propertyNames);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IVisualizationRepository
{
    Task<List<QueryVisualization>> GetForQueryAsync(Guid savedQueryId, CancellationToken cancellationToken);
    Task<List<QueryVisualization>> GetForQueriesAsync(IReadOnlyCollection<Guid> savedQueryIds, CancellationToken cancellationToken);
    Task<QueryVisualization?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(QueryVisualization visualization, CancellationToken cancellationToken);
    void Update(QueryVisualization visualization);
    void Remove(QueryVisualization visualization);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IDashboardRepository
{
    /// <summary>Tracked, with tiles, shares, and each tile's visualization, query and query shares loaded.</summary>
    Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<List<Dashboard>> GetForUserAsync(string userId, CancellationToken cancellationToken);
    Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken);
    void Remove(Dashboard dashboard);
    Task AddTileAsync(DashboardTile tile, CancellationToken cancellationToken);
    void RemoveTile(DashboardTile tile);
    Task AddShareAsync(DashboardShare share, CancellationToken cancellationToken);
    void RemoveShare(DashboardShare share);
    /// <summary>Throws ConcurrencyConflictException if another writer changed the dashboard first.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
