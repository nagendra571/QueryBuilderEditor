using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Parameters;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Visualizations;

public enum VisualizationDataPurpose
{
    Draw,
    Download
}

public sealed record LoadedVisualizationData(
    SavedQuery SavedQuery,
    QueryVisualization Visualization,
    QueryDefinition Definition,
    List<QueryResultColumnDto> Columns,
    List<Dictionary<string, object?>> Rows,
    long ExecutionTimeMs,
    int RowLimit,
    bool Truncated);

/// <summary>
/// The rows behind a saved chart, for drawing it or downloading them. Both go through one path on purpose, so a download
/// can never reach data the chart itself would not show: the definition is loaded from the database (never taken from the
/// browser), access is checked against shares or a dashboard grant, and the catalog validation, the caller's row-level scope
/// and the record limits all apply. Only the columns the chart references are returned.
/// </summary>
public sealed class VisualizationDataLoader(
    ISavedQueryRepository savedQueries,
    IVisualizationRepository visualizations,
    IDataSourceRepository dataSources,
    IDataCatalogService catalogService,
    IQuerySqlBuilderFactory sqlBuilderFactory,
    IQueryExecutionService executionService,
    IDataScopeGuard dataScopeGuard,
    RecordLimitSettings recordLimitSettings,
    ICurrentAppSettings appSettings,
    ICurrentUserService currentUser,
    IParameterBinder parameterBinder,
    Dashboards.IDashboardGrant dashboardGrant,
    Dashboards.DashboardGrantResolver grantResolver)
{
    public async Task<LoadedVisualizationData> LoadAsync(
        Guid savedQueryId,
        Guid visualizationId,
        Dictionary<string, string?> parameterValues,
        Guid? dashboardId,
        Guid? tileId,
        VisualizationDataPurpose purpose,
        CancellationToken cancellationToken)
    {
        var savedQuery = await savedQueries.GetByIdAsync(savedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), savedQueryId);

        // Checked against the share table on every request, so a revoked share takes effect at once. A hub Viewer has no
        // query access of its own, so only a dashboard context that proves this chart belongs to a dashboard they can
        // see gets through; an invalid or missing context leaves the grant empty and the refusal stands.
        var actor = AccessActor.From(currentUser);
        if (dashboardId is { } dashboard && tileId is { } tile)
        {
            await grantResolver.TryGrantAsync(dashboard, tile, savedQuery, visualizationId, actor, cancellationToken);
        }

        var canView = SavedQueryAccess.CanView(savedQuery, actor);
        if (!(canView || dashboardGrant.Covers(savedQuery.Id)))
        {
            throw new ForbiddenException("You do not have access to this query.");
        }

        var values = canView ? parameterValues : grantResolver.ConstrainValues(parameterValues);

        var visualization = await visualizations.GetByIdAsync(visualizationId, cancellationToken);
        if (visualization is null || visualization.SavedQueryId != savedQuery.Id)
        {
            throw new NotFoundException(nameof(QueryVisualization), visualizationId);
        }

        if (savedQuery.IsDisabled)
        {
            throw new QueryDisabledException(purpose == VisualizationDataPurpose.Download
                ? $"'{savedQuery.Name}' is disabled and cannot be exported."
                : $"'{savedQuery.Name}' is disabled and cannot be run.");
        }

        var definition = QueryDefinitionSerializer.Deserialize(savedQuery.DefinitionJson);
        var options = VisualizationOptionsSerializer.Deserialize(visualization.OptionsJson);
        ThrowIfStale(visualization.Type, options, definition);

        var dataSource = await dataSources.GetByIdAsync(savedQuery.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), savedQuery.DataSourceId);

        await catalogService.ValidateAsync(savedQuery.DataSourceId, definition, cancellationToken);
        var bound = await parameterBinder.MaterializeAsync(definition, values, cancellationToken);
        var scopePredicates = await dataScopeGuard.AuthorizeAsync(savedQuery.DataSourceId, bound, cancellationToken);

        var builder = sqlBuilderFactory.GetBuilder(dataSource.Provider);
        var generated = builder.Build(bound, scopePredicates);

        var effectiveLimit = RecordLimits.Effective(dataSource.MaxRecords, await appSettings.MaxRecordsAsync(cancellationToken), recordLimitSettings);

        // A downloaded table is a file, not a screen: it gets every row up to the record limit (or the legacy export cap)
        // instead of the first screenful. Every other chart downloads exactly the rows it is drawn from.
        var downloadTable = purpose == VisualizationDataPurpose.Download && visualization.Type == VisualizationType.Table;
        var cap = downloadTable ? effectiveLimit ?? RecordLimits.LegacyExportRows : RecordLimits.ChartRows(effectiveLimit);

        var result = await executionService.ExecuteAsync(
            dataSource, generated, new Dictionary<string, string?>(), cap, cancellationToken);

        if (result.Truncated)
        {
            if (downloadTable)
            {
                // A cut-off file looks complete to whoever opens it, so an oversized download is refused, never trimmed.
                throw new RecordLimitExceededException(effectiveLimit is { } limit
                    ? $"This chart has more than {limit:N0} records, the record limit. Add filters to narrow it down before downloading."
                    : $"This chart has more than {RecordLimits.LegacyExportRows:N0} records, too many to download. Add filters to narrow it down.");
            }

            // A table is a list, not a picture: showing the first N rows with a warning is honest, whereas a
            // truncated chart or pivot would silently draw wrong totals.
            if (visualization.Type != VisualizationType.Table)
            {
                throw new RecordLimitExceededException(effectiveLimit is { } limit && limit <= RecordLimits.ChartMaxRows
                    ? $"This query has more than {cap:N0} records — add filters or totals to chart it."
                    : $"This chart has too many points to chart (max {RecordLimits.ChartMaxRows:N0}) — add filters or totals.");
            }
        }

        var referenced = VisualizationOptionsValidator.ReferencedColumns(visualization.Type, options);
        var columns = ProjectColumns(result, referenced, options, visualization.Type, definition);

        var rows = result.Rows
            .Select(row => referenced.ToDictionary(name => name, name => row.GetValueOrDefault(name), StringComparer.Ordinal))
            .ToList();

        VisualizationDataChecks.Check(visualization.Type, options, rows);
        if (visualization.Type == VisualizationType.Counter)
        {
            rows = rows.Take(1).ToList();
        }

        return new LoadedVisualizationData(savedQuery, visualization, definition, columns, rows, result.ExecutionTimeMs, cap, result.Truncated);
    }

    private static void ThrowIfStale(VisualizationType type, VisualizationOptions options, QueryDefinition definition)
    {
        var issues = VisualizationOptionsValidator.Validate(type, options, definition);
        if (issues.Count == 0)
        {
            return;
        }

        var missing = issues.Where(i => i.MissingColumn is not null).Select(i => i.MissingColumn!).Distinct().ToList();
        throw new CatalogValidationException(missing.Count > 0
            ? $"This chart needs updating: column(s) {string.Join(", ", missing)} no longer exist."
            : $"This chart needs updating: {string.Join(" ", issues.Select(i => i.Message))}");
    }

    private static List<QueryResultColumnDto> ProjectColumns(
        QueryResultSet result, IReadOnlyList<string> referenced, VisualizationOptions options, VisualizationType type,
        QueryDefinition definition)
    {
        var measures = new HashSet<string>(StringComparer.Ordinal);
        switch (type)
        {
            case VisualizationType.Table:
                break;
            case VisualizationType.Pivot:
                if (options.Pivot is { } pivot) measures.UnionWith(pivot.Values);
                break;
            case VisualizationType.Counter:
                if (options.Counter is { } counter)
                {
                    measures.Add(counter.Value);
                    if (counter.Target is not null) measures.Add(counter.Target);
                }
                break;
            default:
                measures.UnionWith(options.Measures);
                if (type == VisualizationType.Scatter && !string.IsNullOrEmpty(options.Size)) measures.Add(options.Size);
                break;
        }

        var aggregates = definition.Columns
            .Where(c => c.IsVisible)
            .GroupBy(c => c.EffectiveAlias, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Aggregate, StringComparer.Ordinal);

        var columns = new List<QueryResultColumnDto>();
        foreach (var name in referenced)
        {
            var column = result.Columns.FirstOrDefault(c => c.Name == name)
                ?? throw new CatalogValidationException($"This chart needs updating: column(s) {name} no longer exist.");

            if (measures.Contains(name) && column.DataType != ColumnDataType.Number)
            {
                throw new CatalogValidationException($"'{name}' is not a number column.");
            }
            columns.Add(new QueryResultColumnDto(column.Name, column.DataType, aggregates.GetValueOrDefault(name)));
        }
        return columns;
    }
}
