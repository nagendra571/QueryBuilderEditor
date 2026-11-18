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
    public object AuditDetails(object scope, string? format = null) =>
        DrillAudit.Details(
            Steps, Request.Action, Request.BreakdownColumn, Rows.Count, scope, format,
            Context.Visualization.Id,
            // Only when the grant is what let the request in: otherwise the ids say nothing about why it was allowed.
            Context.CanView ? null : Request.DashboardId, Context.CanView ? null : Request.TileId);
}

internal static class DrillAudit
{
    public const int MaxAuditedValueLength = 200;

    public static Dictionary<string, object?> Details(
        IReadOnlyList<DrillStep> steps, DrillAction action, string? breakdownColumn, int rowCount, object scope, string? format,
        Guid? visualizationId, Guid? dashboardId, Guid? tileId)
    {
        var details = new Dictionary<string, object?>();
        if (visualizationId is not null) details["VisualizationId"] = visualizationId;
        if (dashboardId is not null) details["DashboardId"] = dashboardId;
        if (tileId is not null) details["TileId"] = tileId;
        details["Action"] = action.ToString();
        // A step value can be up to 4,000 characters; the log keeps enough to recognise it, not to hold a pasted document.
        details["Steps"] = steps.Select(s => new { s.Column, Value = Truncate(s.Value) }).ToList();
        details["BreakdownColumn"] = breakdownColumn;
        details["RowCount"] = rowCount;
        if (format is not null) details["Format"] = format;
        details["Scope"] = scope;
        return details;
    }

    private static string? Truncate(string? value)
    {
        if (value is null || value.Length <= MaxAuditedValueLength) return value;
        var end = char.IsHighSurrogate(value[MaxAuditedValueLength - 1]) ? MaxAuditedValueLength - 1 : MaxAuditedValueLength;
        return value[..end];
    }
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

    internal static List<DrillStep> Validate(DrillRequest request, bool forExport)
    {
        if (!Enum.IsDefined(request.Action))
            throw new CatalogValidationException("Unknown drill action.");
        if (request.CrossFilters is { Count: > 0 })
            throw new CatalogValidationException("Click-to-filter is not available for this request.");
        if (forExport && request.Action != DrillAction.Rows)
            throw new CatalogValidationException("Only drilled rows can be downloaded.");

        var steps = request.Steps ?? [];
        foreach (var step in steps)
        {
            if (step is null || string.IsNullOrWhiteSpace(step.Column))
                throw new CatalogValidationException("Every drill step needs a column.");
        }

        if (request.Action == DrillAction.Breakdown && string.IsNullOrWhiteSpace(request.BreakdownColumn))
            throw new CatalogValidationException("A breakdown needs a column.");
        return steps;
    }

    internal static async Task<Dictionary<string, ColumnDataType>> DrillColumnTypesAsync(
        IDataCatalogService catalog, QueryContext context, IReadOnlyList<string> drillColumns, StringComparer comparer,
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
        var rows = await RunRowsCoreAsync(loader, context, steps, drillColumns, types, same, limit, forExport, "chart", cancellationToken);
        return new DrillRun(context, request, steps, rows.Definition, null, rows.Columns, rows.Rows, rows.ExecutionTimeMs, rows.Cap, rows.Truncated);
    }

    internal sealed record DrillRows(
        QueryDefinition Definition, List<QueryResultColumnDto> Columns, List<Dictionary<string, object?>> Rows,
        long ExecutionTimeMs, int Cap, bool Truncated);

    /// <summary>The rows behind a click trail, for a chart and for the results grid alike: derive the drill definition, then
    /// the loader's single validate, bind, scope and build path. Nothing here knows whether a chart was involved.</summary>
    internal static async Task<DrillRows> RunRowsCoreAsync(
        VisualizationDataLoader loader, QueryContext context, IReadOnlyList<DrillStep> steps,
        IReadOnlyList<string> drillColumns, Dictionary<string, ColumnDataType> types, Func<string, string, bool> same,
        int? limit, bool forExport, string subject, CancellationToken cancellationToken)
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
                ?? throw new CatalogValidationException($"This {subject} needs updating: column(s) {name} no longer exist.");
            columns.Add(new QueryResultColumnDto(column.Name, column.DataType));
        }

        return new DrillRows(definition, columns, Project(result, names), result.ExecutionTimeMs, cap, result.Truncated);
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
    internal static List<Dictionary<string, object?>> Project(QueryResultSet result, IReadOnlyList<string> names) =>
        result.Rows
            .Select(row => names.ToDictionary(name => name, name => row.GetValueOrDefault(name), StringComparer.Ordinal))
            .ToList();
}
