using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dashboards;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Drill;

/// <summary>
/// Turns a dashboard's click-filters into drill steps for one target tile. The browser names a source tile and one of its
/// output columns plus a value; everything else (which column that is in the catalog, whether the target can take it) is
/// decided here from saved definitions, so a click can only narrow what the viewer's own row scope already allows. The
/// steps still pass through <see cref="VisualizationDataLoader.ExecuteAsync"/>.
/// </summary>
public sealed class CrossFilterResolver(IDashboardRepository dashboards, IDataSourceRepository dataSources, IDataCatalogService catalog)
{
    public const string TurnedOff = "Click-to-filter is turned off for this dashboard.";

    /// <summary>The request-facing entry: proves the dashboard and the target tile belong together, then resolves. No
    /// filters means no lookups at all, so an unfiltered request costs nothing here.</summary>
    public async Task<(List<DrillStep> Steps, CrossFilterResultDto Result)> ResolveForRequestAsync(
        Guid? dashboardId, Guid? tileId, Guid targetVisualizationId, IReadOnlyList<CrossFilterDto>? filters, SavedQuery targetQuery,
        AccessActor actor, CancellationToken cancellationToken)
    {
        if (filters is not { Count: > 0 }) return (new List<DrillStep>(), new CrossFilterResultDto([], []));

        if (dashboardId is not { } id || tileId is not { } tile)
            throw new CatalogValidationException("Click-to-filter needs the dashboard the chart is on.");

        var dashboard = await dashboards.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), id);
        if (!DashboardAccess.CanView(dashboard, actor))
            throw new ForbiddenException("You do not have access to this dashboard.");

        var target = dashboard.Tiles.FirstOrDefault(t => t.Id == tile && t.Kind == DashboardTileKind.Chart);
        if (target?.VisualizationId != targetVisualizationId)
            throw new CatalogValidationException("That chart is not a tile of this dashboard.");

        return await ResolveAsync(dashboard, filters, targetQuery, actor, cancellationToken);
    }

    public async Task<(List<DrillStep> Steps, CrossFilterResultDto Result)> ResolveAsync(
        Dashboard dashboard, IReadOnlyList<CrossFilterDto> filters, SavedQuery targetQuery, AccessActor actor,
        CancellationToken cancellationToken)
    {
        if (filters.Count == 0) return (new List<DrillStep>(), new CrossFilterResultDto([], []));

        if (!dashboard.ClickToFilterEnabled) throw new CatalogValidationException(TurnedOff);
        if (filters.Count > DrillLimits.MaxCrossFilters)
            throw new CatalogValidationException($"At most {DrillLimits.MaxCrossFilters} click-filters can apply at once.");

        var targetSource = await dataSources.GetByIdAsync(targetQuery.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), targetQuery.DataSourceId);
        var comparer = IdentifierMatching.Comparer(targetSource.Provider);
        var targetDefinition = QueryDefinitionSerializer.Deserialize(targetQuery.DefinitionJson);

        DataSourceCatalog? targetCatalog = null;
        var steps = new List<DrillStep>();
        var applied = new List<string>();
        var notApplied = new List<string>();

        foreach (var filter in filters)
        {
            if (filter is null || string.IsNullOrWhiteSpace(filter.Column))
                throw new CatalogValidationException("Every click-filter needs a column.");

            var tile = dashboard.Tiles.FirstOrDefault(t => t.Id == filter.SourceTileId && t.Kind == DashboardTileKind.Chart);
            if (tile?.Visualization is not { SavedQuery: { } sourceQuery } sourceChart)
                throw new CatalogValidationException($"'{filter.Column}' can't be used to filter this dashboard.");

            // The dashboard being visible is not enough outside the hub: a tile whose query the caller can't open is shown
            // to them as restricted, so its columns must not be usable (or probed) as filters either.
            if (!(actor.InHub || SavedQueryAccess.CanView(sourceQuery, actor)))
                throw new CatalogValidationException($"'{filter.Column}' can't be used to filter this dashboard.");

            var sourceDefinition = QueryDefinitionSerializer.Deserialize(sourceQuery.DefinitionJson);
            var sourceOptions = VisualizationOptionsSerializer.Deserialize(sourceChart.OptionsJson);
            var drillColumns = (IReadOnlyList<string>)(sourceOptions.Drill?.Columns ?? []);
            if (!DrillDefinitions.AllowedStepColumns(sourceDefinition, drillColumns).Contains(filter.Column, StringComparer.Ordinal))
                throw new CatalogValidationException($"'{filter.Column}' can't be used to filter this dashboard.");

            var shown = sourceDefinition.Columns.FirstOrDefault(c =>
                c.IsVisible && c.Aggregate == AggregateFunction.None && c.EffectiveAlias == filter.Column);
            var catalogName = shown?.ColumnName ?? filter.Column;

            string? targetColumn = null;
            if (sourceQuery.DataSourceId == targetQuery.DataSourceId)
            {
                targetCatalog ??= await catalog.GetCatalogAsync(targetQuery.DataSourceId, cancellationToken);
                targetColumn = TargetColumn(targetCatalog, targetDefinition, catalogName, comparer);
            }

            if (targetColumn is null)
            {
                notApplied.Add(filter.Column);
                continue;
            }

            steps.Add(new DrillStep(targetColumn, filter.Value));
            applied.Add(filter.Column);
        }

        return (steps, new CrossFilterResultDto(applied.Distinct(StringComparer.Ordinal).ToList(), notApplied.Distinct(StringComparer.Ordinal).ToList()));
    }

    private static string? TargetColumn(
        DataSourceCatalog catalog, QueryDefinition target, string catalogName, StringComparer comparer)
    {
        var source = target.Source;
        var column = catalog.Schemas
            .SelectMany(s => s.Objects)
            .FirstOrDefault(o => comparer.Equals(o.SchemaName, source.SchemaName) && comparer.Equals(o.Name, source.ObjectName))
            ?.Columns.FirstOrDefault(c => comparer.Equals(c.Name, catalogName));
        if (column is null) return null;

        // A drill step is matched to the query's own outputs by name first, so a target that outputs some other column
        // under this name would be filtered on that one instead. Better unfiltered (and reported) than filtered wrongly.
        var clash = target.Columns.FirstOrDefault(c =>
            c.IsVisible && c.Aggregate == AggregateFunction.None && c.EffectiveAlias == column.Name);
        if (clash is not null && !(comparer.Equals(clash.TableAlias, source.Alias) && comparer.Equals(clash.ColumnName, column.Name)))
            return null;

        return column.Name;
    }

    /// <summary>Applies resolved steps to a saved definition: each becomes an extra AND condition, HAVING and everything
    /// else stay as saved, and the target column may be one the query does not output. Types come from the catalog.</summary>
    internal static async Task<QueryDefinition> ApplyAsync(
        IDataCatalogService catalog, QueryContext context, IReadOnlyList<string> drillColumns, IReadOnlyList<DrillStep> steps,
        CancellationToken cancellationToken)
    {
        if (steps.Count == 0) return context.Definition;

        var comparer = IdentifierMatching.Comparer(context.DataSource.Provider);
        var columns = drillColumns.Concat(steps.Select(s => s.Column)).Distinct(StringComparer.Ordinal).ToList();
        var types = await DrillExecution.DrillColumnTypesAsync(catalog, context, columns, comparer, cancellationToken);
        return DrillDefinitions.ApplySteps(context.Definition, steps, columns, types, comparer.Equals);
    }
}
