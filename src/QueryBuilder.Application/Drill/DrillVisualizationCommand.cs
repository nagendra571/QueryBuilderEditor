using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Drill;

public sealed record DrillVisualizationCommand(Guid SavedQueryId, Guid VisualizationId, DrillRequest Request) : IRequest<DrillResultDto>;

/// <summary>Rows behind a chart mark, or a chart regrouped by another column, both for the caller's own authorized data
/// (see <see cref="DrillExecution"/>). Every drill is audited because it reads detail rows the chart itself only summarised.</summary>
public sealed class DrillVisualizationCommandHandler(
    VisualizationDataLoader loader,
    IDataCatalogService catalog,
    ICurrentDataScope currentScope,
    IAuditLogger auditLogger)
    : IRequestHandler<DrillVisualizationCommand, DrillResultDto>
{
    public async Task<DrillResultDto> Handle(DrillVisualizationCommand command, CancellationToken cancellationToken)
    {
        var run = await DrillExecution.RunAsync(
            loader, catalog, command.SavedQueryId, command.VisualizationId, command.Request, forExport: false, cancellationToken);

        var source = run.Context.Definition.Source;
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryDrilled,
                nameof(SavedQuery),
                run.Context.SavedQuery.Id,
                $"{source.SchemaName}.{source.ObjectName}",
                run.Context.SavedQuery.DataSourceId,
                $"Drilled chart '{run.Context.Visualization.Name}' on '{run.Context.SavedQuery.Name}' ({run.Request.Action}), returned {run.Rows.Count} row(s) in {run.ExecutionTimeMs}ms",
                run.AuditDetails((await currentScope.GetAsync(cancellationToken)).Describe())),
            cancellationToken);

        return new DrillResultDto(
            run.Request.Action, run.Columns, run.Rows, run.Rows.Count, run.ExecutionTimeMs, run.Cap, run.Truncated,
            run.Context.Visualization.Type, run.Options);
    }
}
