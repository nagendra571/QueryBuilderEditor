using System.Data.Common;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Abstractions;

/// <summary>Tells whether a candidate connection reaches QueryBuilder's own metadata database, by identity (server,
/// database, machine) rather than by connection string, so aliases and IPs can't get around it. Always false outside
/// hub mode, for PostgreSQL, and when the candidate can't be reached.</summary>
public interface IMetadataDatabaseIdentity
{
    Task<bool> IsSameAsync(DbConnection openCandidate, CancellationToken ct);

    Task<bool> IsSameAsync(DataSourceProvider provider, string connectionString, CancellationToken ct);

    Task<bool> IsSameAsync(DataSourceProvider provider, ManagedConnectionDetails details, CancellationToken ct);
}

public static class MetadataDatabaseMessages
{
    public const string Refused = "A data source can't point at QueryBuilder's own database.";
}
