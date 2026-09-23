namespace QueryBuilder.Domain.Entities;

/// <summary>
/// One admin decision about row-level data scoping for one catalog object of one data source.
/// An object with no rules is "undecided" and hidden from scoped users (fail closed). A rule with
/// a null <see cref="ScopeKey"/> marks the object "not scoped" (visible to everyone, unfiltered);
/// otherwise each rule binds a host-declared scope key (e.g. "ProgramId") to one of the object's
/// columns, and a scoped user sees only rows whose column value is in their values for that key.
/// </summary>
public sealed class DataScopeRule : AuditableEntity
{
    public Guid DataSourceId { get; set; }

    /// <summary>"SchemaName.ObjectName", same format as <see cref="DataSource.AllowedObjects"/>.</summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>Null = the "not scoped" marker.</summary>
    public string? ScopeKey { get; set; }

    /// <summary>Null exactly when <see cref="ScopeKey"/> is null.</summary>
    public string? ColumnName { get; set; }
}
