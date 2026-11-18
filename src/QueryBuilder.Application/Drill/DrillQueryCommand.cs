using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSources;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Drill;

public sealed record DrillQueryCommand(Guid SavedQueryId, List<DrillStep> Steps, Dictionary<string, string?> ParameterValues) : IRequest<DrillResultDto>;

public sealed record ExportDrillQueryCommand(
    Guid SavedQueryId, List<DrillStep> Steps, Dictionary<string, string?> ParameterValues, ExportFormat Format) : IRequest<ExportResult>;

/// <summary>The path of both results-grid commands: a saved query's totals row to the detail rows behind it. Same loader,
/// same drill derivation and same execute path as a chart drill, minus the chart (so no drill columns).</summary>
internal static class DrillQueryExecution
{
    public static async Task<(QueryContext Context, IReadOnlyList<DrillStep> Steps, DrillExecution.DrillRows Rows)> RunAsync(
        VisualizationDataLoader loader, Guid savedQueryId, List<DrillStep>? steps, Dictionary<string, string?>? parameterValues,
        bool forExport, CancellationToken cancellationToken)
    {
        var context = await loader.ResolveQueryAsync(savedQueryId, parameterValues ?? [], cancellationToken);
        var checkedSteps = DrillExecution.Validate(new DrillRequest(DrillAction.Rows, steps, null, null, parameterValues, null, null), forExport);

        if (!DrillDefinitions.IsAggregated(context.Definition))
            throw new CatalogValidationException("This query has no totals to drill into.");

        var comparer = IdentifierMatching.Comparer(context.DataSource.Provider);
        var limit = await loader.EffectiveLimitAsync(context, cancellationToken);
        var rows = await DrillExecution.RunRowsCoreAsync(
            loader, context, checkedSteps, [], new Dictionary<string, ColumnDataType>(comparer), comparer.Equals, limit, forExport,
            "query", cancellationToken);
        return (context, checkedSteps, rows);
    }
}

public sealed class DrillQueryCommandHandler(VisualizationDataLoader loader, ICurrentDataScope currentScope, IAuditLogger auditLogger)
    : IRequestHandler<DrillQueryCommand, DrillResultDto>
{
    public async Task<DrillResultDto> Handle(DrillQueryCommand command, CancellationToken cancellationToken)
    {
        var (context, steps, rows) = await DrillQueryExecution.RunAsync(
            loader, command.SavedQueryId, command.Steps, command.ParameterValues, forExport: false, cancellationToken);

        var source = context.Definition.Source;
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryDrilled,
                nameof(SavedQuery),
                context.SavedQuery.Id,
                $"{source.SchemaName}.{source.ObjectName}",
                context.SavedQuery.DataSourceId,
                $"Drilled results of '{context.SavedQuery.Name}' ({DrillAction.Rows}), returned {rows.Rows.Count} row(s) in {rows.ExecutionTimeMs}ms",
                DrillAudit.Details(steps, DrillAction.Rows, null, rows.Rows.Count, (await currentScope.GetAsync(cancellationToken)).Describe(), null, null, null, null)),
            cancellationToken);

        return new DrillResultDto(
            DrillAction.Rows, rows.Columns, rows.Rows, rows.Rows.Count, rows.ExecutionTimeMs, rows.Cap, rows.Truncated,
            VisualizationType.Table, null);
    }
}

public sealed class ExportDrillQueryCommandHandler(
    VisualizationDataLoader loader, IExportService exportService, ICurrentDataScope currentScope, IAuditLogger auditLogger)
    : IRequestHandler<ExportDrillQueryCommand, ExportResult>
{
    public async Task<ExportResult> Handle(ExportDrillQueryCommand command, CancellationToken cancellationToken)
    {
        var (context, steps, rows) = await DrillQueryExecution.RunAsync(
            loader, command.SavedQueryId, command.Steps, command.ParameterValues, forExport: true, cancellationToken);

        var resultSet = new QueryResultSet
        {
            Columns = rows.Columns.Select(c => new ResultColumn { Name = c.Name, DataType = c.DataType }).ToList(),
            Rows = rows.Rows,
            ExecutionTimeMs = rows.ExecutionTimeMs
        };

        var name = ExportNames.FileName(context.SavedQuery.Name, "query");
        var content = await exportService.ExportAsync(resultSet, command.Format, context.SavedQuery.Name, cancellationToken);

        var source = context.Definition.Source;
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryExported,
                nameof(SavedQuery),
                context.SavedQuery.Id,
                $"{source.SchemaName}.{source.ObjectName}",
                context.SavedQuery.DataSourceId,
                $"Downloaded drilled rows of '{context.SavedQuery.Name}' as {command.Format} ({rows.Rows.Count} row(s))",
                DrillAudit.Details(steps, DrillAction.Rows, null, rows.Rows.Count, (await currentScope.GetAsync(cancellationToken)).Describe(), command.Format.ToString(), null, null, null)),
            cancellationToken);

        return new ExportResult(content, exportService.GetContentType(command.Format), name + exportService.GetFileExtension(command.Format));
    }
}
