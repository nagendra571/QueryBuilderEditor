using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Parameters;

public sealed record ParameterOption(string Label, string Value);

public static class ParameterTypes
{
    /// <summary>Definitions saved before parameter types existed carry only a column data type.</summary>
    public static ParameterType Effective(QueryParameter parameter) => parameter.Type ?? parameter.DataType switch
    {
        ColumnDataType.Number => ParameterType.Number,
        ColumnDataType.Date => ParameterType.Date,
        ColumnDataType.DateTime => ParameterType.DateTime,
        ColumnDataType.Boolean => ParameterType.Boolean,
        _ => ParameterType.Text
    };

    public static bool IsRange(ParameterType type) => type is ParameterType.DateRange or ParameterType.DateTimeRange;
    public static bool IsList(ParameterType type) => type is ParameterType.List or ParameterType.QueryList;

    public static string Title(QueryParameter parameter) =>
        string.IsNullOrWhiteSpace(parameter.Label) ? parameter.Name : parameter.Label;

    /// <summary>Operators that need no value at all, so a run-time parameter on them is meaningless.</summary>
    public static bool TakesNoValue(FilterOperator op) =>
        op is FilterOperator.IsNull or FilterOperator.IsNotNull or FilterOperator.IsTrue or FilterOperator.IsFalse;

    /// <summary>The types a filter on this column with this operator can be asked as at run time.</summary>
    public static IReadOnlyList<ParameterType> AllowedFor(ColumnDataType column, FilterOperator op)
    {
        if (TakesNoValue(op))
        {
            return [];
        }
        if (op == FilterOperator.Between)
        {
            return column is ColumnDataType.Date or ColumnDataType.DateTime
                ? [ParameterType.DateRange, ParameterType.DateTimeRange]
                : [];
        }
        if (op is FilterOperator.In or FilterOperator.NotIn)
        {
            return column == ColumnDataType.Boolean ? [] : [ParameterType.List, ParameterType.QueryList];
        }

        return column switch
        {
            ColumnDataType.Number => [ParameterType.Number, ParameterType.List, ParameterType.QueryList],
            ColumnDataType.Date => [ParameterType.Date, ParameterType.List, ParameterType.QueryList],
            ColumnDataType.DateTime => [ParameterType.Date, ParameterType.DateTime, ParameterType.List, ParameterType.QueryList],
            ColumnDataType.Boolean => [ParameterType.Boolean],
            _ => [ParameterType.Text, ParameterType.List, ParameterType.QueryList]
        };
    }
}
