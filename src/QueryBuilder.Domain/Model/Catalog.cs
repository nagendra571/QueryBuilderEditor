using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Model;

/// <summary>Live-introspected metadata tree for a data source — never persisted, always read fresh (short-cached).</summary>
public sealed class DataSourceCatalog
{
    public Guid DataSourceId { get; set; }
    public List<SchemaMetadata> Schemas { get; set; } = [];
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
    public string? ForeignKeyTable { get; set; }
    public string? ForeignKeyColumn { get; set; }
    public int OrdinalPosition { get; set; }
}
