using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class HubRegistryModelTests
{
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
            .UseSqlServer("Server=never_opened;Database=never_opened")
            .AddInterceptors(new StopBeforeDatabase())
            .Options;

    private static readonly EntityState[] Changes = [EntityState.Added, EntityState.Modified, EntityState.Deleted];

    private static object[] Records(Guid appId) =>
    [
        new AppSigningKey { AppId = appId, Kid = "k", Algorithm = "RS256", PublicKeyPem = "pem" },
        new AppReturnUrl { AppId = appId, Url = "https://h/x" },
    ];

    [Fact]
    public async Task Registry_records_are_hub_wide_only()
    {
        var app = Guid.NewGuid();
        foreach (var id in new[] { app, App.NoneId })
        {
            await using var db = new AppDbContext(StoppingOptions(), new FixedAppContext(id));
            foreach (var record in Records(app))
            {
                foreach (var state in Changes)
                {
                    db.Entry(record).State = state;
                    await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
                }

                db.Entry(record).State = EntityState.Detached;
            }
        }

        await using var hub = new AppDbContext(StoppingOptions(), new FixedAppContext(App.NoneId, hubWide: true));
        foreach (var record in Records(app))
        {
            foreach (var state in Changes)
            {
                hub.Entry(record).State = state;
                await Assert.ThrowsAsync<SentinelException>(() => hub.SaveChangesAsync());
            }

            hub.Entry(record).State = EntityState.Detached;
        }
    }

    [Fact]
    public async Task System_records_are_writable_without_an_app()
    {
        foreach (var id in new[] { App.NoneId, Guid.NewGuid() })
        {
            await using var db = new AppDbContext(StoppingOptions(), new FixedAppContext(id));
            object[] records = [new LaunchReplay { Jti = "j", AppId = Guid.NewGuid() }, new DataProtectionKeyRecord { Id = 1, FriendlyName = "f", Xml = "<x/>" }];
            foreach (var record in records)
            {
                foreach (var state in Changes)
                {
                    db.Entry(record).State = state;
                    await Assert.ThrowsAsync<SentinelException>(() => db.SaveChangesAsync());
                }

                db.Entry(record).State = EntityState.Detached;
            }
        }
    }

    [Fact]
    public async Task Seen_users_are_app_owned_and_filtered()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using var db = new AppDbContext(StoppingOptions(), new FixedAppContext(a));

        Assert.Contains(a.ToString(), db.SeenUsers.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(b.ToString(), db.SeenUsers.ToQueryString(), StringComparison.OrdinalIgnoreCase);

        var stamped = new SeenUser { UserId = "u" };
        db.Entry(stamped).State = EntityState.Added;
        await Assert.ThrowsAsync<SentinelException>(() => db.SaveChangesAsync());
        Assert.Equal(a, stamped.AppId);

        db.Entry(stamped).State = EntityState.Detached;
        db.Entry(new SeenUser { UserId = "u", AppId = b }).State = EntityState.Added;
        await Assert.ThrowsAsync<CrossAppWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Save_guard_markers_are_mutually_exclusive()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=never_opened;Database=never_opened").Options);
        foreach (var type in db.Model.GetEntityTypes().Select(t => t.ClrType))
        {
            var markers = new[] { typeof(IAppOwned), typeof(IHubRegistryRecord), typeof(IHubSystemRecord) }.Count(m => m.IsAssignableFrom(type));
            Assert.True(markers <= 1, $"{type.Name} implements {markers} save-guard markers");
        }
    }

    [MetadataSqlFact]
    public Task An_800_character_return_url_inserts() => MetadataDb.WithDatabaseAsync(async (options, _) =>
    {
        var app = new App { Key = "long-url", Name = "Long" };
        await using var hub = new AppDbContext(options, new FixedAppContext(App.NoneId, hubWide: true));
        hub.Apps.Add(app);
        await hub.SaveChangesAsync();
        hub.AppReturnUrls.Add(new AppReturnUrl { AppId = app.Id, Url = "https://h/" + new string('a', 790) });
        await hub.SaveChangesAsync();
    });

    [MetadataSqlFact]
    public Task Replay_ids_are_unique_per_app() => MetadataDb.WithDatabaseAsync(async (options, _) =>
    {
        await using var db = new AppDbContext(options, new FixedAppContext(App.NoneId));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddMinutes(3);
        db.LaunchReplays.Add(new LaunchReplay { Jti = "same", AppId = a, ExpiresUtc = expires });
        db.LaunchReplays.Add(new LaunchReplay { Jti = "same", AppId = b, ExpiresUtc = expires });
        await db.SaveChangesAsync();

        await using var again = new AppDbContext(options, new FixedAppContext(App.NoneId));
        again.LaunchReplays.Add(new LaunchReplay { Jti = "same", AppId = a, ExpiresUtc = expires });
        await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync());
    });

    [MetadataSqlFact]
    public Task AddHubRegistry_applies_on_top_of_AddApps() => MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
    {
        await using (var db = new AppDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261110171537_AddApps");
        }

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        foreach (var table in new[] { "AppSigningKeys", "AppReturnUrls", "SeenUsers", "LaunchReplays", "DataProtectionKeys" })
        {
            Assert.Equal(1, await ScalarAsync(connectionString, $"SELECT COUNT(*) FROM sys.tables WHERE name = '{table}'"));
        }

        foreach (var (table, columns) in new[] { ("AppSigningKeys", 2), ("AppReturnUrls", 2), ("SeenUsers", 2) })
        {
            Assert.Equal(1, await ScalarAsync(connectionString,
                $"SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('{table}') AND is_unique = 1 AND is_primary_key = 0 AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id = indexes.object_id AND c.index_id = indexes.index_id) = {columns}"));
        }

        Assert.Equal(1, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM Apps WHERE Id = '" + App.DefaultId + "' AND ScopeKeys = ''"));
    });

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
