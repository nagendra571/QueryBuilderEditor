using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Abstractions;

/// <summary>Introspects a registered data source and returns its browsable schema/table/column tree.</summary>
public interface IDataCatalogService
{
    Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="Exceptions.CatalogValidationException"/> if any referenced table/column does not exist.</summary>
    Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken);

    /// <summary>Every table/view the data source's connection can see (system-schema-excluded,
    /// <see cref="Entities.DataSource.AllowedSchemas"/>-scoped), bypassing
    /// <see cref="Entities.DataSource.CatalogScope"/>/<see cref="Entities.DataSource.AllowedObjects"/>
    /// entirely — for the admin picker, which needs to see everything to choose from. No column
    /// metadata is populated (not needed for picking, and this path isn't cached).</summary>
    Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken);

    /// <summary>Evicts the cached catalog for a data source so the next read reflects a recent
    /// change (e.g. an admin catalog-policy update) immediately instead of waiting out the cache TTL.</summary>
    void InvalidateCatalogCache(Guid dataSourceId);
}
