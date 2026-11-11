using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Parameters;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Visualizations;

public sealed record RenderVisualizationCommand(
    Guid SavedQueryId,
    Guid VisualizationId,
    Dictionary<string, string?> ParameterValues) : IRequest<VisualizationDataDto>;

/// <summary>
/// Draws a saved chart from the viewer's own authorized results. The definition is loaded from the
/// database — never taken from the browser — and goes through the same catalog validation and
/// row-level scope guard as a run, with the <em>viewer's</em> scope. A chart over too many rows is
/// refused rather than drawn partially, except a table, which shows the first rows and flags the
/// result as truncated.
/// </summary>
public sealed class RenderVisualizationCommandHandler(
    ISavedQueryRepository savedQueries,
    IVisualizationRepository visualizations,
    IDataSourceRepository dataSources,
    IDataCatalogService catalogService,
    IQuerySqlBuilderFactory sqlBuilderFactory,
    IQueryExecutionService executionService,
    IDataScopeGuard dataScopeGuard,
    ICurrentDataScope currentScope,
    RecordLimitSettings recordLimitSettings,
    IAuditLogger auditLogger,
    ICurrentUserService currentUser,
    IParameterBinder parameterBinder)
    : IRequestHandler<RenderVisualizationCommand, VisualizationDataDto>
{
    public async Task<VisualizationDataDto> Handle(RenderVisualizationCommand request, CancellationToken cancellationToken)
    {
        var savedQuery = await savedQueries.GetByIdAsync(request.SavedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.SavedQueryId);

        // Checked against the share table on every request, so a revoked share takes effect at once.
        if (!SavedQueryAccess.CanView(savedQuery, AccessActor.From(currentUser)))
        {
            throw new ForbiddenException("You do not have access to this query.");
        }

        var visualization = await visualizations.GetByIdAsync(request.VisualizationId, cancellationToken);
        if (visualization is null || visualization.SavedQueryId != savedQuery.Id)
        {
            throw new NotFoundException(nameof(QueryVisualization), request.VisualizationId);
        }

        if (savedQuery.IsDisabled)
        {
            throw new QueryDisabledException($"'{savedQuery.Name}' is disabled and cannot be run.");
        }

        var definition = QueryDefinitionSerializer.Deserialize(savedQuery.DefinitionJson);
        var options = VisualizationOptionsSerializer.Deserialize(visualization.OptionsJson);
        ThrowIfStale(visualization.Type, options, definition);

        var dataSource = await dataSources.GetByIdAsync(savedQuery.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), savedQuery.DataSourceId);

        await catalogService.ValidateAsync(savedQuery.DataSourceId, definition, cancellationToken);
        var bound = await parameterBinder.MaterializeAsync(definition, request.ParameterValues, cancellationToken);
        var scopePredicates = await dataScopeGuard.AuthorizeAsync(savedQuery.DataSourceId, bound, cancellationToken);

        var builder = sqlBuilderFactory.GetBuilder(dataSource.Provider);
        var generated = builder.Build(bound, scopePredicates);

        var effectiveLimit = RecordLimits.Effective(dataSource.MaxRecords, recordLimitSettings);
        var cap = RecordLimits.ChartRows(effectiveLimit);

        var result = await executionService.ExecuteAsync(
            dataSource, generated, new Dictionary<string, string?>(), cap, cancellationToken);

        // A table is a list, not a picture: showing the first N rows with a warning is honest, whereas a
        // truncated chart or pivot would silently draw wrong totals.
        if (result.Truncated && visualization.Type != VisualizationType.Table)
        {
            throw new RecordLimitExceededException(effectiveLimit is { } limit && limit <= RecordLimits.ChartMaxRows
                ? $"This query has more than {cap:N0} records — add filters or totals to chart it."
                : $"This chart has too many points to chart (max {RecordLimits.ChartMaxRows:N0}) — add filters or totals.");
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

        var entityName = $"{definition.Source.SchemaName}.{definition.Source.ObjectName}";
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryRun,
                nameof(SavedQuery),
                savedQuery.Id,
                entityName,
                savedQuery.DataSourceId,
                $"Rendered chart '{visualization.Name}' on '{savedQuery.Name}', returned {rows.Count} row(s) in {result.ExecutionTimeMs}ms",
                new
                {
                    VisualizationId = visualization.Id,
                    RowCount = rows.Count,
                    result.ExecutionTimeMs,
                    Scope = (await currentScope.GetAsync(cancellationToken)).Describe()
                }),
            cancellationToken);

        return new VisualizationDataDto(columns, rows, rows.Count, result.ExecutionTimeMs, cap, result.Truncated);
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
