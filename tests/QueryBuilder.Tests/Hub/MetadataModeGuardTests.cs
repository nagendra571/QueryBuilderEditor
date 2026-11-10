using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class MetadataModeGuardTests
{
    private static ServiceProvider Build(string connectionString, bool hub)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddInfrastructure(connectionString, applyMigrations: false, hubMode: hub);
        return services.BuildServiceProvider();
    }

    private static Task StartAsync<T>(ServiceProvider provider) where T : IHostedService =>
        provider.GetServices<IHostedService>().OfType<T>().Single().StartAsync(CancellationToken.None);

    private static async Task<string?> StoredModeAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options);
        return await db.MetadataSettings.Where(s => s.Key == "Mode").Select(s => s.Value).SingleOrDefaultAsync();
    }

    [MetadataSqlFact]
    public Task First_run_records_the_mode() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        using var provider = Build(cs, hub: true);
        await StartAsync<MetadataModeGuard>(provider);

        Assert.Equal("Hub", await StoredModeAsync(options));
    });

    [MetadataSqlFact]
    public Task Embedded_refuses_a_hub_database() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        using (var hub = Build(cs, hub: true)) await StartAsync<MetadataModeGuard>(hub);

        using var embedded = Build(cs, hub: false);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync<MetadataModeGuard>(embedded));

        Assert.Equal("This QueryBuilder metadata database belongs to a Hub-mode installation; this app is configured for Embedded mode. Use a separate metadata database for the hub.", ex.Message);
    });

    [MetadataSqlFact]
    public Task Hub_refuses_an_embedded_database() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        using (var embedded = Build(cs, hub: false)) await StartAsync<MetadataModeGuard>(embedded);

        using var hub = Build(cs, hub: true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync<MetadataModeGuard>(hub));

        Assert.Contains("an Embedded-mode", ex.Message);
        Assert.Contains("Hub mode", ex.Message);
    });

    [MetadataSqlFact]
    public Task Same_mode_starts_again() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        for (var i = 0; i < 2; i++)
        {
            using var provider = Build(cs, hub: false);
            await StartAsync<MetadataModeGuard>(provider);
        }

        Assert.Equal("Embedded", await StoredModeAsync(options));
    });

    [MetadataSqlFact]
    public Task Embedded_seeder_still_creates_the_default_source_in_the_default_app() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        using var provider = Build(cs, hub: false);
        await StartAsync<DefaultDataSourceSeeder>(provider);

        await using var db = new AppDbContext(options);
        var source = await db.DataSources.SingleAsync();
        Assert.Equal(App.DefaultId, source.AppId);
    });

    [MetadataSqlFact]
    public Task Hub_mode_never_seeds_the_metadata_database_as_a_data_source() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        using var provider = Build(cs, hub: true);
        await StartAsync<DefaultDataSourceSeeder>(provider);

        await using var db = new AppDbContext(options);
        Assert.Equal(0, await db.DataSources.IgnoreQueryFilters().CountAsync());
    });

    private static MetadataModeGuard NewGuard(ServiceProvider provider, bool hub) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), new MetadataMode(hub), NullLogger<MetadataModeGuard>.Instance);

    private static async Task MigrateToAsync(DbContextOptions<AppDbContext> options, string target)
    {
        await using var db = new AppDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task AddDataSourceAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options);
        db.DataSources.Add(new DataSource
        {
            Name = "Existing",
            Provider = DataSourceProvider.SqlServer,
            ConnectionStringName = DataSource.DefaultConnectionStringSentinel,
            CatalogScope = CatalogScope.TablesAndViews,
            IsActive = true,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();
    }

    [MetadataSqlFact]
    public Task Embedded_guard_tolerates_a_missing_schema_but_hub_guard_does_not() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        await MigrateToAsync(options, "20261110023605_AddDataSourceScopeKeys");

        using var provider = Build(cs, hub: false);
        await NewGuard(provider, hub: false).StartAsync(CancellationToken.None);

        using var hubProvider = Build(cs, hub: true);
        await Assert.ThrowsAnyAsync<Exception>(() => NewGuard(hubProvider, hub: true).StartAsync(CancellationToken.None));
    }, migrate: false);

    [MetadataSqlFact]
    public Task Embedded_guard_tolerates_an_empty_database() => MetadataDb.WithDatabaseAsync(async (_, cs) =>
    {
        using var provider = Build(cs, hub: false);
        await NewGuard(provider, hub: false).StartAsync(CancellationToken.None);
    }, migrate: false);

    [MetadataSqlFact]
    public Task A_concurrent_first_start_that_wins_the_insert_is_compared_not_fatal() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        using var provider = Build(cs, hub: false);
        var guard = NewGuard(provider, hub: false);
        guard.BeforeInsert = async () =>
        {
            await using var other = new AppDbContext(options);
            other.MetadataSettings.Add(new MetadataSetting { Key = "Mode", Value = "Embedded" });
            await other.SaveChangesAsync();
        };
        await guard.StartAsync(CancellationToken.None);

        using var hubProvider = Build(cs, hub: true);
        var hubGuard = NewGuard(hubProvider, hub: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => hubGuard.StartAsync(CancellationToken.None));
    });

    private static async Task BlockModeInsertsAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER TR_BlockModeInsert ON MetadataSettings INSTEAD OF INSERT AS THROW 50000, 'denied', 1;");
    }

    [MetadataSqlFact]
    public Task Embedded_guard_continues_when_the_mode_cannot_be_recorded_but_hub_does_not() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        await BlockModeInsertsAsync(options);

        using var provider = Build(cs, hub: false);
        await NewGuard(provider, hub: false).StartAsync(CancellationToken.None);
        Assert.Null(await StoredModeAsync(options));

        using var hubProvider = Build(cs, hub: true);
        await Assert.ThrowsAsync<DbUpdateException>(() => NewGuard(hubProvider, hub: true).StartAsync(CancellationToken.None));
    });

    [MetadataSqlFact]
    public Task A_concurrent_first_start_with_a_different_mode_is_refused() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        using var provider = Build(cs, hub: true);
        var guard = NewGuard(provider, hub: true);
        guard.BeforeInsert = async () =>
        {
            await using var other = new AppDbContext(options);
            other.MetadataSettings.Add(new MetadataSetting { Key = "Mode", Value = "Embedded" });
            await other.SaveChangesAsync();
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.StartAsync(CancellationToken.None));
    });

    [MetadataSqlFact]
    public Task Hub_refuses_existing_data_without_a_mode_but_embedded_records_embedded() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        await AddDataSourceAsync(options);

        using var hub = Build(cs, hub: true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync<MetadataModeGuard>(hub));
        Assert.Equal("This metadata database already holds QueryBuilder data but has no recorded mode; the hub needs its own empty metadata database.", ex.Message);
        Assert.Null(await StoredModeAsync(options));

        using var embedded = Build(cs, hub: false);
        await StartAsync<MetadataModeGuard>(embedded);
        Assert.Equal("Embedded", await StoredModeAsync(options));
    });
}
