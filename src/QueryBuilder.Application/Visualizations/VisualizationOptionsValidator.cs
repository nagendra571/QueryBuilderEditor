using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Visualizations;

/// <param name="MissingColumn">Set when the problem is a referenced column that is not (or no longer)
/// a visible output of the query, so the UI can say which one.</param>
public sealed record VisualizationIssue(string Message, string? MissingColumn = null);

/// <summary>An output column of a query: its result-set name and the type it will have.</summary>
public sealed record OutputColumn(string Name, ColumnDataType DataType);

public static class VisualizationOptionsValidator
{
    public const int MaxPieSlices = 12;
    public const int MaxTableColumns = 30;
    public const int MaxPivotRows = 3;
    public const int MaxPivotValues = 5;
    public const int MaxPivotColumns = 50;
    public const int MaxFunnelSteps = 20;
    public const int MaxWaterfallBars = 30;

    /// <summary>The visible output columns of a definition, keyed by <see cref="QueryColumn.EffectiveAlias"/>.
    /// Counts, sums and averages are always numeric; Min/Max keep the source column's type.</summary>
    public static IReadOnlyList<OutputColumn> OutputColumns(QueryDefinition definition) =>
        definition.Columns
            .Where(c => c.IsVisible)
            .Select(c => new OutputColumn(c.EffectiveAlias, c.Aggregate switch
            {
                AggregateFunction.Count or AggregateFunction.CountDistinct or AggregateFunction.Sum or AggregateFunction.Avg
                    => ColumnDataType.Number,
                _ => c.DataType
            }))
            .ToList();

