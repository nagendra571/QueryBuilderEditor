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
    Guid? TileId = null,
    List<Drill.CrossFilterDto>? CrossFilters = null) : IRequest<VisualizationDataDto>;

/// <summary>
/// Draws a saved chart from the viewer's own authorized results (see <see cref="VisualizationDataLoader"/>). A chart over
/// too many rows is refused rather than drawn partially, except a table, which shows the first rows and flags the result as
/// truncated.
/// </summary>
public sealed class RenderVisualizationCommandHandler(
    VisualizationDataLoader loader,
    Drill.CrossFilterResolver crossFilterResolver,
    ICurrentDataScope currentScope,
    IAuditLogger auditLogger)
    : IRequestHandler<RenderVisualizationCommand, VisualizationDataDto>
{
    public async Task<VisualizationDataDto> Handle(RenderVisualizationCommand request, CancellationToken cancellationToken)
    {
        var data = await loader.LoadAsync(
            request.SavedQueryId, request.VisualizationId, request.ParameterValues, request.DashboardId, request.TileId,
            VisualizationDataPurpose.Draw, cancellationToken, request.CrossFilters, crossFilterResolver);

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
                RunDetails(data, (await currentScope.GetAsync(cancellationToken)).Describe())),
            cancellationToken);

        return new VisualizationDataDto(data.Columns, data.Rows, data.Rows.Count, data.ExecutionTimeMs, data.RowLimit, data.Truncated, data.KeyColumns, data.CrossFilter);
    }

    // Only when a click-filter narrowed the chart, so a plain render's audit entry keeps its old shape. The filters are the
    // resolved ones (the target's own column names), cut short like drill audits: a value can be up to 4,000 characters.
    private static object RunDetails(LoadedVisualizationData data, object scope)
    {
        if (data.CrossFilterSteps is not { Count: > 0 } steps)
        {
            return new { VisualizationId = data.Visualization.Id, RowCount = data.Rows.Count, data.ExecutionTimeMs, Scope = scope };
        }

        return new
        {
            VisualizationId = data.Visualization.Id,
            RowCount = data.Rows.Count,
            data.ExecutionTimeMs,
            Scope = scope,
            CrossFilters = steps.Select(s => new { s.Column, Value = Drill.DrillAudit.Truncate(s.Value) }).ToList()
        };
    }
}
