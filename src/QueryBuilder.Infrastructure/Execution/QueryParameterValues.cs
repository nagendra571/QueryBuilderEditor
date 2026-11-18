using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Execution;

/// <summary>Turns a generated parameter into the CLR value both providers bind, so the two execution services can't drift apart.</summary>
public static class QueryParameterValues
{
    public static object Resolve(GeneratedQueryParameter parameter, IReadOnlyDictionary<string, string?> runtimeValues) =>
        Resolve(parameter, runtimeValues, wholeNumbersAsLong: false);

    /// <summary>Postgres binds a whole number that fits in <see cref="long"/> as an integer, because a <c>numeric</c>
    /// parameter makes the planner cast an integer column and give up its index. Anything else stays decimal.</summary>
    public static object ResolveForPostgres(GeneratedQueryParameter parameter, IReadOnlyDictionary<string, string?> runtimeValues) =>
        Resolve(parameter, runtimeValues, wholeNumbersAsLong: true);

    private static object Resolve(
        GeneratedQueryParameter parameter, IReadOnlyDictionary<string, string?> runtimeValues, bool wholeNumbersAsLong)
    {
        string? rawValue = parameter.LiteralValue;
        if (parameter.IsRuntimeParameter && parameter.RuntimeParameterName is not null &&
            runtimeValues.TryGetValue(parameter.RuntimeParameterName, out var supplied))
        {
            rawValue = supplied;
        }

        return ConvertValue(parameter.DataType, rawValue, wholeNumbersAsLong);
    }

    private static object ConvertValue(ColumnDataType dataType, string? rawValue, bool wholeNumbersAsLong)
    {
        // An empty text value is a value, not "no value": a chart groups empty strings apart from NULL, and drilling into that
        // group must find them (= NULL would match nothing). Every other type has no empty literal.
        if (rawValue is null || (rawValue.Length == 0 && dataType != ColumnDataType.Text))
        {
            return DBNull.Value;
        }

        try
        {
            return dataType switch
            {
                ColumnDataType.Number => ParseNumber(rawValue, wholeNumbersAsLong),
                ColumnDataType.Boolean => ParseBool(rawValue),
                ColumnDataType.Date => ParseDateTime(rawValue).Date,
                ColumnDataType.DateTime => ParseDateTime(rawValue),
                ColumnDataType.Guid => Guid.Parse(rawValue),
                _ => rawValue
            };
        }
        catch (Exception ex) when (wholeNumbersAsLong && ex is FormatException or OverflowException)
        {
            throw new QueryExecutionException($"Value '{rawValue}' is not a valid {dataType}.", ex);
        }
        catch (FormatException ex)
        {
            throw new QueryExecutionException($"Value '{rawValue}' is not a valid {dataType}.", ex);
        }
    }

    private static object ParseNumber(string rawValue, bool wholeNumbersAsLong)
    {
        if (!QueryLiteral.TryParseNumber(rawValue, out var value)) throw new FormatException();
        if (wholeNumbersAsLong && value == decimal.Truncate(value) && value >= long.MinValue && value <= long.MaxValue)
        {
            return (long)value;
        }

        return value;
    }

    private static DateTime ParseDateTime(string rawValue) =>
        QueryLiteral.TryParseDateTime(rawValue, out var value) ? value : throw new FormatException();

    private static bool ParseBool(string value) => value is "1" or "true" or "True" or "TRUE";
}
