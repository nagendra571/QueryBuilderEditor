namespace QueryBuilder.Domain.Enums;

public enum DataSourceProvider
{
    SqlServer = 0,
    PostgreSql = 1,
    MySql = 2,
    Sqlite = 3
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
    QueryEnabled = 8
}
