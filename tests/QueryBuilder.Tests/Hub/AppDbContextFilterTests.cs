using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

public class AppDbContextFilterTests
{
    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(@"Server=(localdb)
one;Database=never_opened;Integrated Security=true")
            .Options;

    private sealed class StopBeforeDatabase : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            throw new SentinelException();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new SentinelException();
    }

    private sealed class SentinelException : Exception;

    private static DbContextOptions<AppDbContext> StoppingOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(@"Server=(localdb)
one;Database=never_opened;Integrated Security=true")
            .AddInterceptors(new StopBeforeDatabase())
            .Options;

    private sealed class FakeAppContext(Guid appId, bool hubWide = false) : IAppContext
    {
        public Guid AppId { get; } = appId;
        public bool IsHubWide { get; } = hubWide;
    }

    [Fact]
    public void Every_app_owned_entity_has_a_query_filter()
    {
        using var db = new AppDbContext(Options());
        var types = db.Model.GetEntityTypes()
            .Where(t => typeof(IAppOwned).IsAssignableFrom(t.ClrType) || t.ClrType == typeof(AuditLogEntry))
            .ToList();

        Assert.Equal(9, types.Count);
#pragma warning disable CS0618 // GetDeclaredQueryFilters exists only on EF 10; this runs on both targets
        foreach (var t in types)
        {
            Assert.True(t.GetQueryFilter() is not null, $"{t.ClrType.Name} has no query filter");
#pragma warning restore CS0618
        }
    }

    [Fact]
    public void Legacy_constructor_uses_the_default_app()
    {
        using var db = new AppDbContext(Options());
        Assert.Equal(App.DefaultId, db.CurrentAppId);
        Assert.False(db.IsHubWide);
    }

    [Fact]
    public void Query_filter_follows_each_context_instance()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        using var one = new AppDbContext(Options(), new FakeAppContext(a));
        using var two = new AppDbContext(Options(), new FakeAppContext(b));
        using var hub = new AppDbContext(Options(), new FakeAppContext(App.NoneId, hubWide: true));

        var sqlOne = one.DataSources.ToQueryString();
        var sqlTwo = two.DataSources.ToQueryString();
        var sqlHub = hub.DataSources.ToQueryString();

        Assert.Contains(a.ToString(), sqlOne, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(b.ToString(), sqlOne, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(b.ToString(), sqlTwo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(a.ToString(), sqlTwo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(App.NoneId.ToString(), sqlHub, StringComparison.OrdinalIgnoreCase); // hub-wide flag is a parameter too
    }

    private static DataSource NewSource(Guid appId = default) => new() { Name = "s", AppId = appId };

    [Fact]
    public async Task Save_stamps_the_current_app_on_new_rows()
    {
        var app = Guid.NewGuid();
        await using var db = new AppDbContext(StoppingOptions(), new FakeAppContext(app));
        var source = NewSource();
        db.Entry(source).State = EntityState.Added;
        db.Entry(new AuditLogEntry()).State = EntityState.Added;

        await Assert.ThrowsAsync<SentinelException>(() => db.SaveChangesAsync());
        Assert.Equal(app, source.AppId);
        Assert.Equal(app, db.ChangeTracker.Entries<AuditLogEntry>().Single().Entity.AppId);
    }

    [Fact]
    public async Task Per_app_and_none_contexts_cannot_change_apps()
    {
        foreach (var id in new[] { Guid.NewGuid(), App.NoneId })
        {
            await using var db = new AppDbContext(StoppingOptions(), new FakeAppContext(id));
            var row = new App { Key = "x", Name = "x" };
            foreach (var state in new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted })
            {
                db.Entry(row).State = state;
                await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
            }
        }
    }

    [Fact]
    public async Task A_hub_wide_context_may_change_apps()
    {
        await using var db = new AppDbContext(StoppingOptions(), new FakeAppContext(App.NoneId, hubWide: true));
        var row = new App { Key = "x", Name = "x" };
        db.Entry(row).State = EntityState.Added;
        await Assert.ThrowsAsync<SentinelException>(() => db.SaveChangesAsync());
        db.Entry(row).State = EntityState.Modified;
        await Assert.ThrowsAsync<SentinelException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_refuses_a_row_of_another_app()
    {
        await using var db = new AppDbContext(Options(), new FakeAppContext(Guid.NewGuid()));
        db.Entry(NewSource(Guid.NewGuid())).State = EntityState.Added;
        await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_refuses_modifying_or_deleting_another_apps_row()
    {
        await using var db = new AppDbContext(Options(), new FakeAppContext(Guid.NewGuid()));
        var source = NewSource(Guid.NewGuid());
        db.Entry(source).State = EntityState.Modified;
        await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
        db.Entry(source).State = EntityState.Deleted;
        await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_refuses_any_write_without_an_app()
    {
        await using var db = new AppDbContext(Options(), new FakeAppContext(App.NoneId));
        db.Entry(NewSource()).State = EntityState.Added;
        await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_hub_wide_write_must_name_its_app()
    {
        await using var db = new AppDbContext(Options(), new FakeAppContext(App.NoneId, hubWide: true));
        db.Entry(NewSource()).State = EntityState.Added;
        var ex = await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
        Assert.Equal("A hub-wide write must name its app.", ex.Message);
    }
}
