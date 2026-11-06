namespace QueryBuilder.Domain.Enums;

public enum DataSourceProvider
{
    SqlServer = 0,
    PostgreSql = 1,
    MySql = 2,
    Sqlite = 3
}

/// <summary>Which object kinds a data source's catalog surfaces. Combined with
/// <see cref="Entities.DataSource.AllowedObjects"/> (if non-empty) for finer-grained control.</summary>
public enum CatalogScope
{
    Views = 0,
    Tables = 1,
    TablesAndViews = 2
}

public enum ColumnDataType
{
    Text = 0,
    Number = 1,
    Date = 2,
    DateTime = 3,
    Boolean = 4,
    Guid = 5,
    Unknown = 6
}

public enum SchemaObjectKind
{
    Table = 0,
    View = 1
}

public enum UnavailableReason
{
    NameClash = 0
}

public enum JoinType
{
    Inner = 0,
    Left = 1,
    Right = 2,
    Full = 3
}

public enum AggregateFunction
{
    None = 0,
    Sum = 1,
    Avg = 2,
    Count = 3,
    CountDistinct = 4,
    Min = 5,
    Max = 6
}

public enum SortDirection
{
    Asc = 0,
    Desc = 1
}

public enum FilterLogicalOperator
{
    And = 0,
    Or = 1
}

public enum FilterOperator
{
    Equals = 0,
    NotEquals = 1,
    GreaterThan = 2,
    GreaterOrEqual = 3,
    LessThan = 4,
    LessOrEqual = 5,
    Contains = 6,
    NotContains = 7,
    StartsWith = 8,
    EndsWith = 9,
    In = 10,
    NotIn = 11,
    Between = 12,
    IsNull = 13,
    IsNotNull = 14,
    IsTrue = 15,
    IsFalse = 16
}

public enum QueryAccessLevel
{
    Viewer = 0,
    Editor = 1,
    Owner = 2
}

/// <summary>
/// Governance-worthy events recorded to the audit log. Deliberately excludes high-frequency,
/// low-signal reads (catalog browsing, SQL preview) — only state changes and actual data access
/// (run/export) are tracked.
/// </summary>
public enum AuditAction
{
    QueryCreated = 0,
    QueryUpdated = 1,
    QueryDeleted = 2,
    QueryRun = 3,
    QueryExported = 4,
    QueryShared = 5,
    QueryUnshared = 6,
    QueryDisabled = 7,
    QueryEnabled = 8,
    DataSourceCatalogPolicyUpdated = 9,
    DataSourceDataScopeUpdated = 10,
    DataSourceRecordLimitUpdated = 11,
    VisualizationCreated = 12,
    VisualizationUpdated = 13,
    VisualizationDeleted = 14,
    DashboardCreated = 15,
    DashboardUpdated = 16,
    DashboardDeleted = 17,
    DashboardShared = 18,
    DashboardUnshared = 19
}

public enum DashboardTileKind
{
    Chart = 0,
    Text = 1,
    Section = 2
}

/// <summary>An admin's row-level data-scope decision for one catalog object. Not persisted as-is —
/// derived from (and written as) <see cref="Entities.DataScopeRule"/> rows.</summary>
public enum DataScopeState
{
    Undecided = 0,
    NotScoped = 1,
    Scoped = 2
}

public enum VisualizationType
{
    Bar = 0,
    Line = 1,
    Pie = 2,
    Counter = 3,
    Area = 4,
    HorizontalBar = 5,
    Combo = 6,
    Scatter = 7,
    Table = 8,
    Pivot = 9,
    Funnel = 10,
    Waterfall = 11,
    Trend = 12
}

public enum ValueFormatStyle
{
    Number = 0,
    Compact = 1,
    Currency = 2,
    Percent = 3
}

public enum VisualizationSort
{
    None = 0,
    Asc = 1,
    Desc = 2
}

public enum ParameterType
{
    Text = 0,
    Number = 1,
    Date = 2,
    DateTime = 3,
    DateRange = 4,
    DateTimeRange = 5,
    List = 6,
    QueryList = 7,
    Boolean = 8
}

/// <summary>Where a dashboard tile gets the value of one of its query's parameters.</summary>
public enum ParameterMappingSource
{
    /// <summary>The query's own default (or nothing, for an optional parameter).</summary>
    Default = 0,
    /// <summary>A dashboard-wide parameter, shared with other tiles.</summary>
    Dashboard = 1,
    /// <summary>A small control in this tile's header.</summary>
    Tile = 2,
    /// <summary>A value the editor set; viewers see no control.</summary>
    Fixed = 3
}
