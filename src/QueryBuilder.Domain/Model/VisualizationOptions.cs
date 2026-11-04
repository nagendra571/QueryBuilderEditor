using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Domain.Model;

/// <summary>
/// How a chart maps a saved query's output columns to roles. Columns are referenced by output name
/// (<see cref="QueryColumn.EffectiveAlias"/>), the same key result rows use. Versioned so the shape
/// can evolve without breaking stored charts.
/// </summary>
public sealed class VisualizationOptions
{
    public const int CurrentVersion = 1;
    public const int MaxMeasures = 5;

    public int Version { get; set; } = CurrentVersion;
    public string? X { get; set; }
    public List<string> Measures { get; set; } = [];
    public string? Series { get; set; }
    public bool Stacked { get; set; }
    public VisualizationSort SortX { get; set; } = VisualizationSort.None;
    public CounterOptions? Counter { get; set; }
    public bool Percent { get; set; }
    public List<string> LineMeasures { get; set; } = [];
    public string? Size { get; set; }
    public string? Label { get; set; }
    public TableOptions? Table { get; set; }
    public PivotOptions? Pivot { get; set; }
    public ValueFormat? Format { get; set; }

    /// <summary>A crafted body can send <c>null</c> for any list; everything downstream assumes a list.</summary>
    public void Normalize()
    {
        Measures ??= [];
        LineMeasures ??= [];
        if (Table is not null) Table.Columns ??= [];
        if (Pivot is not null)
        {
            Pivot.Rows ??= [];
            Pivot.Values ??= [];
        }
    }
}

public sealed class TableOptions
{
    public List<string> Columns { get; set; } = [];
}

public sealed class PivotOptions
{
    public List<string> Rows { get; set; } = [];
    public string? Column { get; set; }
    public List<string> Values { get; set; } = [];
    public bool Totals { get; set; } = true;
    public bool Shade { get; set; }
}

public sealed class ValueFormat
{
    public ValueFormatStyle Style { get; set; }
    public int? Decimals { get; set; }
    public string? Currency { get; set; }
}

public sealed class CounterOptions
{
    public string Value { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int Decimals { get; set; }
    public string? Target { get; set; }
}
