using System.Globalization;
using System.Text.Json;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Parameters;

/// <summary>
/// Validates run-time parameter values and writes them into a copy of the definition as the literal
/// formats <c>SqlServerQuerySqlBuilder</c> already understands (scalar, JSON array for Between/IN).
/// Nothing here produces SQL text: the builder binds each literal as a SqlParameter, which is what
/// makes every parameter type safe against injection. List and query-list values are checked against
/// the allowed options — the latter recomputed here for the current caller, never taken from the browser.
/// </summary>
public sealed class ParameterBinder(IParameterOptionsService optionsService, TimeProvider clock) : IParameterBinder
{
    public const int MaxMultipleValues = 1_000;
    private const int MaxTextLength = 4_000;

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"
    ];

    private sealed class ParameterValueException(string message) : Exception(message);

    public async Task<QueryDefinition> MaterializeAsync(
        QueryDefinition definition, IReadOnlyDictionary<string, string?>? values,
        CancellationToken cancellationToken, bool allowMissing = false)
    {
        values ??= new Dictionary<string, string?>();
        var copy = QueryDefinitionSerializer.Deserialize(QueryDefinitionSerializer.Serialize(definition));
        var conditions = ParameterValidator.Conditions(copy).Where(c => c.IsParameterized).ToList();

        var declared = new Dictionary<string, QueryParameter>(StringComparer.Ordinal);
        foreach (var parameter in copy.Parameters)
        {
            declared.TryAdd(parameter.Name, parameter);
        }

        var errors = new List<string>();
        void AddError(string message)
        {
            if (!errors.Contains(message))
            {
                errors.Add(message);
            }
        }

        var known = new HashSet<string>(declared.Keys, StringComparer.Ordinal);
        known.UnionWith(conditions.Where(c => c.ParameterName is not null).Select(c => c.ParameterName!));
        foreach (var key in values.Keys.Where(k => !known.Contains(k)).Order(StringComparer.Ordinal))
        {
            AddError($"Unknown parameter '{Short(key)}'.");
        }

        var dropped = new HashSet<FilterCondition>();
        foreach (var condition in conditions)
        {
            if (ParameterTypes.TakesNoValue(condition.Operator))
            {
                condition.IsParameterized = false;
                continue;
            }

            var parameter = declared.GetValueOrDefault(condition.ParameterName ?? string.Empty)
                ?? new QueryParameter { Name = condition.ParameterName ?? string.Empty, DataType = condition.DataType };
            var title = ParameterTypes.Title(parameter);

            var raw = Pick(values, parameter);
            if (raw is null)
            {
                if (allowMissing)
                {
                    continue;
                }
                if (parameter.IsRequired)
                {
                    AddError($"{title} is required.");
                }
                else
                {
                    dropped.Add(condition);
                }
                continue;
            }

            try
            {
                condition.Value = await BindAsync(condition, parameter, raw, cancellationToken);
                condition.IsParameterized = false;
            }
            catch (ParameterValueException ex)
            {
                AddError($"{title}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            throw new CatalogValidationException(string.Join(" ", errors));
        }

        if (dropped.Count > 0)
        {
            Prune(copy.Filters, dropped);
            if (copy.Having is not null)
            {
                Prune(copy.Having, dropped);
                if (IsEmpty(copy.Having))
                {
                    copy.Having = null;
                }
            }
        }

        return copy;
    }

    private static string? Pick(IReadOnlyDictionary<string, string?> values, QueryParameter parameter)
    {
        if (values.TryGetValue(parameter.Name, out var supplied) && !IsBlank(supplied))
        {
            return supplied!.Trim();
        }
        return IsBlank(parameter.DefaultValue) ? null : parameter.DefaultValue!.Trim();
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim() == "[]";

    private async Task<string> BindAsync(FilterCondition condition, QueryParameter parameter, string raw, CancellationToken ct)
    {
        var type = ParameterTypes.Effective(parameter);
        var column = condition.DataType;

        if (condition.Operator == FilterOperator.Between)
        {
            if (!ParameterTypes.IsRange(type))
            {
                throw new ParameterValueException("is not a date range, so it can't be used on a 'between' filter.");
            }
            var (lo, hi) = ParseRange(raw, type, column);
            return JsonSerializer.Serialize(new[] { lo, hi });
        }

        if (ParameterTypes.IsRange(type))
        {
            throw new ParameterValueException("is a date range and can only be used on a 'between' filter.");
        }

        var items = ParseMany(raw, type);
        var multipleAllowed = parameter.AllowMultiple && ParameterTypes.IsList(type);
        if (items.Count > 1 && !(multipleAllowed && condition.Operator is FilterOperator.In or FilterOperator.NotIn))
        {
            throw new ParameterValueException("accepts a single value.");
        }
        if (items.Count > MaxMultipleValues)
        {
            throw new ParameterValueException($"accepts at most {MaxMultipleValues:N0} values.");
        }

        var canonical = new List<string>(items.Count);
        foreach (var item in items)
        {
            canonical.Add(await ParseOneAsync(item, parameter, type, column, ct));
        }

        return condition.Operator is FilterOperator.In or FilterOperator.NotIn
            ? JsonSerializer.Serialize(canonical)
            : canonical[0];
    }

    private static List<string> ParseMany(string raw, ParameterType type)
    {
        if (ParameterTypes.IsList(type) && raw.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var items = new List<string>();
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        items.Add(element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText());
                    }
                    return items.Count == 0 ? [raw] : items;
                }
            }
            catch (JsonException)
            {
            }
        }
        return [raw];
    }

    private async Task<string> ParseOneAsync(string raw, QueryParameter parameter, ParameterType type, ColumnDataType column, CancellationToken ct)
    {
        switch (type)
        {
            case ParameterType.Number:
                return FormatNumber(raw);
            case ParameterType.Boolean:
                return FormatBoolean(raw);
            case ParameterType.Date:
                return FormatDate(ParseDate(raw));
            case ParameterType.DateTime:
                return FormatDateTime(ParseDateTime(raw));
            case ParameterType.List:
                RequireOption(parameter.Options.Contains(raw, StringComparer.Ordinal), raw);
                return Coerce(raw, column);
            case ParameterType.QueryList:
            {
                var options = await optionsService.GetOptionsAsync(parameter, ct);
                RequireOption(options.Any(o => string.Equals(o.Value, raw, StringComparison.Ordinal)), raw);
                return Coerce(raw, column);
            }
            default:
                if (raw.Length > MaxTextLength)
                {
                    throw new ParameterValueException($"is too long (max {MaxTextLength:N0} characters).");
                }
                return Coerce(raw, column);
        }
    }

    private static void RequireOption(bool isMember, string raw)
    {
        if (!isMember)
        {
            throw new ParameterValueException($"'{Short(raw)}' is not one of the allowed values.");
        }
    }

    /// <summary>An allowed option is text; the column it filters decides what else it must be valid as.</summary>
    private string Coerce(string raw, ColumnDataType column) => column switch
    {
        ColumnDataType.Number => FormatNumber(raw),
        ColumnDataType.Boolean => FormatBoolean(raw),
        ColumnDataType.Date => FormatDate(ParseDate(raw)),
        ColumnDataType.DateTime => FormatDateTime(ParseDateTime(raw)),
        ColumnDataType.Guid when !Guid.TryParse(raw, out _) => throw new ParameterValueException($"'{Short(raw)}' is not a valid identifier."),
        _ => raw
    };

    private static string FormatNumber(string raw) =>
        decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : throw new ParameterValueException($"'{Short(raw)}' is not a number.");

    private static string FormatBoolean(string raw) => raw.ToLowerInvariant() switch
    {
        "true" or "1" => "true",
        "false" or "0" => "false",
        _ => throw new ParameterValueException($"'{Short(raw)}' is not true or false.")
    };

    private DateTime Now => clock.GetLocalNow().DateTime;

    private DateTime ParseDate(string raw)
    {
        if (RelativeDates.TryResolveSingle(raw, Now, includeTime: false, out var resolved))
        {
            return resolved;
        }
        return TryParseIso(raw, out var parsed)
            ? parsed.Date
            : throw new ParameterValueException($"'{Short(raw)}' is not a date (use yyyy-MM-dd, today or yesterday).");
    }

    private DateTime ParseDateTime(string raw)
    {
        if (RelativeDates.TryResolveSingle(raw, Now, includeTime: true, out var resolved))
        {
            return resolved;
        }
        return TryParseIso(raw, out var parsed)
            ? parsed
            : throw new ParameterValueException($"'{Short(raw)}' is not a date and time (use yyyy-MM-ddTHH:mm).");
    }

    private static bool TryParseIso(string raw, out DateTime value) =>
        DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    private static string FormatDate(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTime value) =>
        value.ToString(value.Millisecond == 0 ? "yyyy-MM-ddTHH:mm:ss" : "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>"start~end" or one relative token. A day-granular range over a date-time column ends at
    /// the last moment of its end day, so the end date itself is included.</summary>
    private (string Lo, string Hi) ParseRange(string raw, ParameterType type, ColumnDataType column)
    {
        var parts = raw.Split('~');
        DateTime start, end;

        if (parts.Length == 1)
        {
            if (!RelativeDates.TryResolveRange(parts[0], Now, out start, out end))
            {
                throw new ParameterValueException(
                    "needs a start and end (start~end) or a relative range such as last_30_days.");
            }
            end = EndOfDay(end);
        }
        else if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
        {
            if (type == ParameterType.DateRange)
            {
                start = ParseDate(parts[0]);
                end = ParseDate(parts[1]);
                end = EndOfDay(end);
            }
            else
            {
                start = ParseDateTime(parts[0]);
                end = ParseDateTime(parts[1]);
            }
        }
        else
        {
            throw new ParameterValueException("needs a start and end (start~end).");
        }

        if (start > end)
        {
            throw new ParameterValueException("start must be on or before end.");
        }

        return column == ColumnDataType.Date
            ? (FormatDate(start), FormatDate(end))
            : (FormatDateTime(start), FormatDateTime(end));
    }

    private static DateTime EndOfDay(DateTime day) => day.Date.AddDays(1).AddMilliseconds(-3);

    private static void Prune(FilterGroup group, HashSet<FilterCondition> dropped)
    {
        group.Conditions.RemoveAll(dropped.Contains);
        foreach (var nested in group.Groups)
        {
            Prune(nested, dropped);
        }
        group.Groups.RemoveAll(IsEmpty);
    }

    private static bool IsEmpty(FilterGroup group) => group.Conditions.Count == 0 && group.Groups.Count == 0;

    private static string Short(string value) => value.Length <= 50 ? value : value[..50] + "...";
}
