using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence;

/// <summary>
/// Records which mode owns the metadata database and refuses to start in the other one: a hub and an embedded
/// app sharing a database would see each other's rows as the Default app's. Runs whether or not this app applies
/// migrations, since a DBA may have provisioned the schema. An embedded app whose schema is missing or whose
/// database is unreachable only logs a warning, so <c>/_setup</c> stays reachable to explain it.
/// </summary>
internal sealed class MetadataModeGuard(
    IServiceScopeFactory scopeFactory,
    MetadataMode mode,
    ILogger<MetadataModeGuard> logger) : IHostedService
{
    internal const string Key = "Mode";

    /// <summary>Test seam: runs between the read and the insert, where a concurrent first start can slip in.</summary>
    internal Func<Task>? BeforeInsert { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string? stored;
        try
        {
            stored = await ReadAsync(cancellationToken);
        }
        catch (Exception ex) when (!mode.Hub && IsDatabaseFailure(ex))
        {
            logger.LogWarning(ex,
                "Could not check which mode owns the QueryBuilder metadata database (schema missing or database unreachable); the check runs again on the next start. See /_setup.");
            return;
        }

        if (stored is null)
        {
            if (mode.Hub && await HoldsDataAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "This metadata database already holds QueryBuilder data but has no recorded mode; the hub needs its own empty metadata database.");
            }

            if (BeforeInsert is not null)
            {
                await BeforeInsert();
            }

            try
            {
                using var scope = CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.MetadataSettings.Add(new MetadataSetting { Key = Key, Value = mode.Name });
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex)
            {
                // A concurrent first start won the insert; compare with what it wrote.
                stored = await ReadAsync(cancellationToken);
                if (stored is null)
                {
                    if (mode.Hub)
                    {
                        throw;
                    }

                    // E.g. a DBA-managed login without INSERT on MetadataSettings; the mode just stays unrecorded.
                    logger.LogWarning(ex,
                        "Could not record the QueryBuilder metadata mode (the insert failed and nothing was found afterwards); continuing.");
                    return;
                }
            }
        }

        if (!string.Equals(stored, mode.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"This QueryBuilder metadata database belongs to {Article(stored)} {stored}-mode installation; this app is configured for {mode.Name} mode. Use a separate metadata database for the hub.");
        }
    }

    // EF sometimes wraps the provider's exception (for example when it opens a database that doesn't exist).
    private static bool IsDatabaseFailure(Exception ex) =>
        ex is DbException || ex.InnerException is DbException;

    private static string Article(string word) =>
        word.Length > 0 && "AEIOUaeiou".Contains(word[0]) ? "an" : "a";

    private IServiceScope CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAppContextScope>().UseApp(App.DefaultId);
        return scope;
    }

    private async Task<string?> ReadAsync(CancellationToken ct)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.MetadataSettings.AsNoTracking()
            .Where(s => s.Key == Key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
    }

    // IgnoreQueryFilters: existing rows may belong to any app, and any of them means this database isn't empty.
    private async Task<bool> HoldsDataAsync(CancellationToken ct)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DataSources.IgnoreQueryFilters().AnyAsync(ct)
            || await db.SavedQueries.IgnoreQueryFilters().AnyAsync(ct)
            || await db.Dashboards.IgnoreQueryFilters().AnyAsync(ct);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
