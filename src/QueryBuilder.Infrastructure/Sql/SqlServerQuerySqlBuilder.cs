using System.Text;
using System.Text.Json;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Infrastructure.Sql;

/// <summary>
/// Generates T-SQL from a validated <see cref="QueryDefinition"/>. Every identifier in the
/// definition has already been checked against the live catalog by the caller — this class
/// never accepts free-form identifier text. Every literal filter value is bound as a
/// parameter, never concatenated into the SQL text.
/// </summary>
public sealed class SqlServerQuerySqlBuilder : IQuerySqlBuilder
{
    public DataSourceProvider Provider => DataSourceProvider.SqlServer;

    public GeneratedQuery Build(QueryDefinition definition, IReadOnlyList<ScopePredicate> scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var parameters = new List<GeneratedQueryParameter>();
        var parameterCounter = 0;

        string NextParamName() => $"@p{parameterCounter++}";

        var sql = new StringBuilder();

        AppendSelect(sql, definition);
        AppendFrom(sql, definition);
        AppendJoins(sql, definition);
        AppendWhere(sql, definition.Filters, scope, parameters, NextParamName);

        var groupByColumns = ResolveGroupByColumns(definition);
        if (groupByColumns.Count > 0)
        {
            sql.Append("\nGROUP BY ").Append(string.Join(", ", groupByColumns.Select(c => SqlIdentifier.QualifiedColumn(c.TableAlias, c.ColumnName))));
        }

        if (definition.Having is not null && (definition.Having.Conditions.Count > 0 || definition.Having.Groups.Count > 0))
        {
            sql.Append("\nHAVING ");
            AppendFilterGroup(sql, definition.Having, parameters, NextParamName, isHaving: true, definition);
        }

        AppendOrderBy(sql, definition);

        return new GeneratedQuery(sql.ToString(), parameters);
    }

    private static void AppendSelect(StringBuilder sql, QueryDefinition definition)
    {
        var visibleColumns = definition.Columns.Where(c => c.IsVisible).OrderBy(c => c.OrderIndex).ToList();
        sql.Append("SELECT ");
        sql.Append(string.Join(",\n       ", visibleColumns.Select(c => $"{ColumnExpression(c)} AS {SqlIdentifier.Quote(c.EffectiveAlias)}")));
    }

    private static string ColumnExpression(QueryColumn column)
    {
        var columnRef = SqlIdentifier.QualifiedColumn(column.TableAlias, column.ColumnName);
        return column.Aggregate == AggregateFunction.None ? columnRef : AggregateExpression(column.Aggregate, columnRef);
    }

    private static string AggregateExpression(AggregateFunction aggregate, string columnRef) => aggregate switch
    {
        AggregateFunction.Sum => $"SUM({columnRef})",
        AggregateFunction.Avg => $"AVG({columnRef})",
        AggregateFunction.Count => $"COUNT({columnRef})",
        AggregateFunction.CountDistinct => $"COUNT(DISTINCT {columnRef})",
        AggregateFunction.Min => $"MIN({columnRef})",
        AggregateFunction.Max => $"MAX({columnRef})",
        _ => columnRef
    };

    private static void AppendFrom(StringBuilder sql, QueryDefinition definition)
    {
        var source = definition.Source;
        sql.Append("\nFROM ")
            .Append(SqlIdentifier.Quote(source.SchemaName)).Append('.').Append(SqlIdentifier.Quote(source.ObjectName))
            .Append(" AS ").Append(SqlIdentifier.Quote(source.Alias));
    }

    private static void AppendJoins(StringBuilder sql, QueryDefinition definition)
    {
        foreach (var join in definition.Joins)
        {
            var joinKeyword = join.JoinType switch
            {
                JoinType.Left => "LEFT JOIN",
                JoinType.Right => "RIGHT JOIN",
                JoinType.Full => "FULL OUTER JOIN",
                _ => "INNER JOIN"
            };

            sql.Append('\n').Append(joinKeyword).Append(' ')
                .Append(SqlIdentifier.Quote(join.SchemaName)).Append('.').Append(SqlIdentifier.Quote(join.ObjectName))
                .Append(" AS ").Append(SqlIdentifier.Quote(join.Alias))
                .Append(" ON ")
                .Append(string.Join(" AND ", join.Conditions.Select(c =>
                    $"{SqlIdentifier.QualifiedColumn(c.LeftAlias, c.LeftColumn)} = {SqlIdentifier.QualifiedColumn(c.RightAlias, c.RightColumn)}")));
        }
    }

