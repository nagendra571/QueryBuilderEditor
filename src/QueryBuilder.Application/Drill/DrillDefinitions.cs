using System.Globalization;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Drill;

/// <summary>
/// Derives a drill query from a saved query and a click trail. The browser only ever sends the trail;
/// every transform here only narrows (an extra AND group), so the row-level data scope applied later by
/// the SQL builder can never be widened by a step.
/// </summary>
public static class DrillDefinitions
{
    public static readonly IReadOnlySet<VisualizationType> BreakdownTypes = new HashSet<VisualizationType>
    {
        VisualizationType.Bar, VisualizationType.HorizontalBar, VisualizationType.Line, VisualizationType.Area,
        VisualizationType.Combo, VisualizationType.Waterfall, VisualizationType.Pie, VisualizationType.Funnel
    };

    public static bool IsAggregated(QueryDefinition saved) =>
        saved.Columns.Any(c => c.IsVisible && c.Aggregate != AggregateFunction.None);

    public static IReadOnlyList<string> AllowedStepColumns(QueryDefinition saved, IReadOnlyList<string> drillColumns) =>
        saved.Columns
            .Where(c => c.IsVisible && c.Aggregate == AggregateFunction.None)
            .Select(c => c.EffectiveAlias)
            .Concat(drillColumns)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static QueryDefinition ApplySteps(
        QueryDefinition definition, IReadOnlyList<DrillStep> steps, IReadOnlyList<string> drillColumns,
        IReadOnlyDictionary<string, ColumnDataType>? drillColumnTypes = null,
        Func<string, string, bool>? sameIdentifier = null) =>
        ApplyStepsCore(definition, steps, drillColumns, drillColumnTypes, sameIdentifier);

    private static QueryDefinition ApplyStepsCore(
        QueryDefinition definition, IReadOnlyList<DrillStep> steps, IReadOnlyList<string> drillColumns,
        IReadOnlyDictionary<string, ColumnDataType>? drillColumnTypes, Func<string, string, bool>? sameIdentifier)
    {
        var copy = Clone(definition);
        if (steps.Count == 0) return copy;

        if (steps.Count > DrillLimits.MaxSteps)
            throw new CatalogValidationException($"A drill trail can have at most {DrillLimits.MaxSteps} steps.");

        var conditions = new List<FilterCondition>();
        foreach (var step in steps)
        {
            if (step.Value is { Length: > DrillLimits.MaxValueLength })
                throw new CatalogValidationException($"A drill value can be at most {DrillLimits.MaxValueLength} characters.");
            conditions.Add(ToCondition(definition, step, drillColumns, drillColumnTypes, sameIdentifier));
        }

        var original = copy.Filters;
        var hasOriginal = original.Conditions.Count > 0 || original.Groups.Count > 0;
        var groups = new List<FilterGroup>();
        if (hasOriginal) groups.Add(original);
        groups.Add(new FilterGroup { Operator = FilterLogicalOperator.And, Conditions = conditions });
        copy.Filters = new FilterGroup { Operator = FilterLogicalOperator.And, Groups = groups };
        return copy;
    }

