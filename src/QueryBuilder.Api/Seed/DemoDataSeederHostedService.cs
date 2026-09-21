using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Api.Seed;

/// <summary>
/// Seeds this sample app's demo data (a "Sales Sample" data source plus its backing schema).
/// Registered after AddQueryBuilderEditor so it runs after QueryBuilder's own migration hosted
/// service — hosted services start in registration order. A real integration would not register
/// this at all; it exists only so this repo's own demo has something to browse immediately.
/// </summary>
public sealed class DemoDataSeederHostedService(IServiceScopeFactory scopeFactory, DemoSourceSeeder demoSourceSeeder) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await AppDbSeeder.SeedAsync(db, cancellationToken);
        await demoSourceSeeder.SeedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
