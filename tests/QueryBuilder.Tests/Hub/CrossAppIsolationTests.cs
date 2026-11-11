using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dashboards;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor.Middleware;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Repositories;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class CrossAppIsolationTests
{
    private const string User = "u";

    private sealed record Ids(Guid DataSource, Guid Query, Guid Visualization, Guid Dashboard, Guid Tile, Guid Rule);

    private sealed record Seed(Guid AppA, Ids A, Ids B);

    private static AppDbContext Db(DbContextOptions<AppDbContext> options, Guid appId, bool hubWide = false) =>
        new(options, new FixedAppContext(appId, hubWide));

    private static async Task<Ids> SeedAppAsync(DbContextOptions<AppDbContext> options, Guid appId)
    {
        var ids = new Ids(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = Db(options, appId);
        db.DataSources.Add(new DataSource { Id = ids.DataSource, Name = "source" });
        db.SavedQueries.Add(new SavedQuery { Id = ids.Query, Name = "query", DataSourceId = ids.DataSource, OwnerId = User, DefinitionJson = "{}" });
        await db.SaveChangesAsync();
        db.QueryShares.Add(new QueryShare { SavedQueryId = ids.Query, SharedWithUserId = "other", SharedWithUserEmail = "o@x" });
        db.QueryVisualizations.Add(new QueryVisualization { Id = ids.Visualization, SavedQueryId = ids.Query, Name = "viz" });
        db.Dashboards.Add(new Dashboard { Id = ids.Dashboard, Name = "dash", OwnerId = User });
        await db.SaveChangesAsync();
        db.DashboardTiles.Add(new DashboardTile { Id = ids.Tile, DashboardId = ids.Dashboard, Kind = DashboardTileKind.Chart, VisualizationId = ids.Visualization });
        db.DashboardShares.Add(new DashboardShare { DashboardId = ids.Dashboard, SharedWithUserId = "other", SharedWithUserEmail = "o@x" });
        db.DataScopeRules.Add(new DataScopeRule { Id = ids.Rule, DataSourceId = ids.DataSource, ObjectName = "dbo.v", ScopeKey = "k", ColumnName = "c" });
        db.AuditLogEntries.Add(new AuditLogEntry { EntityType = "x", Summary = ids.Query.ToString() });
        await db.SaveChangesAsync();
        return ids;
    }

    private static Task RunAsync(Func<DbContextOptions<AppDbContext>, Seed, Task> body) => MetadataDb.WithDatabaseAsync(async (options, _) =>
    {
        var appA = Guid.NewGuid();
        await using (var hub = Db(options, App.NoneId, hubWide: true))
        {
            hub.Apps.Add(new App { Id = appA, Key = "a", Name = "A" });
            await hub.SaveChangesAsync();
        }

        var a = await SeedAppAsync(options, appA);
        var b = await SeedAppAsync(options, App.DefaultId);
        await body(options, new Seed(appA, a, b));
    });

    [MetadataSqlFact]
    public Task App_b_cannot_load_app_a_rows_by_id() => RunAsync(async (options, seed) =>
    {
        await using var db = Db(options, App.DefaultId);
        var ct = CancellationToken.None;
        Assert.Null(await new DataSourceRepository(db).GetByIdAsync(seed.A.DataSource, ct));
        Assert.Null(await new SavedQueryRepository(db).GetByIdAsync(seed.A.Query, ct));
        Assert.Null(await new VisualizationRepository(db).GetByIdAsync(seed.A.Visualization, ct));
        Assert.Null(await new DashboardRepository(db).GetByIdAsync(seed.A.Dashboard, ct));
        Assert.Empty(await new DataScopeRuleRepository(db, new MemoryCache(new MemoryCacheOptions())).GetForDataSourceAsync(seed.A.DataSource, ct));
        Assert.False(await db.DashboardTiles.AnyAsync(t => t.Id == seed.A.Tile));
        Assert.False(await db.QueryShares.AnyAsync(s => s.SavedQueryId == seed.A.Query));
        Assert.False(await db.DashboardShares.AnyAsync(s => s.DashboardId == seed.A.Dashboard));

        // Control: its own rows load.
        Assert.NotNull(await new SavedQueryRepository(db).GetByIdAsync(seed.B.Query, ct));
        Assert.NotNull(await new DashboardRepository(db).GetByIdAsync(seed.B.Dashboard, ct));
    });

    [MetadataSqlFact]
    public Task App_b_lists_contain_none_of_app_a() => RunAsync(async (options, seed) =>
    {
        await using var db = Db(options, App.DefaultId);
        var ct = CancellationToken.None;
        var sources = new DataSourceRepository(db);

        Assert.Equal([seed.B.DataSource], (await sources.GetAllAsync(ct)).Select(d => d.Id));
        Assert.Equal([seed.B.DataSource], (await sources.GetActiveAsync(ct)).Select(d => d.Id));
        Assert.Equal([seed.B.Query], (await new SavedQueryRepository(db).ListForUserAsync(new QueryBuilder.Application.Common.AccessActor(User, false, false, false), ct)).Select(q => q.Id));
        Assert.Equal([seed.B.Dashboard], (await new DashboardRepository(db).ListForUserAsync(new QueryBuilder.Application.Common.AccessActor(User, false, false, false), ct)).Select(d => d.Id));
        Assert.Equal([seed.B.Query], (await new SavedQueryRepository(db).ListForUserAsync(new QueryBuilder.Application.Common.AccessActor("other", false, false, false), ct)).Select(q => q.Id));
        Assert.Equal([seed.B.Dashboard], (await new DashboardRepository(db).ListForUserAsync(new QueryBuilder.Application.Common.AccessActor("other", false, false, false), ct)).Select(d => d.Id));

        var visualizations = new VisualizationRepository(db);
        Assert.Empty(await visualizations.GetForQueryAsync(seed.A.Query, ct));
        Assert.Equal([seed.B.Visualization], (await visualizations.GetForQueriesAsync([seed.A.Query, seed.B.Query], ct)).Select(v => v.Id));

        Assert.Equal([seed.B.Query.ToString()], await db.AuditLogEntries.Select(e => e.Summary).ToListAsync());
        Assert.Equal(1, await db.DataScopeRules.CountAsync());
        Assert.Equal(1, await db.QueryShares.CountAsync());
        Assert.Equal(1, await db.DashboardShares.CountAsync());
        Assert.Equal(1, await db.DashboardTiles.CountAsync());
    });

    [MetadataSqlFact]
    public Task A_shared_rules_cache_is_not_read_across_apps() => RunAsync(async (options, seed) =>
    {
        var ct = CancellationToken.None;
        var cache = new MemoryCache(new MemoryCacheOptions());
        await using var dbA = Db(options, seed.AppA);
        await using var dbB = Db(options, App.DefaultId);

        Assert.Single(await new DataScopeRuleRepository(dbA, cache).GetForDataSourceAsync(seed.A.DataSource, ct));
        Assert.Empty(await new DataScopeRuleRepository(dbB, cache).GetForDataSourceAsync(seed.A.DataSource, ct));
    });

    [MetadataSqlFact]
    public Task A_foreign_call_does_not_poison_the_rules_cache() => RunAsync(async (options, seed) =>
    {
        var ct = CancellationToken.None;
        var cache = new MemoryCache(new MemoryCacheOptions());
        await using var dbA = Db(options, seed.AppA);
        await using var dbB = Db(options, App.DefaultId);

        Assert.Empty(await new DataScopeRuleRepository(dbB, cache).GetForDataSourceAsync(seed.A.DataSource, ct));
        Assert.Single(await new DataScopeRuleRepository(dbA, cache).GetForDataSourceAsync(seed.A.DataSource, ct));
    });

    [MetadataSqlFact]
    public Task Writing_an_app_a_entity_from_app_b_is_refused() => RunAsync(async (options, seed) =>
    {
        await using (var db = Db(options, App.DefaultId))
        {
            var stranger = new Dashboard { Id = seed.A.Dashboard, AppId = seed.AppA, Name = "hijacked", OwnerId = User };
            db.Dashboards.Attach(stranger);
            db.Entry(stranger).Property(d => d.Name).IsModified = true;
            await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
        }

        await using var check = Db(options, App.NoneId, hubWide: true);
        Assert.Equal("dash", (await check.Dashboards.SingleAsync(d => d.Id == seed.A.Dashboard)).Name);
    });

    private sealed class FixedUser : ICurrentUserService
    {
        public string UserId => User;
        public string DisplayName => "U";
        public string? Email => null;
        public QueryBuilder.Domain.Enums.HubRole? Role => null;
        public bool IsSuperAdmin => false;
    }

    private sealed class NoAudit : IAuditLogger
    {
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [MetadataSqlFact]
    public Task A_chart_tile_pointing_at_another_apps_visualization_is_not_found() => RunAsync(async (options, seed) =>
    {
        await using var db = Db(options, seed.AppA);
        var handler = new AddChartToDashboardCommandHandler(
            new DashboardRepository(db), new VisualizationRepository(db), new SavedQueryRepository(db), new FixedUser(), new NoAudit());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new AddChartToDashboardCommand(seed.A.Dashboard, seed.B.Visualization), CancellationToken.None));

        await using var check = Db(options, App.NoneId, hubWide: true);
        Assert.Equal([seed.A.Tile], await check.DashboardTiles.Where(t => t.DashboardId == seed.A.Dashboard).Select(t => t.Id).ToListAsync());
    });

    [MetadataSqlFact]
    public Task Hub_wide_context_sees_both_apps_and_must_name_the_app_on_insert() => RunAsync(async (options, seed) =>
    {
        await using var db = Db(options, App.NoneId, hubWide: true);
        var ids = (await new SavedQueryRepository(db).ListForUserAsync(new QueryBuilder.Application.Common.AccessActor(User, false, false, false), CancellationToken.None)).Select(q => q.Id).ToHashSet();
        Assert.Equal(new[] { seed.A.Query, seed.B.Query }.ToHashSet(), ids);
        Assert.Equal(2, (await new DataSourceRepository(db).GetAllAsync(CancellationToken.None)).Count);
        Assert.Equal(2, (await new DashboardRepository(db).ListForUserAsync(new QueryBuilder.Application.Common.AccessActor(User, false, false, false), CancellationToken.None)).Count);

        await new SavedQueryRepository(db).AddAsync(
            new SavedQuery { Name = "n", DataSourceId = seed.A.DataSource, OwnerId = User, DefinitionJson = "{}", AppId = Guid.Empty },
            CancellationToken.None);
        await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
    });

    [MetadataSqlFact]
    public Task None_context_sees_nothing_and_cannot_write() => RunAsync(async (options, seed) =>
    {
        await using var db = Db(options, App.NoneId);
        var ct = CancellationToken.None;
        Assert.Empty(await new DataSourceRepository(db).GetAllAsync(ct));
        Assert.Empty(await new DataSourceRepository(db).GetActiveAsync(ct));
        Assert.Empty(await new SavedQueryRepository(db).ListForUserAsync(new QueryBuilder.Application.Common.AccessActor(User, false, false, false), ct));
        Assert.Empty(await new DashboardRepository(db).ListForUserAsync(new QueryBuilder.Application.Common.AccessActor(User, false, false, false), ct));
        Assert.Empty(await new VisualizationRepository(db).GetForQueriesAsync([seed.A.Query, seed.B.Query], ct));
        Assert.Empty(await new DataScopeRuleRepository(db, new MemoryCache(new MemoryCacheOptions())).GetForDataSourceAsync(seed.B.DataSource, ct));
        Assert.Empty(await db.AuditLogEntries.ToListAsync());
        Assert.Empty(await db.DashboardTiles.ToListAsync());

        await new DataSourceRepository(db).AddAsync(new DataSource { Name = "x", AppId = seed.AppA }, ct);
        await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync(ct));
    });

    [Fact]
    public async Task Cross_app_write_maps_to_404()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, new CrossAppWriteException("A hub-wide write must name its app."), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("Not found", body);
        Assert.DoesNotContain("hub-wide", body);
    }
}