    /// <summary>Output columns the options point at, in a stable order. It drives both the stale-column
    /// check and which columns a render may return, so every role must be listed here.</summary>
    public static IReadOnlyList<string> ReferencedColumns(VisualizationType type, VisualizationOptions options)
    {
        var names = new List<string>();
        switch (type)
        {
            case VisualizationType.Counter:
                if (options.Counter is { } counter)
                {
                    names.Add(counter.Value);
                    if (counter.Target is not null) names.Add(counter.Target);
                }
                break;
            case VisualizationType.Table:
                if (options.Table is { } table) names.AddRange(table.Columns);
                break;
            case VisualizationType.Pivot:
                if (options.Pivot is { } pivot)
                {
                    names.AddRange(pivot.Rows);
                    if (pivot.Column is not null) names.Add(pivot.Column);
                    names.AddRange(pivot.Values);
                }
                break;
            default:
                if (options.X is not null) names.Add(options.X);
                if (options.Series is not null) names.Add(options.Series);
                names.AddRange(options.Measures);
                if (type == VisualizationType.Combo) names.AddRange(options.LineMeasures);
                if (type == VisualizationType.Scatter)
                {
                    if (options.Size is not null) names.Add(options.Size);
                    if (options.Label is not null) names.Add(options.Label);
                }
                break;
        }

        return names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<VisualizationIssue> Validate(
        VisualizationType type, VisualizationOptions options, QueryDefinition definition)
    {
        options.Normalize();
        var issues = ValidateRoles(type, options, definition).ToList();
        if (issues.Count > 0)
        {
            return issues;
        }

        // The executor fills each row last-wins by name, so a shared name would pair one column's
        // values with another column's aggregate.
        var visible = definition.Columns.Where(c => c.IsVisible).Select(c => c.EffectiveAlias).ToList();
        var ambiguous = ReferencedColumns(type, options).Where(name => visible.Count(v => v == name) > 1).ToList();
        if (ambiguous.Count > 0)
        {
            issues.Add(new VisualizationIssue(
                $"More than one column is named {string.Join(", ", ambiguous)}; give them different names."));
        }
        return issues;
    }

    private static IReadOnlyList<VisualizationIssue> ValidateRoles(
        VisualizationType type, VisualizationOptions options, QueryDefinition definition)
    {
        var issues = new List<VisualizationIssue>();

        if (options.Version != VisualizationOptions.CurrentVersion)
        {
            issues.Add(new VisualizationIssue($"Unsupported chart options version {options.Version}."));
            return issues;
        }

        void RejectBlank(IEnumerable<string?> names, string role)
        {
            if (names.Any(string.IsNullOrWhiteSpace))
            {
                issues.Add(new VisualizationIssue($"A {role} column name cannot be blank."));
            }
        }

        var output = OutputColumns(definition)
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        OutputColumn? Resolve(string? name, string role)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            if (output.TryGetValue(name, out var column))
            {
                return column;
            }
            issues.Add(new VisualizationIssue($"'{name}' is not a visible column of this query ({role}).", name));
            return null;
        }

        void RequireNumber(string? name, string role)
        {
            if (Resolve(name, role) is { } column && column.DataType != ColumnDataType.Number)
            {
                issues.Add(new VisualizationIssue($"'{column.Name}' is not a number column."));
            }
        }

        void RequireNumberOrDate(string? name, string role)
        {
            if (Resolve(name, role) is { } column
                && column.DataType is not (ColumnDataType.Number or ColumnDataType.Date or ColumnDataType.DateTime))
            {
                issues.Add(new VisualizationIssue($"'{column.Name}' is not a number or date column."));
            }
        }

        if (options.Format is { } format)
        {
            if (format.Decimals is < 0 or > 4)
            {
                issues.Add(new VisualizationIssue("Number format decimals must be between 0 and 4."));
            }
            if (format.Style == ValueFormatStyle.Currency
                && (format.Currency is not { Length: 3 } code || !code.All(c => c is >= 'A' and <= 'Z')))
            {
                issues.Add(new VisualizationIssue("A currency format needs a three-letter code such as USD."));
            }
        }

        if (type == VisualizationType.Counter)
        {
            if (options.Counter is null || string.IsNullOrWhiteSpace(options.Counter.Value))
            {
                issues.Add(new VisualizationIssue("A counter needs a value column."));
                return issues;
            }
            if (options.Counter.Decimals is < 0 or > 4)
            {
                issues.Add(new VisualizationIssue("Counter decimals must be between 0 and 4."));
            }
            RequireNumber(options.Counter.Value, "value");
            RequireNumber(options.Counter.Target, "target");
            return issues;
        }

        if (type == VisualizationType.Table)
        {
            var columns = options.Table?.Columns ?? [];
            if (columns.Count is < 1 or > MaxTableColumns)
            {
                issues.Add(new VisualizationIssue($"Choose between 1 and {MaxTableColumns} table columns."));
            }
            RejectBlank(columns, "table");
            foreach (var column in columns)
            {
                Resolve(column, "table column");
            }
            if (columns.Distinct(StringComparer.Ordinal).Count() != columns.Count)
            {
                issues.Add(new VisualizationIssue("A table column can only be used once."));
            }
            return issues;
        }

        if (type == VisualizationType.Pivot)
        {
            var pivot = options.Pivot ?? new PivotOptions();
            if (pivot.Rows.Count is < 1 or > MaxPivotRows)
            {
                issues.Add(new VisualizationIssue($"Choose between 1 and {MaxPivotRows} row columns."));
            }
            if (pivot.Values.Count is < 1 or > MaxPivotValues)
            {
                issues.Add(new VisualizationIssue($"Choose between 1 and {MaxPivotValues} value columns."));
            }
            RejectBlank(pivot.Rows, "pivot row");
            RejectBlank(pivot.Values, "pivot value");
            foreach (var row in pivot.Rows)
            {
                Resolve(row, "row");
            }
            Resolve(pivot.Column, "column");
            foreach (var value in pivot.Values)
            {
                RequireNumber(value, "value");
            }

            var roles = pivot.Rows.Concat(pivot.Values).Concat(pivot.Column is null ? [] : [pivot.Column]).ToList();
            if (roles.Distinct(StringComparer.Ordinal).Count() != roles.Count)
            {
                issues.Add(new VisualizationIssue("A column cannot be used in two pivot roles."));
            }
            return issues;
        }

        if (string.IsNullOrEmpty(options.X))
        {
            issues.Add(new VisualizationIssue("Choose a category (X) column."));
        }
        else if (type is VisualizationType.Scatter or VisualizationType.Trend)
        {
            RequireNumberOrDate(options.X, "category");
        }
        else
        {
            Resolve(options.X, "category");
        }

        if (options.Measures.Count is < 1 or > VisualizationOptions.MaxMeasures)
        {
            issues.Add(new VisualizationIssue($"Choose between 1 and {VisualizationOptions.MaxMeasures} measure columns."));
        }
        RejectBlank(options.Measures, "measure");
        foreach (var measure in options.Measures)
        {
            RequireNumber(measure, "measure");
        }

        if (options.Measures.Distinct(StringComparer.Ordinal).Count() != options.Measures.Count)
        {
            issues.Add(new VisualizationIssue("A measure column can only be used once."));
        }

        if (!string.IsNullOrEmpty(options.Series))
        {
            if (type == VisualizationType.Pie)
            {
                issues.Add(new VisualizationIssue("A pie chart cannot have a series column."));
            }
            else
            {
                Resolve(options.Series, "series");
                if (options.Measures.Count != 1)
                {
                    issues.Add(new VisualizationIssue("A chart with a series column needs exactly one measure."));
                }
            }
        }

        if (type == VisualizationType.Pie && options.Measures.Count != 1)
        {
            issues.Add(new VisualizationIssue("A pie chart needs exactly one measure."));
        }

        if (type == VisualizationType.Scatter && options.Measures.Count != 1)
        {
            issues.Add(new VisualizationIssue("A scatter chart needs exactly one measure."));
        }

        if (type is VisualizationType.Funnel or VisualizationType.Waterfall or VisualizationType.Trend
            && (options.Measures.Count != 1 || !string.IsNullOrEmpty(options.Series)))
        {
            issues.Add(new VisualizationIssue("This chart needs exactly one measure and no series column."));
        }

        if (options.Stacked && type is not (VisualizationType.Bar or VisualizationType.HorizontalBar or VisualizationType.Area))
        {
            issues.Add(new VisualizationIssue("Only bar charts can be stacked."));
        }

        if (options.Percent && !options.Stacked)
        {
            issues.Add(new VisualizationIssue("A 100% chart must also be stacked."));
        }

        if (type == VisualizationType.Combo)
        {
            if (!string.IsNullOrEmpty(options.Series))
            {
                issues.Add(new VisualizationIssue("A combo chart cannot have a series column."));
            }
            var lines = options.LineMeasures.Distinct(StringComparer.Ordinal).ToList();
            if (lines.Any(l => !options.Measures.Contains(l, StringComparer.Ordinal)))
            {
                issues.Add(new VisualizationIssue("Every line measure must also be one of the measures."));
            }
            else if (lines.Count < 1 || lines.Count >= options.Measures.Count)
            {
                issues.Add(new VisualizationIssue("A combo chart needs at least one line and one bar measure."));
            }
        }

        if (type == VisualizationType.Scatter)
        {
            RequireNumber(options.Size, "size");
            Resolve(options.Label, "label");
        }

        return issues;
    }
}
