using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence;

/// <summary>
/// Metadata store for the query builder itself (saved queries, registered data sources, shares).
/// This is intentionally separate from the business data sources the queries run against.
/// Every app-owned row is filtered to the current app on read and checked on write, so a repository
/// can't see or touch another app's rows even if it forgets to ask.
/// </summary>
public sealed class AppDbContext : DbContext
{
    private readonly IAppContext _appContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, IAppContext appContext) : base(options)
    {
        _appContext = appContext;
    }

    // Keeps design-time tooling and tests that build the context directly on the Default app.
    public AppDbContext(DbContextOptions<AppDbContext> options) : this(options, new DefaultAppContext())
    {
    }

    // Query filters name these properties, not _appContext, so EF re-evaluates them per context instance.
    internal Guid CurrentAppId => _appContext.AppId;
    internal bool IsHubWide => _appContext.IsHubWide;

    public DbSet<App> Apps => Set<App>();
    public DbSet<MetadataSetting> MetadataSettings => Set<MetadataSetting>();
    public DbSet<SavedQuery> SavedQueries => Set<SavedQuery>();
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<QueryShare> QueryShares => Set<QueryShare>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<DataScopeRule> DataScopeRules => Set<DataScopeRule>();
    public DbSet<QueryVisualization> QueryVisualizations => Set<QueryVisualization>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<DashboardTile> DashboardTiles => Set<DashboardTile>();
    public DbSet<DashboardShare> DashboardShares => Set<DashboardShare>();
    public DbSet<AppSigningKey> AppSigningKeys => Set<AppSigningKey>();
    public DbSet<AppReturnUrl> AppReturnUrls => Set<AppReturnUrl>();
    public DbSet<SeenUser> SeenUsers => Set<SeenUser>();
    public DbSet<LaunchReplay> LaunchReplays => Set<LaunchReplay>();
    public DbSet<DataProtectionKeyRecord> DataProtectionKeys => Set<DataProtectionKeyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Driven by the model so a new app-owned entity can't be forgotten.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(IAppOwned).IsAssignableFrom(t.ClrType)).ToList())
        {
            typeof(AppDbContext)
                .GetMethod(nameof(ApplyAppFilter), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);
        }

        modelBuilder.Entity<AuditLogEntry>().HasQueryFilter(e => IsHubWide || e.AppId == CurrentAppId);

        base.OnModelCreating(modelBuilder);
    }

    private void ApplyAppFilter<T>(ModelBuilder modelBuilder) where T : class, IAppOwned =>
        modelBuilder.Entity<T>().HasQueryFilter(e => IsHubWide || e.AppId == CurrentAppId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAppWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAppWrites()
    {
        ChangeTracker.DetectChanges();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is IAppOwned owned)
            {
                if (IsHubWide)
                {
                    if (entry.State == EntityState.Added && owned.AppId == Guid.Empty)
                    {
                        throw Refuse("A hub-wide write must name its app.", entry);
                    }

                    continue;
                }

                RequireWritableApp();
                if (entry.State == EntityState.Added && owned.AppId == Guid.Empty)
                {
                    owned.AppId = CurrentAppId;
                }
                else if (owned.AppId != CurrentAppId)
                {
                    throw Refuse("That row belongs to another app.", entry);
                }
            }
            else if (entry.Entity is IHubRegistryRecord)
            {
                if (!IsHubWide)
                {
                    throw Refuse("Only a hub-wide context may change apps.", entry);
                }
            }
            else if (entry.Entity is IHubSystemRecord)
            {
                // Plumbing, never tenant data: even a request with no app (a launch) must be able to write it.
            }
            else if (entry.Entity is not AuditLogEntry && !IsHubWide)
            {
                // A future table that is not app-owned fails closed unless the context may write.
                RequireWritableApp();
            }
            else if (entry.Entity is AuditLogEntry audit && !IsHubWide)
            {
                RequireWritableApp();
                if (entry.State == EntityState.Added && audit.AppId is null)
                {
                    audit.AppId = CurrentAppId;
                }
                else if (audit.AppId != CurrentAppId)
                {
                    throw Refuse("That row belongs to another app.", entry);
                }
            }
        }
    }

    private CrossAppWriteException Refuse(string message, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        new(message, CurrentAppId, entry.Metadata.ClrType.Name,
            entry.Metadata.FindPrimaryKey()?.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()).FirstOrDefault());

    private void RequireWritableApp()
    {
        if (CurrentAppId == App.NoneId)
        {
            throw new CrossAppWriteException("No app is selected, so nothing can be written.", CurrentAppId);
        }
    }

    private sealed class DefaultAppContext : IAppContext
    {
        public Guid AppId => App.DefaultId;
        public bool IsHubWide => false;
    }
}
