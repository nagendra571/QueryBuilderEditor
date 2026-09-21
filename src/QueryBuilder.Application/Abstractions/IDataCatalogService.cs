using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Abstractions;

/// <summary>Introspects a registered data source and returns its browsable schema/table/column tree.</summary>
public interface IDataCatalogService
{
    Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="Exceptions.CatalogValidationException"/> if any referenced table/column does not exist.</summary>
    Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken);
}
