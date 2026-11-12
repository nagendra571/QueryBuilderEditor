namespace QueryBuilder.Domain.Entities;

/// <summary>A key/value marker about the metadata database itself (for example which mode wrote to it). Not app-owned.</summary>
public sealed class MetadataSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
