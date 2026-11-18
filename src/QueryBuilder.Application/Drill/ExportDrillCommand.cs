using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSources;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Drill;

public sealed record ExportDrillCommand(Guid SavedQueryId, Guid VisualizationId, DrillRequest Request, ExportFormat Format) : IRequest<ExportResult>;

/// <summary>Downloads drilled rows. A file looks complete to whoever opens it, so rows over the record limit are refused
/// rather than cut off; only rows (not a regrouped chart) can be downloaded.</summary>
public sealed class ExportDrillCommandHandler(
    VisualizationDataLoader loader,
    IDataCatalogService catalog,
    IExportService exportService,
    ICurrentDataScope currentScope,
    IAuditLogger auditLogger)
    : IRequestHandler<ExportDrillCommand, ExportResult>
{
    public async Task<ExportResult> Handle(ExportDrillCommand command, CancellationToken cancellationToken)
    {
        var run = await DrillExecution.RunAsync(
            loader, catalog, command.SavedQueryId, command.VisualizationId, command.Request, forExport: true, cancellationToken);

        var resultSet = new QueryResultSet
        {
            Columns = run.Columns.Select(c => new ResultColumn { Name = c.Name, DataType = c.DataType }).ToList(),
            Rows = run.Rows,
            ExecutionTimeMs = run.ExecutionTimeMs
        };

        var visualization = run.Context.Visualization;
        var name = ExportNames.FileName(visualization.Name, "chart");
        var content = await exportService.ExportAsync(resultSet, command.Format, visualization.Name, cancellationToken);

        var source = run.Context.Definition.Source;
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryExported,
                nameof(SavedQuery),
                run.Context.SavedQuery.Id,
                $"{source.SchemaName}.{source.ObjectName}",
                run.Context.SavedQuery.DataSourceId,
                $"Downloaded drilled rows of chart '{visualization.Name}' on '{run.Context.SavedQuery.Name}' as {command.Format} ({run.Rows.Count} row(s))",
                run.AuditDetails((await currentScope.GetAsync(cancellationToken)).Describe(), command.Format.ToString())),
            cancellationToken);

        return new ExportResult(content, exportService.GetContentType(command.Format), name + exportService.GetFileExtension(command.Format));
    }
}
