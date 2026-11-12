using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

public class AppContextTests
{
    [Fact]
    public void Embedded_without_a_request_resolves_to_default()
    {
        var accessor = new AppContextAccessor(new MetadataMode(false));

        Assert.Equal(App.DefaultId, accessor.AppId);
        Assert.False(accessor.IsHubWide);
    }

    [Fact]
    public void Hub_without_a_request_resolves_to_none()
    {
        var accessor = new AppContextAccessor(new MetadataMode(true));

        Assert.Equal(App.NoneId, accessor.AppId);
        Assert.False(accessor.IsHubWide);
    }

    [Fact]
    public void Explicit_choice_wins()
    {
        var other = Guid.NewGuid();
        var hub = new AppContextAccessor(new MetadataMode(true));
        hub.UseApp(other);
        Assert.Equal(other, hub.AppId);

        var embedded = new AppContextAccessor(new MetadataMode(false));
        embedded.UseHubWide();
        Assert.True(embedded.IsHubWide);

        var none = new AppContextAccessor(new MetadataMode(false));
        none.UseApp(App.NoneId);
        Assert.Equal(App.NoneId, none.AppId);
        Assert.False(none.IsHubWide);
    }

    [Fact]
    public void UseApp_after_context_creation_still_applies()
    {
        var accessor = new AppContextAccessor(new MetadataMode(true));
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=never.invalid;Database=x").Options;
        using var db = new AppDbContext(options, accessor);
        Assert.Equal(App.NoneId, db.CurrentAppId);

        var chosen = Guid.NewGuid();
        accessor.UseApp(chosen);

        Assert.Equal(chosen, db.CurrentAppId);
    }

    [Fact]
    public void A_different_second_choice_throws_but_repeating_is_fine()
    {
        var accessor = new AppContextAccessor(new MetadataMode(true));
        var first = Guid.NewGuid();
        accessor.UseApp(first);
        accessor.UseApp(first);
        Assert.Throws<InvalidOperationException>(() => accessor.UseApp(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => accessor.UseHubWide());

        var wide = new AppContextAccessor(new MetadataMode(true));
        wide.UseHubWideForFactory();
        wide.UseHubWideForFactory();
        Assert.Throws<InvalidOperationException>(() => wide.UseApp(first));
    }
}
