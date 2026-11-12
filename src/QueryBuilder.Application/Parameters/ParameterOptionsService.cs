using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Parameters;

/// <summary>
/// Runs a query-list parameter's source query live, as the current caller, through the same
/// validate -> row-level scope -> build -> execute path as any other run. There is deliberately no
/// cache shared between users: a dropdown must never reveal a value outside the caller's data scope.
/// The per-request cache only avoids re-running the same source query within one request.
/// </summary>
public sealed class ParameterOptionsService(
    ISavedQueryRepository savedQueries,
    IDataSourceRepository dataSources,
    IDataCatalogService catalogService,
    IQuerySqlBuilderFactory sqlBuilderFactory,
    IQueryExecutionService executionService,
    IDataScopeGuard dataScopeGuard,
    RecordLimitSettings recordLimitSettings,
    ICurrentAppSettings appSettings,
    ICurrentUserService currentUser,
    Dashboards.IDashboardGrant dashboardGrant,
    IServiceProvider services) : IParameterOptionsService
{
    public const int MaxOptions = 1_000;
    private const int MaxFetchRows = 10_000;
    private const int MaxDepth = 3;

    private readonly Dictionary<string, IReadOnlyList<ParameterOption>> _cache = new(StringComparer.Ordinal);
    private readonly List<Guid> _active = [];

    public async Task<IReadOnlyList<ParameterOption>> GetOptionsAsync(QueryParameter parameter, CancellationToken cancellationToken)
    {
        if (parameter.OptionsQueryId is not { } sourceId || sourceId == Guid.Empty)
        {
            throw new CatalogValidationException($"{ParameterTypes.Title(parameter)}: no source query is chosen.");
        }

        var key = $"{sourceId}|{parameter.OptionsLabelColumn}|{parameter.OptionsValueColumn}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_active.Contains(sourceId) || _active.Count >= MaxDepth)
        {
            throw new CatalogValidationException(
                $"{ParameterTypes.Title(parameter)}: the choices come from a query that depends on itself.");
        }

        _active.Add(sourceId);
        try
        {
            var options = await LoadAsync(parameter, sourceId, cancellationToken);
            _cache[key] = options;
            return options;
        }
        finally
        {
            _active.Remove(sourceId);
        }
    }

    private async Task<IReadOnlyList<ParameterOption>> LoadAsync(QueryParameter parameter, Guid sourceId, CancellationToken ct)
    {
        // Same answer for "missing" and "not yours": a caller must not learn which queries exist.
        var source = await savedQueries.GetByIdAsync(sourceId, ct);
        if (source is null || !(SavedQueryAccess.CanView(source, AccessActor.From(currentUser)) || dashboardGrant.Covers(source.Id)))
        {
            throw new ForbiddenException("You do not have access to the query that supplies these choices.");
        }
        if (source.IsDisabled)
        {
            throw new QueryDisabledException($"'{source.Name}' is disabled, so its choices are unavailable.");
        }

        var dataSource = await dataSources.GetByIdAsync(source.DataSourceId, ct)
            ?? throw new NotFoundException(nameof(DataSource), source.DataSourceId);

        await catalogService.ValidateAsync(source.DataSourceId, QueryDefinitionSerializer.Deserialize(source.DefinitionJson), ct);

        // The source query runs with its own saved defaults.
        var binder = services.GetRequiredService<IParameterBinder>();
        var definition = await binder.MaterializeAsync(QueryDefinitionSerializer.Deserialize(source.DefinitionJson), null, ct);

        var scope = await dataScopeGuard.AuthorizeAsync(source.DataSourceId, definition, ct);
        var generated = sqlBuilderFactory.GetBuilder(dataSource.Provider).Build(definition, scope);

        var limit = RecordLimits.Effective(dataSource.MaxRecords, await appSettings.MaxRecordsAsync(ct), recordLimitSettings);
        var fetch = limit is { } l ? Math.Min(l, MaxFetchRows) : MaxFetchRows;
        var result = await executionService.ExecuteAsync(dataSource, generated, new Dictionary<string, string?>(), fetch, ct);

        var (labelColumn, valueColumn) = PickColumns(parameter, result, IdentifierMatching.Comparison(dataSource.Provider));

        var options = new List<ParameterOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in result.Rows)
        {
            var value = Format(row.GetValueOrDefault(valueColumn));
            if (value is null || !seen.Add(value))
            {
                continue;
            }
            options.Add(new ParameterOption(Format(row.GetValueOrDefault(labelColumn)) ?? value, value));
            if (options.Count >= MaxOptions)
            {
                break;
            }
        }
        return options;
    }

    private static (string Label, string Value) PickColumns(QueryParameter parameter, QueryResultSet result, StringComparison comparison)
    {
        if (result.Columns.Count == 0)
        {
            throw new CatalogValidationException($"{ParameterTypes.Title(parameter)}: the source query has no columns.");
        }

        string? Find(string? wanted) =>
            string.IsNullOrWhiteSpace(wanted) ? null : result.Columns.FirstOrDefault(c => string.Equals(c.Name, wanted, comparison))?.Name;

        var configuredLabel = Find(parameter.OptionsLabelColumn);
        var configuredValue = Find(parameter.OptionsValueColumn);
        if (!string.IsNullOrWhiteSpace(parameter.OptionsLabelColumn) && configuredLabel is null)
        {
            throw new CatalogValidationException($"{ParameterTypes.Title(parameter)}: the source query has no column '{parameter.OptionsLabelColumn}'.");
        }
        if (!string.IsNullOrWhiteSpace(parameter.OptionsValueColumn) && configuredValue is null)
        {
            throw new CatalogValidationException($"{ParameterTypes.Title(parameter)}: the source query has no column '{parameter.OptionsValueColumn}'.");
        }

        // Conventional names aren't identifiers being authorized, so a differently-cased alias still counts.
        string? FindConvention(string wanted) =>
            result.Columns.FirstOrDefault(c => c.Name == wanted)?.Name
            ?? result.Columns.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))?.Name;

        var named = FindConvention("name");
        var valued = FindConvention("value");
        var first = result.Columns[0].Name;

        var value = configuredValue ?? configuredLabel ?? valued ?? named ?? first;
        var label = configuredLabel ?? (configuredValue is not null ? configuredValue : named ?? valued ?? first);
        return (label, value);
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
