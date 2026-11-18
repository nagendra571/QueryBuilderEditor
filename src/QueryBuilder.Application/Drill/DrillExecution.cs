using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Drill;

/// <summary>One click on a chart that already passed the browser's own filters, to be applied by the server as an extra
/// filter. The value is never trusted as SQL: it is resolved against the saved chart's data.</summary>
public sealed record CrossFilterDto(Guid SourceTileId, string Column, string? Value);

internal sealed record DrillRun(
    VisualizationContext Context,
    DrillRequest Request,
    IReadOnlyList<DrillStep> Steps,
    QueryDefinition Definition,
    VisualizationOptions? Options,
    List<QueryResultColumnDto> Columns,
    List<Dictionary<string, object?>> Rows,
    long ExecutionTimeMs,
    int Cap,
    bool Truncated)
{
    public object AuditDetails(object scope, string? format = null) => new
    {
        VisualizationId = Context.Visualization.Id,
        Request.DashboardId,
        Request.TileId,
        Action = Request.Action.ToString(),
        Steps = Steps.Select(s => new { s.Column, s.Value }).ToList(),
        Request.BreakdownColumn,
        RowCount = Rows.Count,
        Format = format,
        Scope = scope
    };
}

/// <summary>
/// The shared path of a drill and a drill download. The browser contributes only a click trail; the definition is derived
/// from the saved query (<see cref="DrillDefinitions"/>) and then handed to <see cref="VisualizationDataLoader.ExecuteAsync"/>,
/// the one place that validates, binds parameters, applies the caller's row-level scope and builds SQL.
/// </summary>
internal static class DrillExecution
{
    public static async Task<DrillRun> RunAsync(
        VisualizationDataLoader loader, IDataCatalogService catalog, Guid savedQueryId, Guid visualizationId,
        DrillRequest request, bool forExport, CancellationToken cancellationToken)
    {
        var context = await loader.ResolveAsync(
            savedQueryId, visualizationId, request.ParameterValues ?? [], request.DashboardId, request.TileId,
            forExport ? VisualizationDataPurpose.Download : VisualizationDataPurpose.Drill, cancellationToken);

        var steps = Validate(request, forExport);

        // Only the saved chart may name extra columns; a request that did could reach any column of the view.
        var drillColumns = (IReadOnlyList<string>)(context.Options.Drill?.Columns ?? []);
        var comparer = IdentifierMatching.Comparer(context.DataSource.Provider);
        bool Same(string a, string b) => comparer.Equals(a, b);
        var types = await DrillColumnTypesAsync(catalog, context, drillColumns, comparer, cancellationToken);
        var limit = await loader.EffectiveLimitAsync(context, cancellationToken);

        return request.Action == DrillAction.Rows
            ? await RunRowsAsync(loader, context, request, steps, drillColumns, types, Same, limit, forExport, cancellationToken)
            : await RunBreakdownAsync(loader, context, request, steps, drillColumns, types, Same, limit, cancellationToken);
    }

    private static List<DrillStep> Validate(DrillRequest request, bool forExport)
    {
        if (!Enum.IsDefined(request.Action))
            throw new CatalogValidationException("Unknown drill action.");
        if (request.CrossFilters is { Count: > 0 })
            throw new CatalogValidationException("Click-to-filter is not available for this request.");
        if (forExport && request.Action != DrillAction.Rows)
            throw new CatalogValidationException("Only drilled rows can be downloaded.");

        var steps = request.Steps ?? [];
        if (steps.Count > DrillLimits.MaxSteps)
            throw new CatalogValidationException($"A drill trail can have at most {DrillLimits.MaxSteps} steps.");
        foreach (var step in steps)
        {
            if (step is null || string.IsNullOrWhiteSpace(step.Column))
                throw new CatalogValidationException("Every drill step needs a column.");
        }

        if (request.Action == DrillAction.Breakdown && string.IsNullOrWhiteSpace(request.BreakdownColumn))
            throw new CatalogValidationException("A breakdown needs a column.");
        return steps;
    }

