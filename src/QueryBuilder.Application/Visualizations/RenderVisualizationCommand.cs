using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Visualizations;

public sealed record RenderVisualizationCommand(
    Guid SavedQueryId,
    Guid VisualizationId,
    Dictionary<string, string?> ParameterValues,
    Guid? DashboardId = null,
    Guid? TileId = null) : IRequest<VisualizationDataDto>;

/// <summary>
/// Draws a saved chart from the viewer's own authorized results (see <see cref="VisualizationDataLoader"/>). A chart over
/// too many rows is refused rather than drawn partially, except a table, which shows the first rows and flags the result as
/// truncated.
/// </summary>
public sealed class RenderVisualizationCommandHandler(
    VisualizationDataLoader loader,
    ICurrentDataScope currentScope,
    IAuditLogger auditLogger)
    : IRequestHandler<RenderVisualizationCommand, VisualizationDataDto>
{
    public async Task<VisualizationDataDto> Handle(RenderVisualizationCommand request, CancellationToken cancellationToken)
    {
        var data = await loader.LoadAsync(
            request.SavedQueryId, request.VisualizationId, request.ParameterValues, request.DashboardId, request.TileId,
            VisualizationDataPurpose.Draw, cancellationToken);

        var source = data.Definition.Source;
        var entityName = $"{source.SchemaName}.{source.ObjectName}";
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryRun,
                nameof(SavedQuery),
                data.SavedQuery.Id,
                entityName,
                data.SavedQuery.DataSourceId,
                $"Rendered chart '{data.Visualization.Name}' on '{data.SavedQuery.Name}', returned {data.Rows.Count} row(s) in {data.ExecutionTimeMs}ms",
                new
                {
                    VisualizationId = data.Visualization.Id,
                    RowCount = data.Rows.Count,
                    data.ExecutionTimeMs,
                    Scope = (await currentScope.GetAsync(cancellationToken)).Describe()
                }),
            cancellationToken);

        return new VisualizationDataDto(data.Columns, data.Rows, data.Rows.Count, data.ExecutionTimeMs, data.RowLimit, data.Truncated);
    }
}
