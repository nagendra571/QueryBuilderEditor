using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

public class AppModelTests
{
    [Fact]
    public void Every_metadata_entity_is_app_owned_except_the_known_exceptions()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=never_opened;Database=never_opened").Options;
        using var db = new AppDbContext(options);

        var notOwned = db.Model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => !typeof(IAppOwned).IsAssignableFrom(t))
            .Select(t => t.Name).OrderBy(n => n).ToList();

        Assert.Equal(["App", "AuditLogEntry", "MetadataSetting"], notOwned);
    }

    [Fact]
    public void Default_and_none_ids_differ_and_none_is_empty()
    {
        Assert.NotEqual(App.DefaultId, App.NoneId);
        Assert.Equal(Guid.Empty, App.NoneId);
        Assert.Equal(new Guid("00000000-0000-0000-0000-00000000000d"), App.DefaultId);
    }
}