    public static QueryDefinition ForRows(
        QueryDefinition saved, IReadOnlyList<DrillStep> steps, IReadOnlyList<string> drillColumns,
        Func<string, string, bool>? sameIdentifier = null,
        IReadOnlyDictionary<string, ColumnDataType>? drillColumnTypes = null)
    {
        // Ordinal unless the caller passes the provider's comparer: folding case would merge PostgreSQL's
        // distinct "amount" and "Amount".
        sameIdentifier ??= (a, b) => string.Equals(a, b, StringComparison.Ordinal);
        var lookupIdentifier = sameIdentifier;
        var result = ApplyStepsCore(saved, steps, drillColumns, drillColumnTypes, lookupIdentifier);

        var visible = result.Columns.Where(c => c.IsVisible).ToList();
        var columns = new List<QueryColumn>();

        bool Has(string alias, string column) =>
            columns.Any(c => sameIdentifier(c.TableAlias, alias) && sameIdentifier(c.ColumnName, column));

        foreach (var c in visible.Where(c => c.Aggregate == AggregateFunction.None))
            columns.Add(c);

        // A count over a column becomes that column itself, so each row shows what was counted.
        foreach (var c in visible.Where(c => c.Aggregate != AggregateFunction.None))
        {
            if (Has(c.TableAlias, c.ColumnName)) continue;
            columns.Add(new QueryColumn
            {
                TableAlias = c.TableAlias,
                ColumnName = c.ColumnName,
                Alias = c.ColumnName,
                DataType = c.DataType
            });
        }

        foreach (var name in drillColumns)
        {
            if (Has(result.Source.Alias, name)) continue;
            columns.Add(new QueryColumn
            {
                TableAlias = result.Source.Alias,
                ColumnName = name,
                DataType = DrillColumnType(drillColumnTypes, name, lookupIdentifier)
            });
        }

        for (var i = 0; i < columns.Count; i++) columns[i].OrderIndex = i;

        result.Columns = columns;
        result.GroupBy = [];
        result.Having = null;
        result.Sorts = result.Sorts
            .Where(s => Has(s.TableAlias, s.ColumnName))
            .ToList();
        return result;
    }

    public static (QueryDefinition Definition, VisualizationOptions Options) ForBreakdown(
        QueryDefinition saved, VisualizationType type, VisualizationOptions options,
        IReadOnlyList<DrillStep> steps, string breakdownColumn, IReadOnlyList<string> drillColumns,
        IReadOnlyDictionary<string, ColumnDataType>? drillColumnTypes = null,
        Func<string, string, bool>? sameIdentifier = null)
    {
        sameIdentifier ??= (a, b) => string.Equals(a, b, StringComparison.Ordinal);
        if (!BreakdownTypes.Contains(type) || !IsAggregated(saved))
            throw new CatalogValidationException("This chart can't be broken down.");

        if (!AllowedStepColumns(saved, drillColumns).Contains(breakdownColumn, StringComparer.Ordinal)
            || steps.Any(s => string.Equals(s.Column, breakdownColumn, StringComparison.Ordinal)))
            throw new CatalogValidationException($"'{breakdownColumn}' can't be used to break down this chart.");

        var result = ApplySteps(saved, steps, drillColumns, drillColumnTypes, sameIdentifier);
        var newOptions = VisualizationOptionsSerializer.Deserialize(VisualizationOptionsSerializer.Serialize(options));
        newOptions.Normalize();

        var visible = result.Columns.Where(c => c.IsVisible).ToList();
        var columns = new List<QueryColumn>();

        void AddPlain(string name)
        {
            if (columns.Any(c => c.EffectiveAlias == name)) return;
            var existing = visible.FirstOrDefault(c => c.Aggregate == AggregateFunction.None && c.EffectiveAlias == name);
            // Only a drill column may be made up; anything else (a stale Series naming a hidden column)
            // would reach a column the saved query never exposed.
            if (existing is null && !drillColumns.Contains(name, StringComparer.Ordinal))
                throw new CatalogValidationException("This chart can't be broken down.");
            columns.Add(existing ?? new QueryColumn
            {
                TableAlias = result.Source.Alias,
                ColumnName = name,
                DataType = DrillColumnType(drillColumnTypes, name, sameIdentifier)
            });
        }

        AddPlain(breakdownColumn);
        if (!string.IsNullOrEmpty(newOptions.Series) && newOptions.Series != breakdownColumn) AddPlain(newOptions.Series);

        var measureNames = newOptions.Measures.Concat(type == VisualizationType.Combo ? newOptions.LineMeasures : []);
        foreach (var name in measureNames.Distinct(StringComparer.Ordinal))
        {
            if (columns.Any(c => c.EffectiveAlias == name)) continue;
            var measure = visible.FirstOrDefault(c => c.EffectiveAlias == name);
            if (measure is not null) columns.Add(measure);
        }

        for (var i = 0; i < columns.Count; i++) columns[i].OrderIndex = i;

        var plain = columns.Where(c => c.Aggregate == AggregateFunction.None).ToList();
        result.Columns = columns;
        result.GroupBy = plain
            .GroupBy(c => (c.TableAlias, c.ColumnName))
            .Select(g => new QueryColumnRef { TableAlias = g.Key.TableAlias, ColumnName = g.Key.ColumnName })
            .ToList();
        result.Having = null;
        // A sort on a column that is neither grouped nor selected would be invalid SQL once regrouped.
        result.Sorts = result.Sorts
            .Where(s => columns.Any(c => sameIdentifier(c.TableAlias, s.TableAlias) && sameIdentifier(c.ColumnName, s.ColumnName)))
            .ToList();

        newOptions.X = breakdownColumn;
        if (newOptions.Series == breakdownColumn) newOptions.Series = null;

        var issues = VisualizationOptionsValidator.Validate(type, newOptions, result);
        if (issues.Count > 0)
            throw new CatalogValidationException(string.Join(" ", issues.Select(i => i.Message)));

        return (result, newOptions);
    }

