using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Persistence;

/// <summary>
/// Registers a "Default" data source pointed at QueryBuilder's own connection string the first
/// time the app starts with zero data sources registered, so the catalog isn't empty out of the
/// box. Runs after <see cref="DatabaseMigrationHostedService"/> (registration order) when
/// migrations are enabled; when they're not, the DBA-provisioned schema is expected to already
/// exist. Any failure here (e.g. schema not provisioned yet) is logged, not fatal — it never blocks
/// startup.
/// </summary>
internal sealed class DefaultDataSourceSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<DefaultDataSourceSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (await db.DataSources.AnyAsync(cancellationToken))
            {
                return;
            }

            db.DataSources.Add(new DataSource
            {
                Name = "Default",
                Description = "Auto-registered on first run using QueryBuilder's own connection string. " +
                               "Register additional data sources via SQL (see the package README) — an admin UI for this is planned.",
                Provider = DataSourceProvider.SqlServer,
                ConnectionStringName = DataSource.DefaultConnectionStringSentinel,
                CatalogScope = CatalogScope.TablesAndViews,
                IsActive = true,
                CreatedBy = "system-seed",
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not auto-register a default data source. If the catalog appears empty, register one manually — see the package README.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