    private static async Task<Dictionary<string, ColumnDataType>> DrillColumnTypesAsync(
        IDataCatalogService catalog, VisualizationContext context, IReadOnlyList<string> drillColumns, StringComparer comparer,
        CancellationToken cancellationToken)
    {
        var types = new Dictionary<string, ColumnDataType>(comparer);
        if (drillColumns.Count == 0) return types;

        var source = context.Definition.Source;
        var found = (await catalog.GetCatalogAsync(context.SavedQuery.DataSourceId, cancellationToken)).Schemas
            .SelectMany(s => s.Objects)
            .FirstOrDefault(o => comparer.Equals(o.SchemaName, source.SchemaName) && comparer.Equals(o.Name, source.ObjectName));
        foreach (var column in found?.Columns ?? [])
        {
            types.TryAdd(column.Name, column.DataType);
        }
        return types;
    }

    private static async Task<DrillRun> RunRowsAsync(
        VisualizationDataLoader loader, VisualizationContext context, DrillRequest request, List<DrillStep> steps,
        IReadOnlyList<string> drillColumns, Dictionary<string, ColumnDataType> types, Func<string, string, bool> same,
        int? limit, bool forExport, CancellationToken cancellationToken)
    {
        var definition = DrillDefinitions.ForRows(context.Definition, steps, drillColumns, same, types);
        var requestedCap = forExport ? limit ?? RecordLimits.LegacyExportRows : RecordLimits.GridRows(limit, null);

        var (result, cap, _) = await loader.ExecuteAsync(context, definition, requestedCap, cancellationToken);

        if (forExport && result.Truncated)
        {
            // A cut-off file looks complete to whoever opens it, so an oversized download is refused, never trimmed.
            throw new RecordLimitExceededException(limit is { } l
                ? $"These rows exceed {l:N0} records, the record limit. Add filters to narrow it down before downloading."
                : $"These rows exceed {RecordLimits.LegacyExportRows:N0} records, too many to download. Add filters to narrow it down.");
        }

        var names = definition.Columns.Where(c => c.IsVisible).Select(c => c.EffectiveAlias).Distinct(StringComparer.Ordinal).ToList();
        var columns = new List<QueryResultColumnDto>();
        foreach (var name in names)
        {
            var column = result.Columns.FirstOrDefault(c => c.Name == name)
                ?? throw new CatalogValidationException($"This chart needs updating: column(s) {name} no longer exist.");
            columns.Add(new QueryResultColumnDto(column.Name, column.DataType));
        }
        var rows = Project(result, names);

        return new DrillRun(context, request, steps, definition, null, columns, rows, result.ExecutionTimeMs, cap, result.Truncated);
    }

    private static async Task<DrillRun> RunBreakdownAsync(
        VisualizationDataLoader loader, VisualizationContext context, DrillRequest request, List<DrillStep> steps,
        IReadOnlyList<string> drillColumns, Dictionary<string, ColumnDataType> types, Func<string, string, bool> same,
        int? limit, CancellationToken cancellationToken)
    {
        var type = context.Visualization.Type;
        var (definition, options) = DrillDefinitions.ForBreakdown(
            context.Definition, type, context.Options, steps, request.BreakdownColumn!, drillColumns, types, same);

        var (result, cap, _) = await loader.ExecuteAsync(context, definition, RecordLimits.ChartRows(limit), cancellationToken);
        if (result.Truncated)
        {
            throw VisualizationDataLoader.ChartTooLarge(limit, cap);
        }

        var referenced = VisualizationOptionsValidator.ReferencedColumns(type, options);
        var columns = VisualizationDataLoader.ProjectColumns(result, referenced, options, type, definition);
        var rows = Project(result, referenced);
        VisualizationDataChecks.Check(type, options, rows);

        return new DrillRun(context, request, steps, definition, options, columns, rows, result.ExecutionTimeMs, cap, false);
    }

    // Rows are cut to the named columns here, so nothing the SQL happened to return beyond them can reach the browser.
    private static List<Dictionary<string, object?>> Project(QueryResultSet result, IReadOnlyList<string> names) =>
        result.Rows
            .Select(row => names.ToDictionary(name => name, name => row.GetValueOrDefault(name), StringComparer.Ordinal))
            .ToList();
}