    private static FilterCondition ToCondition(
        QueryDefinition saved, DrillStep step, IReadOnlyList<string> drillColumns,
        IReadOnlyDictionary<string, ColumnDataType>? drillColumnTypes, Func<string, string, bool>? sameIdentifier)
    {
        var source = saved.Columns.FirstOrDefault(c =>
            c.IsVisible && c.Aggregate == AggregateFunction.None && c.EffectiveAlias == step.Column);

        string tableAlias, columnName;
        ColumnDataType dataType;
        if (source is not null)
        {
            (tableAlias, columnName, dataType) = (source.TableAlias, source.ColumnName, source.DataType);
        }
        else if (drillColumns.Contains(step.Column, StringComparer.Ordinal))
        {
            (tableAlias, columnName, dataType) = (saved.Source.Alias, step.Column, DrillColumnType(drillColumnTypes, step.Column, sameIdentifier));
        }
        else
        {
            throw new CatalogValidationException($"'{step.Column}' can't be used to drill into this chart.");
        }

        var condition = new FilterCondition { TableAlias = tableAlias, ColumnName = columnName, DataType = dataType };
        if (step.Value is null)
        {
            condition.Operator = FilterOperator.IsNull;
        }
        else if (dataType == ColumnDataType.Boolean)
        {
            condition.Operator = step.Value switch
            {
                "true" => FilterOperator.IsTrue,
                "false" => FilterOperator.IsFalse,
                _ => throw new CatalogValidationException($"'{step.Column}' is a yes/no column; its value must be true or false.")
            };
        }
        else
        {
            // Checked here so a bad value is a clear 400, not a failure (or, on SQL Server, a 500) at execution time.
            if (!IsValidLiteral(dataType, step.Value))
                throw new CatalogValidationException($"'{step.Value}' is not a valid {dataType} value for '{step.Column}'.");
            condition.Operator = FilterOperator.Equals;
            condition.Value = step.Value;
        }
        return condition;
    }

    // Mirrors QueryParameterValues, which converts the same literals when the query runs.
    private static bool IsValidLiteral(ColumnDataType type, string value) => type switch
    {
        ColumnDataType.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        ColumnDataType.Date or ColumnDataType.DateTime => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        ColumnDataType.Guid => Guid.TryParse(value, out _),
        _ => true
    };

    private static ColumnDataType DrillColumnType(
        IReadOnlyDictionary<string, ColumnDataType>? types, string name, Func<string, string, bool>? same)
    {
        if (types is null) return ColumnDataType.Text;
        if (same is null) return types.TryGetValue(name, out var exact) ? exact : ColumnDataType.Text;
        foreach (var (key, type) in types)
            if (same(key, name)) return type;
        return ColumnDataType.Text;
    }

    private static QueryDefinition Clone(QueryDefinition definition) =>
        QueryDefinitionSerializer.Deserialize(QueryDefinitionSerializer.Serialize(definition));
}
