using System.Globalization;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Visualizations;

/// <summary>Limits that depend on the data itself, so they can only be checked once rows are back.</summary>
public static class VisualizationDataChecks
{
    private const string BlankLabel = "(blank)";

    public static void Check(VisualizationType type, VisualizationOptions options, List<Dictionary<string, object?>> rows)
    {
        switch (type)
        {
            case VisualizationType.Pie:
                CheckPie(rows, options);
                break;
            case VisualizationType.Pivot:
                CheckPivot(rows, options);
                break;
            case VisualizationType.Funnel:
                CheckFunnel(rows, options);
                break;
            case VisualizationType.Waterfall:
                if (rows.Count > VisualizationOptionsValidator.MaxWaterfallBars)
                {
                    throw new CatalogValidationException(
                        $"Too many bars for a waterfall (max {VisualizationOptionsValidator.MaxWaterfallBars}); add filters or totals.");
                }
                break;
            case VisualizationType.Scatter:
                if (!string.IsNullOrEmpty(options.Size) && AnyNegative(rows, options.Size))
                {
                    throw new CatalogValidationException("Bubble size cannot be negative.");
                }
                break;
        }
    }

    private static void CheckPie(List<Dictionary<string, object?>> rows, VisualizationOptions options)
    {
        var categories = rows
            .Select(r => r[options.X!]?.ToString() ?? BlankLabel)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (categories > VisualizationOptionsValidator.MaxPieSlices)
        {
            throw new CatalogValidationException(
                $"Too many slices for a pie chart (max {VisualizationOptionsValidator.MaxPieSlices}); use Bar.");
        }

        if (AnyNegative(rows, options.Measures[0]))
        {
            throw new CatalogValidationException("A pie chart cannot show negative values; use Bar.");
        }
    }

    private static void CheckPivot(List<Dictionary<string, object?>> rows, VisualizationOptions options)
    {
        var column = options.Pivot?.Column;
        if (string.IsNullOrEmpty(column))
        {
            return;
        }

        var distinct = rows
            .Select(r => r[column]?.ToString() ?? BlankLabel)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinct > VisualizationOptionsValidator.MaxPivotColumns)
        {
            throw new CatalogValidationException(
                $"Too many columns for a pivot table (max {VisualizationOptionsValidator.MaxPivotColumns}); add filters or choose a different column.");
        }
    }

    private static void CheckFunnel(List<Dictionary<string, object?>> rows, VisualizationOptions options)
    {
        if (rows.Count > VisualizationOptionsValidator.MaxFunnelSteps)
        {
            throw new CatalogValidationException(
                $"Too many steps for a funnel (max {VisualizationOptionsValidator.MaxFunnelSteps}).");
        }

        if (AnyNegative(rows, options.Measures[0]))
        {
            throw new CatalogValidationException("A funnel cannot show negative values.");
        }
    }

    private static bool AnyNegative(List<Dictionary<string, object?>> rows, string column) =>
        rows.Any(r => r[column] is { } value && TryToDecimal(value) is < 0);

    private static decimal? TryToDecimal(object value)
    {
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
