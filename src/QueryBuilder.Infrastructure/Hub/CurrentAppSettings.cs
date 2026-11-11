using Microsoft.Extensions.Caching.Memory;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

/// <summary>Reads the ceiling through <see cref="ICurrentAppReader"/> and keeps it for at most 30 s, so a change in the
/// registry reaches every instance quickly without a query per request.</summary>
internal sealed class CurrentAppSettings(ICurrentAppReader apps, IAppContext appContext, MetadataMode mode, IMemoryCache cache) : ICurrentAppSettings
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    public async Task<int?> MaxRecordsAsync(CancellationToken ct)
    {
        if (!mode.Hub || appContext.IsHubWide)
        {
            return null;
        }

        var key = $"appmax:{appContext.AppId}";
        if (cache.TryGetValue(key, out CachedCeiling? hit) && hit is not null)
        {
            return hit.Value;
        }

        var app = await apps.GetCurrentAsync(ct);
        cache.Set(key, new CachedCeiling(app?.MaxRecords), Lifetime);
        return app?.MaxRecords;
    }

    private sealed record CachedCeiling(int? Value);
}
