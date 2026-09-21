using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Model;

/// <summary>
/// The full, serializable definition of a visually-built query. This is the single
/// source of truth persisted with a SavedQuery and re-hydrated into the builder UI.
/// </summary>
public sealed class QueryDefinition
{
    public QuerySource Source { get; set; } = new();
    public List<QueryJoin> Joins { get; set; } = [];
    public List<QueryColumn> Columns { get; set; } = [];
    public FilterGroup Filters { get; set; } = new();
    public List<QueryColumnRef> GroupBy { get; set; } = [];
    public FilterGroup? Having { get; set; }
    public List<QuerySort> Sorts { get; set; } = [];
    public List<QueryParameter> Parameters { get; set; } = [];
    public int? RowLimit { get; set; } = 1000;
}

public sealed class QuerySource
{
    public string SchemaName { get; set; } = "dbo";
    public string ObjectName { get; set; } = string.Empty;
    public SchemaObjectKind Kind { get; set; } = SchemaObjectKind.View;
    public string Alias { get; set; } = string.Empty;
}

public sealed class QueryJoin
{
    public string SchemaName { get; set; } = "dbo";
    public string ObjectName { get; set; } = string.Empty;
    public SchemaObjectKind Kind { get; set; } = SchemaObjectKind.Table;
    public string Alias { get; set; } = string.Empty;
    public JoinType JoinType { get; set; } = JoinType.Inner;

    /// <summary>Column-to-column equality pairs that make up the ON clause.</summary>
    public List<JoinCondition> Conditions { get; set; } = [];
}

public sealed class JoinCondition
{
    public string LeftAlias { get; set; } = string.Empty;
    public string LeftColumn { get; set; } = string.Empty;
    public string RightAlias { get; set; } = string.Empty;
    public string RightColumn { get; set; } = string.Empty;
}

public sealed class QueryColumnRef
{
    public string TableAlias { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
}

public sealed class QueryColumn
{
    public string TableAlias { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public ColumnDataType DataType { get; set; } = ColumnDataType.Text;
    public AggregateFunction Aggregate { get; set; } = AggregateFunction.None;
    public int OrderIndex { get; set; }
    public bool IsVisible { get; set; } = true;

    public string EffectiveAlias => string.IsNullOrWhiteSpace(Alias) ? ColumnName : Alias!;
}

public sealed class QuerySort
{
    public string TableAlias { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public SortDirection Direction { get; set; } = SortDirection.Asc;
    public int OrderIndex { get; set; }
}

public sealed class FilterGroup
{
    public FilterLogicalOperator Operator { get; set; } = FilterLogicalOperator.And;
    public List<FilterCondition> Conditions { get; set; } = [];
    public List<FilterGroup> Groups { get; set; } = [];

    public IEnumerable<FilterCondition> AllConditions() =>
        Conditions.Concat(Groups.SelectMany(g => g.AllConditions()));
}

public sealed class FilterCondition
{
    public string TableAlias { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public ColumnDataType DataType { get; set; } = ColumnDataType.Text;
    public AggregateFunction Aggregate { get; set; } = AggregateFunction.None;
    public FilterOperator Operator { get; set; } = FilterOperator.Equals;

    /// <summary>Literal value(s) for the condition. For Between/In this is a JSON array; otherwise a scalar.</summary>
    public string? Value { get; set; }

    /// <summary>When true, the value is not fixed — it is supplied at run time via a Parameter with this name.</summary>
    public bool IsParameterized { get; set; }
    public string? ParameterName { get; set; }
}

public sealed class QueryParameter
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ColumnDataType DataType { get; set; } = ColumnDataType.Text;
    public string? DefaultValue { get; set; }
    public bool IsRequired { get; set; } = true;
}
