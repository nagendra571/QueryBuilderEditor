using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Entities;

/// <summary>
/// A page of chart and text tiles. It holds references to saved visualizations, never data, so each
/// viewer's own query access and row scope decide what a chart tile shows. <see cref="Version"/> is
/// an optimistic-concurrency token bumped on every rename or tile save.
/// </summary>
public sealed class Dashboard : AuditableEntity, IAppOwned
{
    public Guid AppId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public int Version { get; set; } = 1;

    /// <summary>Hub mode: visible read-only to every Viewer and Author of the app. Only Admins and super admins change it.</summary>
    public bool PublishedToApp { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>1 = pre-grid (width preset, height 1-3, positions derived from Order); 2 = positioned
    /// tiles in a 12-column grid. Legacy rows are converted on read and rewritten on the next save.</summary>
    public int GridVersion { get; set; } = 2;

    /// <summary>Serialized dashboard-wide parameters (name, title, type, options, current value). Null = none.</summary>
    public string? ParametersJson { get; set; }

    public List<DashboardTile> Tiles { get; set; } = [];
    public List<DashboardShare> Shares { get; set; } = [];
}

public sealed class DashboardTile : AuditableEntity, IAppOwned
{
    public Guid AppId { get; set; }

    public Guid DashboardId { get; set; }
    public Dashboard? Dashboard { get; set; }
    public DashboardTileKind Kind { get; set; }

    /// <summary>Null for text tiles, and for chart tiles whose visualization was deleted.</summary>
    public Guid? VisualizationId { get; set; }
    public QueryVisualization? Visualization { get; set; }

    public string? Title { get; set; }
    public string? Text { get; set; }
    public int Order { get; set; }

    /// <summary>Grid units: column and row of the top-left corner.</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 6;
    public int Height { get; set; } = 6;

    /// <summary>When true the browser sizes Height to the tile's content.</summary>
    public bool AutoHeight { get; set; }

    /// <summary>Serialized map of query parameter name to value source. Null = every parameter uses the
    /// query default. Kept even while the viewer may not see the tile, but never exposed to them.</summary>
    public string? ParameterMappingsJson { get; set; }
}

/// <summary>Grants another user access to a dashboard. Only Viewer and Editor are valid; ownership
/// lives on <see cref="Dashboard.OwnerId"/>. Never grants access to the charts' underlying queries.</summary>
public sealed class DashboardShare : AuditableEntity, IAppOwned
{
    public Guid AppId { get; set; }

    public Guid DashboardId { get; set; }
    public Dashboard? Dashboard { get; set; }
    public string SharedWithUserId { get; set; } = string.Empty;
    public string SharedWithUserEmail { get; set; } = string.Empty;
    public QueryAccessLevel AccessLevel { get; set; } = QueryAccessLevel.Viewer;
}
