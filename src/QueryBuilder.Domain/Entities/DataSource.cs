using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Entities;

/// <summary>
/// A registered connection to a business database whose views/tables can be
/// exposed to business users through the query builder catalog.
/// </summary>
public sealed class DataSource : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DataSourceProvider Provider { get; set; } = DataSourceProvider.SqlServer;

    /// <summary>Name of the connection string entry (in configuration / secret store) — never a raw secret.</summary>
    public string ConnectionStringName { get; set; } = string.Empty;

    /// <summary>Restrict the catalog to these schemas only (empty = all non-system schemas).</summary>
    public List<string> AllowedSchemas { get; set; } = [];

    /// <summary>When true, only Views are surfaced in the catalog (recommended — business logic lives in views).</summary>
    public bool ViewsOnly { get; set; } = true;

    public bool IsActive { get; set; } = true;
}
