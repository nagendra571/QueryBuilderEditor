using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

/// <summary>Remembers launch token ids until the token could no longer pass its time checks, so each works once.</summary>
public sealed class LaunchReplayStore
{
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromMinutes(1);

    /// <summary>Rows are kept this long past their expiry. The validator compares whole seconds and instances' clocks drift,
    /// so deleting at the exact expiry would let a used token pass the time check after its row is gone.</summary>
    public static readonly TimeSpan RetentionMargin = TimeSpan.FromMinutes(5);
    private long _lastCleanupTicks;

    /// <summary>True when the id was new. A duplicate (app, jti) means the token was already used.</summary>
    internal async Task<bool> TryRecordAsync(AppDbContext db, Guid appId, string jti, DateTimeOffset expiresUtc, CancellationToken ct)
    {
        db.LaunchReplays.Add(new LaunchReplay { AppId = appId, Jti = jti, ExpiresUtc = expiresUtc });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>At most once a minute per process; a failure only delays cleanup.</summary>
    internal async Task CleanUpAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var last = Interlocked.Read(ref _lastCleanupTicks);
        if (now.UtcTicks - last < CleanupEvery.Ticks
            || Interlocked.CompareExchange(ref _lastCleanupTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        try
        {
            var cutoff = now - RetentionMargin;
            await db.LaunchReplays.Where(r => r.ExpiresUtc < cutoff).ExecuteDeleteAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Exchange(ref _lastCleanupTicks, last);
        }
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2627 or 2601 };
}
