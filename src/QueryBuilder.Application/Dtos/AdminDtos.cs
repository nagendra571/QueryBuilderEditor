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

public sealed record AdminDataScopeDto(
    bool Enabled,
    List<string> DeclaredKeys,
    List<AdminDataScopeObjectDto> Objects);

/// <param name="Mappings">Declared scope key → column name, only for <see cref="DataScopeState.Scoped"/>.</param>
/// <param name="StaleKeys">Keys mapped in the database that the host no longer declares — the
/// object is hidden from scoped users until the admin re-saves it without them.</param>
public sealed record AdminDataScopeObjectDto(
    string ObjectName,
    SchemaObjectKind Kind,
    List<string> Columns,
    DataScopeState State,
    Dictionary<string, string> Mappings,
    List<string> StaleKeys);

public sealed record DataScopeObjectRuleDto(string ObjectName, DataScopeState State, Dictionary<string, string>? Mappings);

public sealed record UpdateDataScopeRequest(List<DataScopeObjectRuleDto> Objects);