    /// <summary>
    /// Row-level data-scope predicates go first and are ANDed with the user's filter group, which is
    /// parenthesized whenever scope is present — the user's group may be OR-combined at its top
    /// level, and without the parentheses <c>scope AND a OR b</c> would let <c>b</c> bypass the scope.
    /// Being in WHERE (not HAVING) also means totals/GROUP BY only ever see in-scope rows.
    /// </summary>
    private static void AppendWhere(
        StringBuilder sql, FilterGroup filters, IReadOnlyList<ScopePredicate> scope,
        List<GeneratedQueryParameter> parameters, Func<string> nextParamName)
    {
        var parts = new List<string>();
        var scopeParameterCounter = 0;
        foreach (var predicate in scope)
        {
            if (predicate.Values.Count == 0)
            {
                // The guard never produces this (no values = object hidden), but if it ever did, the
                // only safe reading of "allowed values: none" is no rows.
                parts.Add("(1 = 0)");
                continue;
            }
            var names = predicate.Values.Select(value =>
            {
                var name = $"@{QueryParameterNames.ReservedPrefix}scope{scopeParameterCounter++}";
                parameters.Add(new GeneratedQueryParameter(name, predicate.DataType, value, IsRuntimeParameter: false, RuntimeParameterName: null));
                return name;
            }).ToList();
            parts.Add($"({SqlIdentifier.QualifiedColumn(predicate.Alias, predicate.ColumnName)} IN ({string.Join(", ", names)}))");
        }

        if (filters.Conditions.Count > 0 || filters.Groups.Count > 0)
        {
            var userFilters = new StringBuilder();
            AppendFilterGroup(userFilters, filters, parameters, nextParamName, isHaving: false, null);
            parts.Add(scope.Count > 0 ? $"({userFilters})" : userFilters.ToString());
        }

        if (parts.Count > 0)
        {
            sql.Append("\nWHERE ").Append(string.Join(" AND ", parts));
        }
    }

    private static void AppendFilterGroup(
        StringBuilder sql, FilterGroup group, List<GeneratedQueryParameter> parameters, Func<string> nextParamName,
        bool isHaving, QueryDefinition? definition)
    {
        var parts = new List<string>();

        foreach (var condition in group.Conditions)
        {
            parts.Add(BuildCondition(condition, parameters, nextParamName));
        }

        foreach (var nested in group.Groups)
        {
            var nestedSql = new StringBuilder();
            AppendFilterGroup(nestedSql, nested, parameters, nextParamName, isHaving, definition);
            parts.Add($"({nestedSql})");
        }

        var op = group.Operator == FilterLogicalOperator.Or ? " OR " : " AND ";
        sql.Append(string.Join(op, parts));
    }

    private static string BuildCondition(FilterCondition condition, List<GeneratedQueryParameter> parameters, Func<string> nextParamName)
    {
        var columnRef = SqlIdentifier.QualifiedColumn(condition.TableAlias, condition.ColumnName);
        var expression = condition.Aggregate == AggregateFunction.None ? columnRef : AggregateExpression(condition.Aggregate, columnRef);

        switch (condition.Operator)
        {
            case FilterOperator.IsNull:
                return $"{expression} IS NULL";
            case FilterOperator.IsNotNull:
                return $"{expression} IS NOT NULL";
            case FilterOperator.IsTrue:
                return $"{expression} = 1";
            case FilterOperator.IsFalse:
                return $"{expression} = 0";
        }

        if (condition.Operator == FilterOperator.Between)
        {
            var (lo, hi) = ParseBetween(condition.Value);
            var loParam = AddParameter(parameters, nextParamName, condition, lo);
            var hiParam = AddParameter(parameters, nextParamName, condition, hi);
            return $"{expression} BETWEEN {loParam} AND {hiParam}";
        }

        if (condition.Operator is FilterOperator.In or FilterOperator.NotIn)
        {
            var values = ParseArray(condition.Value);
            if (values.Count == 0)
            {
                // An empty IN-list is never true / always true for NOT IN.
                return condition.Operator == FilterOperator.In ? "1 = 0" : "1 = 1";
            }
            var paramNames = values.Select(v => AddParameter(parameters, nextParamName, condition, v, allowRuntime: false));
            var keyword = condition.Operator == FilterOperator.In ? "IN" : "NOT IN";
            return $"{expression} {keyword} ({string.Join(", ", paramNames)})";
        }

        var literal = condition.Value;
        string paramName;
        switch (condition.Operator)
        {
            case FilterOperator.Contains:
                paramName = AddParameter(parameters, nextParamName, condition, literal, wildcard: WildcardMode.Both);
                return $"{expression} LIKE {paramName}";
            case FilterOperator.NotContains:
                paramName = AddParameter(parameters, nextParamName, condition, literal, wildcard: WildcardMode.Both);
                return $"{expression} NOT LIKE {paramName}";
            case FilterOperator.StartsWith:
                paramName = AddParameter(parameters, nextParamName, condition, literal, wildcard: WildcardMode.End);
                return $"{expression} LIKE {paramName}";
            case FilterOperator.EndsWith:
                paramName = AddParameter(parameters, nextParamName, condition, literal, wildcard: WildcardMode.Start);
                return $"{expression} LIKE {paramName}";
            case FilterOperator.NotEquals:
                paramName = AddParameter(parameters, nextParamName, condition, literal);
                return $"{expression} <> {paramName}";
            case FilterOperator.GreaterThan:
                paramName = AddParameter(parameters, nextParamName, condition, literal);
                return $"{expression} > {paramName}";
            case FilterOperator.GreaterOrEqual:
                paramName = AddParameter(parameters, nextParamName, condition, literal);
                return $"{expression} >= {paramName}";
            case FilterOperator.LessThan:
                paramName = AddParameter(parameters, nextParamName, condition, literal);
                return $"{expression} < {paramName}";
            case FilterOperator.LessOrEqual:
                paramName = AddParameter(parameters, nextParamName, condition, literal);
                return $"{expression} <= {paramName}";
            default:
                paramName = AddParameter(parameters, nextParamName, condition, literal);
                return $"{expression} = {paramName}";
        }
    }

