using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence;

/// <summary>
/// Metadata store for the query builder itself (saved queries, registered data sources, shares).
/// This is intentionally separate from the business data sources the queries run against.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SavedQuery> SavedQueries => Set<SavedQuery>();
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<QueryShare> QueryShares => Set<QueryShare>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
