using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Dtos;

public sealed record AdminDataSourceSummaryDto(Guid Id, string Name, DataSourceProvider Provider, bool IsActive);

public sealed record AdminDataSourceDetailDto(
    Guid Id,
    string Name,
    CatalogScope CatalogScope,
    List<string> AllowedObjects,
    List<SchemaObjectMetadata> Objects);

public sealed record UpdateCatalogPolicyRequest(CatalogScope CatalogScope, List<string> AllowedObjects);