    private enum WildcardMode { None, Both, Start, End }

    private static string AddParameter(
        List<GeneratedQueryParameter> parameters, Func<string> nextParamName, FilterCondition condition,
        string? value, WildcardMode wildcard = WildcardMode.None, bool allowRuntime = true)
    {
        var literal = wildcard switch
        {
            WildcardMode.Both => $"%{value}%",
            WildcardMode.Start => $"%{value}",
            WildcardMode.End => $"{value}%",
            _ => value
        };

        var isRuntime = allowRuntime && condition.IsParameterized;
        if (isRuntime && !QueryParameterNames.IsValid(condition.ParameterName))
        {
            throw new CatalogValidationException($"'{condition.ParameterName}' is not a valid parameter name.");
        }
        var name = isRuntime ? $"@{condition.ParameterName}" : nextParamName();

        parameters.Add(new GeneratedQueryParameter(
            name, condition.DataType, literal, isRuntime, isRuntime ? condition.ParameterName : null));

        return name;
    }

    private static (string? lo, string? hi) ParseBetween(string? value)
    {
        var values = ParseArray(value);
        return (values.ElementAtOrDefault(0), values.ElementAtOrDefault(1));
    }

    private static List<string> ParseArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? [];
        }
        catch (JsonException)
        {
            return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }
    }

    private static List<QueryColumnRef> ResolveGroupByColumns(QueryDefinition definition)
    {
        if (definition.GroupBy.Count > 0)
        {
            return definition.GroupBy;
        }

        var hasAggregate = definition.Columns.Any(c => c.Aggregate != AggregateFunction.None);
        if (!hasAggregate)
        {
            return [];
        }

        return definition.Columns
            .Where(c => c.IsVisible && c.Aggregate == AggregateFunction.None)
            .Select(c => new QueryColumnRef { TableAlias = c.TableAlias, ColumnName = c.ColumnName })
            .ToList();
    }

    private static void AppendOrderBy(StringBuilder sql, QueryDefinition definition)
    {
        if (definition.Sorts.Count == 0)
        {
            return;
        }

        var ordered = definition.Sorts.OrderBy(s => s.OrderIndex).Select(sort =>
        {
            var matchingColumn = definition.Columns.FirstOrDefault(c =>
                c.TableAlias == sort.TableAlias && c.ColumnName == sort.ColumnName);
            var expression = matchingColumn is not null
                ? ColumnExpression(matchingColumn)
                : SqlIdentifier.QualifiedColumn(sort.TableAlias, sort.ColumnName);
            var direction = sort.Direction == SortDirection.Desc ? "DESC" : "ASC";
            return $"{expression} {direction}";
        });

        sql.Append("\nORDER BY ").Append(string.Join(", ", ordered));
    }
}
