using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Entities;

/// <summary>
/// A registered connection to a business database whose views/tables can be
/// exposed to business users through the query builder catalog.
/// </summary>
public sealed class DataSource : AuditableEntity, IAppOwned
{
    public Guid AppId { get; set; }

    /// <summary>
    /// Reserved <see cref="ConnectionStringName"/> value meaning "use QueryBuilder's own metadata
    /// connection string" instead of looking one up by name in configuration. Set on the data
    /// source QueryBuilder auto-registers on first run when none exist yet, so there's something
    /// to query out of the box.
    /// </summary>
    public const string DefaultConnectionStringSentinel = "__QueryBuilderDefault__";

    /// <summary>Reserved <see cref="ConnectionStringName"/> value for a data source whose connection was
    /// entered in the admin UI: the details live encrypted in <see cref="EncryptedConnection"/>.</summary>
    public const string ManagedConnectionSentinel = "__QueryBuilderManaged__";

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DataSourceProvider Provider { get; set; } = DataSourceProvider.SqlServer;

    /// <summary>Name of the connection string entry (in configuration / secret store) — never a raw secret.</summary>
    public string ConnectionStringName { get; set; } = string.Empty;

    /// <summary>The connection details of a managed data source (see <see cref="ManagedConnectionSentinel"/>),
    /// one encrypted blob bound to this row's Id. Null for every other data source.</summary>
    public string? EncryptedConnection { get; set; }

    public bool IsManaged => ConnectionStringName == ManagedConnectionSentinel;

    /// <summary>Restrict the catalog to these schemas only (empty = all non-system schemas).</summary>
    public List<string> AllowedSchemas { get; set; } = [];

    /// <summary>Restricts the catalog by object kind. Combined with <see cref="AllowedObjects"/>
    /// (if non-empty) for finer-grained control.</summary>
    public CatalogScope CatalogScope { get; set; } = CatalogScope.Views;

    /// <summary>Restrict the catalog to these specific "SchemaName.ObjectName" entries only
    /// (empty = every object matching <see cref="CatalogScope"/>/<see cref="AllowedSchemas"/> is
    /// exposed) — same "empty = no restriction" convention as <see cref="AllowedSchemas"/>.</summary>
    public List<string> AllowedObjects { get; set; } = [];

    /// <summary>Row-level data-scope keys an admin added to this data source, on top of the keys the host
    /// declares in code. Only names: each user's values still come from the host's resolver.</summary>
    public List<string> ScopeKeys { get; set; } = [];

    /// <summary>Most records a query against this data source may return: the grid shows the
    /// first this-many with a "narrow it down" warning, and exports beyond it are refused. Null =
    /// fall back to the host's <c>DefaultMaxRecords</c> option (and if that's unset too, no limit).</summary>
    public int? MaxRecords { get; set; }

    public bool IsActive { get; set; } = true;
}
