using QueryBuilder.Application.Common;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Parameters;

/// <summary>Checks a definition's parameter declarations when a query is saved (no database access;
/// whether the saver may use a query-list's source query is checked by the save handler).</summary>
public static class ParameterValidator
{
    public const int MaxParameters = 50;
    public const int MaxOptions = 500;
    public const int MaxOptionLength = 200;
    public const int MaxLabelLength = 200;

    public static IReadOnlyList<string> Validate(QueryDefinition definition)
    {
        var errors = new List<string>();

        if (definition.Parameters.Count > MaxParameters)
        {
            errors.Add($"A query can have at most {MaxParameters} parameters.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conditions = Conditions(definition).Where(c => c.IsParameterized).ToList();

        foreach (var parameter in definition.Parameters)
        {
            var title = ParameterTypes.Title(parameter);
            if (!QueryParameterNames.IsValid(parameter.Name))
            {
                errors.Add($"'{parameter.Name}' is not a valid parameter name.");
                continue;
            }
            if (!seen.Add(parameter.Name))
            {
                errors.Add($"Parameter '{parameter.Name}' is declared more than once.");
                continue;
            }
            if (parameter.Label?.Length > MaxLabelLength)
            {
                errors.Add($"{title}: the title is too long (max {MaxLabelLength} characters).");
            }

            var type = ParameterTypes.Effective(parameter);
            var bound = conditions.Where(c => string.Equals(c.ParameterName, parameter.Name, StringComparison.Ordinal)).ToList();

            foreach (var condition in bound)
            {
                if (!ParameterTypes.AllowedFor(condition.DataType, condition.Operator).Contains(type))
                {
                    errors.Add($"{title}: a {Describe(type)} parameter can't be used on the filter '{condition.ColumnName}' with this condition.");
                }
            }

            if (type == ParameterType.List)
            {
                ValidateOptions(parameter, title, errors);
            }
            else if (type == ParameterType.QueryList)
            {
                if (parameter.OptionsQueryId is null || parameter.OptionsQueryId == Guid.Empty)
                {
                    errors.Add($"{title}: choose the saved query that supplies the choices.");
                }
                if (parameter.OptionsLabelColumn?.Length > 128 || parameter.OptionsValueColumn?.Length > 128)
                {
                    errors.Add($"{title}: the label/value column name is too long.");
                }
            }

            if (parameter.AllowMultiple)
            {
                if (!ParameterTypes.IsList(type))
                {
                    errors.Add($"{title}: only list parameters can allow several values.");
                }
                else if (bound.Any(c => c.Operator is not (FilterOperator.In or FilterOperator.NotIn)))
                {
                    errors.Add($"{title}: several values are only allowed on an 'is one of' or 'is not one of' filter.");
                }
            }
        }

        return errors;
    }

    private static void ValidateOptions(QueryParameter parameter, string title, List<string> errors)
    {
        if (parameter.Options.Count == 0)
        {
            errors.Add($"{title}: add at least one option.");
            return;
        }
        if (parameter.Options.Count > MaxOptions)
        {
            errors.Add($"{title}: at most {MaxOptions} options are allowed.");
        }
        if (parameter.Options.Any(o => string.IsNullOrWhiteSpace(o)))
        {
            errors.Add($"{title}: options can't be blank.");
        }
        if (parameter.Options.Any(o => o.Length > MaxOptionLength))
        {
            errors.Add($"{title}: each option can be at most {MaxOptionLength} characters.");
        }
    }

    private static string Describe(ParameterType type) => type switch
    {
        ParameterType.DateRange => "date range",
        ParameterType.DateTimeRange => "date-time range",
        ParameterType.QueryList => "query-based list",
        ParameterType.DateTime => "date-time",
        _ => type.ToString().ToLowerInvariant()
    };

    internal static IEnumerable<FilterCondition> Conditions(QueryDefinition definition) =>
        definition.Filters.AllConditions().Concat(definition.Having?.AllConditions() ?? []);
}
