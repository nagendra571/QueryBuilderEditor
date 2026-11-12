using System.Text.Json;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Parameters;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Dashboards;

/// <summary>Reads and writes the two JSON columns. A corrupt column reads as "nothing configured"
/// rather than breaking the whole dashboard.</summary>
public static class DashboardParameterStore
{
    public static List<DashboardParameterDto> ReadParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<DashboardParameterDto>>(json, QueryDefinitionSerializer.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? WriteParameters(IReadOnlyCollection<DashboardParameterDto> parameters) =>
        parameters.Count == 0 ? null : JsonSerializer.Serialize(parameters, QueryDefinitionSerializer.Options);

    /// <summary>Null when the tile has no stored mappings.</summary>
    public static Dictionary<string, ParameterMappingDto>? ReadMappings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, ParameterMappingDto>>(json, QueryDefinitionSerializer.Options);
            return map is { Count: > 0 } ? map : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? WriteMappings(IReadOnlyDictionary<string, ParameterMappingDto>? mappings) =>
        mappings is null or { Count: 0 } ? null : JsonSerializer.Serialize(mappings, QueryDefinitionSerializer.Options);
}

/// <summary>Checks dashboard parameters and tile mappings on save. Pure — no database access; whether
/// the saver may use a parameter's source query, and fixed-value parsing, are the save handler's job.</summary>
public static class DashboardParameterValidator
{
    public const int MaxParameters = 20;
    public const int MaxValueLength = 4_000;

