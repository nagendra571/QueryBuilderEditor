namespace QueryBuilder.Domain.Entities;

public sealed class SavedQuery : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid DataSourceId { get; set; }
    public DataSource? DataSource { get; set; }

    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Serialized QueryDefinition (columns, joins, filters, sorts, parameters).</summary>
    public string DefinitionJson { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }

    public List<QueryShare> Shares { get; set; } = [];
}
