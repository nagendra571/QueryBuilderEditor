namespace QueryBuilder.Domain.Model;

/// <summary>One click in a drill trail. <paramref name="Column"/> is an output column name of the saved
/// query (or a drill column); <paramref name="Value"/> null means the clicked mark had no value.</summary>
public sealed record DrillStep(string Column, string? Value);

public enum DrillAction
{
    Rows,
    Breakdown
}