    /// <summary>Returns the cleaned list (trimmed names, titles defaulted); throws naming the first problem.</summary>
    public static List<DashboardParameterDto> NormalizeParameters(IReadOnlyList<DashboardParameterDto> parameters)
    {
        if (parameters.Count > MaxParameters)
        {
            throw new CatalogValidationException($"A dashboard can have at most {MaxParameters} parameters.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DashboardParameterDto>(parameters.Count);
        foreach (var raw in parameters)
        {
            var name = raw.Name?.Trim();
            if (!QueryParameterNames.IsValid(name))
            {
                throw new CatalogValidationException($"'{raw.Name}' is not a valid dashboard parameter name (letters, digits and underscore).");
            }
            if (!seen.Add(name!))
            {
                throw new CatalogValidationException($"Dashboard parameter '{name}' is declared more than once.");
            }
            if (!Enum.IsDefined(raw.Type))
            {
                throw new CatalogValidationException($"{name}: unknown parameter type.");
            }

            var title = string.IsNullOrWhiteSpace(raw.Title) ? name! : raw.Title.Trim();
            if (title.Length > ParameterValidator.MaxLabelLength)
            {
                throw new CatalogValidationException($"{title[..40]}...: the title is too long (max {ParameterValidator.MaxLabelLength} characters).");
            }

            var options = (raw.Options ?? []).ToList();
            if (raw.Type == ParameterType.List)
            {
                if (options.Count is 0 or > ParameterValidator.MaxOptions
                    || options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > ParameterValidator.MaxOptionLength))
                {
                    throw new CatalogValidationException(
                        $"{title}: a list needs 1 to {ParameterValidator.MaxOptions} options of at most {ParameterValidator.MaxOptionLength} characters.");
                }
            }
            else
            {
                options = [];
            }

            if (raw.Type == ParameterType.QueryList)
            {
                if (raw.OptionsQueryId is null || raw.OptionsQueryId == Guid.Empty)
                {
                    throw new CatalogValidationException($"{title}: choose the saved query that supplies the choices.");
                }
                if (raw.OptionsLabelColumn?.Length > 128 || raw.OptionsValueColumn?.Length > 128)
                {
                    throw new CatalogValidationException($"{title}: the label/value column name is too long.");
                }
            }

            if (raw.AllowMultiple && !ParameterTypes.IsList(raw.Type))
            {
                throw new CatalogValidationException($"{title}: only list parameters can allow several values.");
            }

            var value = string.IsNullOrWhiteSpace(raw.Value) ? null : raw.Value.Trim();
            if (value is { Length: > MaxValueLength })
            {
                throw new CatalogValidationException($"{title}: the value is too long.");
            }
            if (value is not null && raw.Type == ParameterType.List
                && !ListItems(value).All(i => options.Contains(i, StringComparer.Ordinal)))
            {
                throw new CatalogValidationException($"{title}: the value is not one of the options.");
            }

            var isQueryList = raw.Type == ParameterType.QueryList;
            result.Add(new DashboardParameterDto(
                name!, title, raw.Type, options,
                isQueryList ? raw.OptionsQueryId : null,
                isQueryList ? NullIfBlank(raw.OptionsLabelColumn) : null,
                isQueryList ? NullIfBlank(raw.OptionsValueColumn) : null,
                raw.AllowMultiple, value));
        }

        return result;
    }

    /// <summary>A dashboard parameter can feed a query parameter of the same kind of value. Several
    /// values may only go to a query parameter that accepts several.</summary>
    public static bool AreCompatible(DashboardParameterDto source, QueryParameter target)
    {
        var targetType = ParameterTypes.Effective(target);
        if (source.AllowMultiple && !(target.AllowMultiple && ParameterTypes.IsList(targetType)))
        {
            return false;
        }
        return Family(source.Type) == Family(targetType);
    }

    private static int Family(ParameterType type) => type switch
    {
        ParameterType.Text => 0,
        ParameterType.Number => 1,
        ParameterType.Date or ParameterType.DateTime => 2,
        ParameterType.DateRange or ParameterType.DateTimeRange => 3,
        ParameterType.List or ParameterType.QueryList => 4,
        ParameterType.Boolean => 5,
        _ => -1
    };

    /// <summary>Cleans one tile's mappings against its query's declared parameters. <c>Default</c>
    /// entries are dropped (absence means default). Fixed values still need the binder (done by the caller).</summary>
    public static Dictionary<string, ParameterMappingDto> NormalizeMappings(
        IReadOnlyDictionary<string, ParameterMappingDto> incoming,
        IReadOnlyList<QueryParameter> queryParameters,
        IReadOnlyList<DashboardParameterDto> dashboardParameters)
    {
        if (incoming.Count > ParameterValidator.MaxParameters * 2 + 10)
        {
            throw new CatalogValidationException("A tile has too many parameter mappings.");
        }

        var result = new Dictionary<string, ParameterMappingDto>(StringComparer.Ordinal);
        foreach (var (name, mapping) in incoming)
        {
            var parameter = queryParameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
                ?? throw new CatalogValidationException($"The chart's query has no parameter '{Short(name)}'.");
            var title = ParameterTypes.Title(parameter);

            if (!Enum.IsDefined(mapping.Source))
            {
                throw new CatalogValidationException($"{title}: unknown value source.");
            }

            switch (mapping.Source)
            {
                case ParameterMappingSource.Default:
                    continue;

                case ParameterMappingSource.Dashboard:
                {
                    var source = dashboardParameters.FirstOrDefault(d =>
                            string.Equals(d.Name, mapping.DashboardParameter, StringComparison.OrdinalIgnoreCase))
                        ?? throw new CatalogValidationException($"{title}: the dashboard parameter '{Short(mapping.DashboardParameter ?? string.Empty)}' does not exist.");
                    if (!AreCompatible(source, parameter))
                    {
                        throw new CatalogValidationException($"{title}: dashboard parameter '{source.Title}' has an incompatible type.");
                    }
                    result[name] = new ParameterMappingDto(ParameterMappingSource.Dashboard, source.Name, null, null);
                    break;
                }

                case ParameterMappingSource.Tile:
                {
                    var label = string.IsNullOrWhiteSpace(mapping.Title) ? null : mapping.Title.Trim();
                    if (label is { Length: > ParameterValidator.MaxLabelLength })
                    {
                        throw new CatalogValidationException($"{title}: the label is too long.");
                    }
                    result[name] = new ParameterMappingDto(ParameterMappingSource.Tile, null, null, label);
                    break;
                }

                case ParameterMappingSource.Fixed:
                {
                    if (string.IsNullOrWhiteSpace(mapping.Value))
                    {
                        throw new CatalogValidationException($"{title}: a fixed value can't be empty.");
                    }
                    if (mapping.Value.Length > MaxValueLength)
                    {
                        throw new CatalogValidationException($"{title}: the fixed value is too long.");
                    }
                    result[name] = new ParameterMappingDto(ParameterMappingSource.Fixed, null, mapping.Value.Trim(), null);
                    break;
                }
            }
        }

        return result;
    }

    internal static List<string> ListItems(string value)
    {
        if (value.StartsWith('['))
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<string>>(value);
                if (items is not null)
                {
                    return items;
                }
            }
            catch (JsonException)
            {
            }
        }
        return [value];
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Short(string value) => value.Length <= 50 ? value : value[..50] + "...";
}
