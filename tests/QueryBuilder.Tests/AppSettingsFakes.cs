using System.Data.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Tests;

internal sealed class FakeAppSettings(int? ceiling = null) : ICurrentAppSettings
{
    public int? Ceiling { get; set; } = ceiling;
    public Task<int?> MaxRecordsAsync(CancellationToken ct) => Task.FromResult(Ceiling);
}

internal sealed class FakeMetadataIdentity(bool same = false) : IMetadataDatabaseIdentity
{
    public bool Same { get; set; } = same;
    public Task<bool> IsSameAsync(DbConnection openCandidate, CancellationToken ct) => Task.FromResult(Same);
    public Task<bool> IsSameAsync(DataSourceProvider provider, string connectionString, CancellationToken ct) => Task.FromResult(Same);
    public Task<bool> IsSameAsync(DataSourceProvider provider, ManagedConnectionDetails details, CancellationToken ct) => Task.FromResult(Same);
}
