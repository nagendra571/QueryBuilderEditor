using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Entities;

/// <summary>
/// A saved chart over one saved query's output. It stores no data — only how to draw the query's
/// result — and is rendered server-side against the saved query's current definition, so the
/// viewer's own row-level scope always applies. <see cref="OptionsJson"/> is a serialized
/// <see cref="Model.VisualizationOptions"/>.
/// </summary>
public sealed class QueryVisualization : AuditableEntity, IAppOwned
{
    public Guid AppId { get; set; }

    public Guid SavedQueryId { get; set; }
    public SavedQuery? SavedQuery { get; set; }
    public string Name { get; set; } = string.Empty;
    public VisualizationType Type { get; set; } = VisualizationType.Bar;
    public string OptionsJson { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
