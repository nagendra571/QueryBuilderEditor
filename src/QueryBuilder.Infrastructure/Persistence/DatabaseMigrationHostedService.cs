using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QueryBuilder.Infrastructure.Persistence;

/// <summary>
/// Applies QueryBuilder's EF Core migrations on startup, before the host begins accepting
/// requests. Registered only when <c>QueryBuilderEditorOptions.ApplyMigrations</c> is true —
/// hosted services run in registration order, so any host-registered seeding that depends on the
/// schema existing must be registered after <c>AddQueryBuilderEditor</c>.
/// </summary>
internal sealed class DatabaseMigrationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
