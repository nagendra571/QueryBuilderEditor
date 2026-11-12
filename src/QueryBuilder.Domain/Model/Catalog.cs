using System.Text.Json.Serialization;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Model;

/// <summary>Live-introspected metadata tree for a data source — never persisted, always read fresh (short-cached).</summary>
public sealed class DataSourceCatalog
{
    public Guid DataSourceId { get; set; }
    public DataSourceProvider Provider { get; set; }
    public List<SchemaMetadata> Schemas { get; set; } = [];

    /// <summary>Objects whose full name clashes with another's. Kept out of <see cref="Schemas"/> and
    /// without columns, so nothing downstream can validate, scope or query them.</summary>
    public List<UnavailableObject> UnavailableObjects { get; set; } = [];
}

public sealed class UnavailableObject
{
    public string SchemaName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SchemaObjectKind Kind { get; set; }
    public UnavailableReason Reason { get; set; }
}

/// <summary>Admin-only: names the clashing objects, which business responses must never carry.</summary>
public sealed class AdminUnavailableObject
{
    public string SchemaName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SchemaObjectKind Kind { get; set; }
    public UnavailableReason Reason { get; set; }
    public List<string> ClashesWith { get; set; } = [];
}

public sealed class SchemaMetadata
{
    public string Name { get; set; } = string.Empty;
    public List<SchemaObjectMetadata> Objects { get; set; } = [];
}

public sealed class SchemaObjectMetadata
{
    public string SchemaName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SchemaObjectKind Kind { get; set; }
    public List<ColumnMetadata> Columns { get; set; } = [];
}

public sealed class ColumnMetadata
{
    public string Name { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public ColumnDataType DataType { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    [JsonIgnore]
    public string? ForeignKeyTable { get; set; }
    [JsonIgnore]
    public string? ForeignKeyColumn { get; set; }
    public int OrdinalPosition { get; set; }

    /// <summary>A nondeterministic collation can match values that differ by case or accent, so the
    /// column can't be trusted as an exact row-level scope filter.</summary>
    public bool HasNondeterministicCollation { get; set; }
}
