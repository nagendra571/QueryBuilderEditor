using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSources;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Visualizations;

public sealed record ExportVisualizationCommand(
    Guid SavedQueryId,
    Guid VisualizationId,
    Dictionary<string, string?> ParameterValues,
    ExportFormat Format,
    Guid? DashboardId = null,
    Guid? TileId = null) : IRequest<ExportResult>;

/// <summary>
/// Downloads the rows a saved chart is drawn from, as CSV or Excel: the same rows and columns the caller would see drawn,
/// loaded through the same checks (see <see cref="VisualizationDataLoader"/>), so anyone who can see a chart — including
/// a hub Viewer through a dashboard — can download what it shows and nothing more. A table downloads all its rows up to
/// the record limit; a download that would be cut off is refused.
/// </summary>
public sealed class ExportVisualizationCommandHandler(
    VisualizationDataLoader loader,
    IExportService exportService,
    ICurrentDataScope currentScope,
    IAuditLogger auditLogger)
    : IRequestHandler<ExportVisualizationCommand, ExportResult>
{
    public async Task<ExportResult> Handle(ExportVisualizationCommand request, CancellationToken cancellationToken)
    {
        var data = await loader.LoadAsync(
            request.SavedQueryId, request.VisualizationId, request.ParameterValues, request.DashboardId, request.TileId,
            VisualizationDataPurpose.Download, cancellationToken);

        var resultSet = new QueryResultSet
        {
            Columns = data.Columns.Select(c => new ResultColumn { Name = c.Name, DataType = c.DataType }).ToList(),
            Rows = data.Rows,
            ExecutionTimeMs = data.ExecutionTimeMs
        };

        var name = ExportNames.FileName(data.Visualization.Name, "chart");
        var content = await exportService.ExportAsync(resultSet, request.Format, data.Visualization.Name, cancellationToken);

        var source = data.Definition.Source;
        var entityName = $"{source.SchemaName}.{source.ObjectName}";
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryExported,
                nameof(SavedQuery),
                data.SavedQuery.Id,
                entityName,
                data.SavedQuery.DataSourceId,
                $"Downloaded chart '{data.Visualization.Name}' on '{data.SavedQuery.Name}' as {request.Format} ({data.Rows.Count} row(s))",
                new
                {
                    VisualizationId = data.Visualization.Id,
                    request.Format,
                    RowCount = data.Rows.Count,
                    Scope = (await currentScope.GetAsync(cancellationToken)).Describe()
                }),
            cancellationToken);

        return new ExportResult(content, exportService.GetContentType(request.Format), name + exportService.GetFileExtension(request.Format));
    }
}
