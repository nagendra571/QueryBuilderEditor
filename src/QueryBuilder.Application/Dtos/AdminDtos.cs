using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Dtos;

public sealed record AdminDataSourceSummaryDto(Guid Id, string Name, DataSourceProvider Provider, bool IsActive);

/// <param name="Connection">Null unless the source is managed and its details can be read. Never carries the password.</param>
/// <param name="ConnectionUnreadable">A managed source whose details can't be decrypted with the current key.</param>
/// <param name="ConnectionStringName">Read-only; set only for a source whose connection comes from host configuration, never the managed sentinel.</param>
/// <param name="CatalogError">Why the object list is empty when the data source couldn't be reached, so the admin can still fix the connection.</param>
public sealed record AdminDataSourceDetailDto(
    Guid Id,
    string Name,
    CatalogScope CatalogScope,
    List<string> AllowedObjects,
    List<SchemaObjectMetadata> Objects,
    int? MaxRecords,
    int? DefaultMaxRecords,
    List<AdminUnavailableObject> Unavailable,
    string? Description,
    bool IsActive,
    bool IsManaged,
    DataSourceProvider Provider,
    ConnectionDto? Connection,
    bool ConnectionUnreadable,
    string? ConnectionStringName,
    string? CatalogError,
    bool UsesMetadataDatabase);

public sealed record CreateDataSourceRequest(string? Name, string? Description, DataSourceProvider Provider, ConnectionInput? Connection);

/// <param name="Connection">Omitted to change only the name and description.</param>
public sealed record UpdateDataSourceRequest(string? Name, string? Description, ConnectionInput? Connection);

public sealed record SetDataSourceActiveRequest(bool IsActive);

/// <param name="ExistingId">With a blank password, reuses that data source's stored one if host, port and username are unchanged.</param>
public sealed record TestConnectionRequest(DataSourceProvider Provider, ConnectionInput? Connection, Guid? ExistingId);

public sealed record UpdateCatalogPolicyRequest(CatalogScope CatalogScope, List<string> AllowedObjects);

/// <param name="MaxRecords">Null clears the data source's own limit (falls back to the host default).</param>
public sealed record UpdateRecordLimitRequest(int? MaxRecords);

public sealed record AdminDataScopeDto(
    bool Enabled,
    List<string> DeclaredKeys,
    List<string> HostKeys,
    List<string> DataSourceKeys,
    List<AdminDataScopeObjectDto> Objects,
    List<AdminUnavailableObject> Unavailable);

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

public sealed record UpdateScopeKeysRequest(List<string>? Keys);

public sealed record UpdateDataScopeRequest(List<DataScopeObjectRuleDto> Objects);
