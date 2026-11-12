using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Infrastructure.Catalog;

/// <summary>Reads one provider's raw catalog. Policy (allowed schemas, kind, allowlist, name clashes) belongs to
/// <see cref="DataCatalogService"/>, so a reader only reports what the connection can see.</summary>
public interface ICatalogReader
{
    DataSourceProvider Provider { get; }

    /// <summary>Every table and view the login can read, system schemas excluded. No columns.</summary>
    Task<List<SchemaObjectMetadata>> ReadObjectsAsync(string connectionString, CancellationToken ct);

    /// <summary>Columns keyed by "schema.name", in a dictionary using <see cref="Application.Common.IdentifierMatching.Comparer"/>.</summary>
    Task<Dictionary<string, List<ColumnMetadata>>> ReadColumnsAsync(string connectionString, CancellationToken ct);
}
