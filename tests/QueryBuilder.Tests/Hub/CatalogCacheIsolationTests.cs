using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Catalog;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Repositories;
using Xunit;

namespace QueryBuilder.Tests.Hub;

public class CatalogCacheIsolationTests
{
    private static DataCatalogService Service(AppDbContext db, IMemoryCache cache) =>
        new(new DataSourceRepository(db), null!, [], cache, NullLogger<DataCatalogService>.Instance);

    [MetadataSqlFact]
    public Task A_cached_catalog_is_not_served_to_another_app() => MetadataDb.WithDatabaseAsync(async (options, _) =>
    {
        var appA = Guid.NewGuid();
        await using (var hub = new AppDbContext(options, new FixedAppContext(App.NoneId, hubWide: true)))
        {
            hub.Apps.Add(new App { Id = appA, Key = "a", Name = "A" });
            await hub.SaveChangesAsync();
        }

        var idA = Guid.NewGuid();
        await using (var db = new AppDbContext(options, new FixedAppContext(appA)))
        {
            db.DataSources.Add(new DataSource { Id = idA, Name = "A source" });
            await db.SaveChangesAsync();
        }

        var cache = new MemoryCache(new MemoryCacheOptions());
        var catalog = new DataSourceCatalog { DataSourceId = idA };
        cache.Set($"catalog:{idA}", catalog);

        await using var asB = new AppDbContext(options, new FixedAppContext(App.DefaultId));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(asB, cache).GetCatalogAsync(idA, CancellationToken.None));

        await using var asA = new AppDbContext(options, new FixedAppContext(appA));
        Assert.Same(catalog, await Service(asA, cache).GetCatalogAsync(idA, CancellationToken.None));
    });
}
