using System.Text;
using System.Text.Json;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dashboards;
using QueryBuilder.Application.DataSources;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Drill;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Export;

namespace QueryBuilder.Tests.Drill;

public class DrillVisualizationCommandTests
{
    private const string HubViewer = "hub-viewer";

    private static Dictionary<string, object?> DetailRow(string region = "EU", string channel = "Web") =>
        new() { ["Region"] = region, ["Channel"] = channel, ["OrderId"] = 7m, ["Amount"] = 12.5m, ["HiddenNote"] = "secret" };

    private static QueryResultSet DetailResult(bool truncated = false) =>
        VizFixture.Result([DetailRow()], truncated,
            ("Region", ColumnDataType.Text), ("Channel", ColumnDataType.Text), ("OrderId", ColumnDataType.Number),
            ("Amount", ColumnDataType.Number), ("HiddenNote", ColumnDataType.Text));

    private static QueryResultSet BreakdownResult(bool truncated = false) =>
        VizFixture.Result(
            [new() { ["Channel"] = "Web", ["Total"] = 10m, ["HiddenNote"] = "secret" }, new() { ["Channel"] = "Shop", ["Total"] = 5m }],
            truncated, ("Channel", ColumnDataType.Text), ("Total", ColumnDataType.Number), ("HiddenNote", ColumnDataType.Text));

    private static DrillRequest Rows(
        DrillStep[]? steps = null, Guid? dashboardId = null, Guid? tileId = null, Dictionary<string, string?>? values = null,
        List<CrossFilterDto>? crossFilters = null) =>
        new(DrillAction.Rows, steps?.ToList(), null, crossFilters, values, dashboardId, tileId);

    private static DrillRequest Breakdown(string column, params DrillStep[] steps) =>
        new(DrillAction.Breakdown, steps.ToList(), column, null, null, null, null);

    private static DrillVisualizationCommandHandler Handler(VizFixture f) => new(f.Loader(), f.Catalog, new VizFixture.FakeScope(), f.Audit);

    private static Task<DrillResultDto> Drill(VizFixture f, QueryVisualization viz, DrillRequest request) =>
        Handler(f).Handle(new DrillVisualizationCommand(f.Query.Id, viz.Id, request), default);

    private static Task<ExportResult> Export(VizFixture f, QueryVisualization viz, DrillRequest request, ExportFormat format = ExportFormat.Csv) =>
        new ExportDrillCommandHandler(f.Loader(), f.Catalog, new ExportService(), new VizFixture.FakeScope(), f.Audit)
            .Handle(new ExportDrillCommand(f.Query.Id, viz.Id, request, format), default);

    private static (VizFixture Fixture, QueryVisualization Viz) Arrange(
        string user = VizFixture.Owner, VisualizationOptions? options = null, VisualizationType type = VisualizationType.Bar)
    {
        var f = new VizFixture();
        f.User.UserId = user;
        var viz = f.AddVisualization(type, options ?? VizFixture.BarOptions());
        f.Executor.Result = DetailResult();
        return (f, viz);
    }

    private static (Guid Dashboard, Guid Tile) Publish(VizFixture f, QueryVisualization viz, string? mappingsJson = null)
    {
        var dashboard = new Dashboard { Name = "D", OwnerId = "someone-else", PublishedToApp = true };
        var tile = new DashboardTile
        {
            DashboardId = dashboard.Id, Kind = DashboardTileKind.Chart, VisualizationId = viz.Id, Visualization = viz,
            ParameterMappingsJson = mappingsJson
        };
        dashboard.Tiles.Add(tile);
        f.Dashboards.Items.Add(dashboard);
        return (dashboard.Id, tile.Id);
    }

    private static DataSourceCatalog CatalogWithAmount() => new()
    {
        Schemas =
        [
            new SchemaMetadata
            {
                Name = "sales",
                Objects =
                [
                    new SchemaObjectMetadata
                    {
                        SchemaName = "sales", Name = "vw_Orders",
                        Columns = [new ColumnMetadata { Name = "Amount", DataType = ColumnDataType.Number }]
                    }
                ]
            }
        ]
    };

    [Fact]
    public async Task Stranger_is_forbidden_and_nothing_runs()
    {
        var (f, viz) = Arrange(VizFixture.Stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => Drill(f, viz, Rows()));

        Assert.Equal(0, f.Executor.Calls);
        Assert.Equal(0, f.Guard.AuthorizeCalls);
    }

    [Fact]
    public async Task Viewer_share_can_drill()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser);

        var result = await Drill(f, viz, Rows([new DrillStep("Region", "EU")]));

        Assert.Equal(DrillAction.Rows, result.Action);
        Assert.Equal(1, result.RowCount);
    }

    [Fact]
    public async Task Disabled_query_is_refused()
    {
        var (f, viz) = Arrange();
        f.Query.IsDisabled = true;

        await Assert.ThrowsAsync<QueryDisabledException>(() => Drill(f, viz, Rows()));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Viewer_without_query_access_drills_through_a_dashboard_grant()
    {
        var (f, viz) = Arrange(HubViewer);
        f.User.Role = HubRole.Viewer;
        var (dashboard, tile) = Publish(f, viz);

        await Assert.ThrowsAsync<ForbiddenException>(() => Drill(f, viz, Rows()));
        var result = await Drill(f, viz, Rows(dashboardId: dashboard, tileId: tile));

        Assert.Equal(1, result.RowCount);
        Assert.Equal(1, f.Guard.AuthorizeCalls);
    }

    [Fact]
    public async Task Viewer_with_a_dashboard_that_does_not_contain_the_tile_is_forbidden()
    {
        var (f, viz) = Arrange(HubViewer);
        f.User.Role = HubRole.Viewer;
        var (dashboard, _) = Publish(f, viz);
        var otherViz = f.AddVisualization(VisualizationType.Bar, VizFixture.BarOptions());
        var (_, otherTile) = Publish(f, otherViz);

        await Assert.ThrowsAsync<ForbiddenException>(() => Drill(f, viz, Rows(dashboardId: dashboard, tileId: Guid.NewGuid())));
        await Assert.ThrowsAsync<ForbiddenException>(() => Drill(f, viz, Rows(dashboardId: dashboard, tileId: otherTile)));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Deleted_visualization_is_not_found()
    {
        var (f, viz) = Arrange();
        f.Visualizations.Items.Remove(viz);

        await Assert.ThrowsAsync<NotFoundException>(() => Drill(f, viz, Rows()));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Viewer_drilling_a_tile_removed_from_the_dashboard_is_forbidden()
    {
        var (f, viz) = Arrange(HubViewer);
        f.User.Role = HubRole.Viewer;
        var (dashboard, tile) = Publish(f, viz);
        f.Dashboards.Items.Single().Tiles.Clear();

        await Assert.ThrowsAsync<ForbiddenException>(() => Drill(f, viz, Rows(dashboardId: dashboard, tileId: tile)));

        Assert.Equal(0, f.Executor.Calls);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Context_values_are_a_copy_that_cannot_change_after_resolving()
    {
        var (f, viz) = Arrange();
        var values = new Dictionary<string, string?> { ["region"] = "EU" };

        var context = await f.Loader().ResolveAsync(f.Query.Id, viz.Id, values, null, null, VisualizationDataPurpose.Drill, default);
        values["region"] = "US";
        values["other"] = "x";

        IReadOnlyDictionary<string, string?> seen = context.Values;
        Assert.Equal("EU", seen["region"]);
        Assert.False(seen.ContainsKey("other"));
    }

    [Fact]
    public async Task Viewer_parameter_values_are_constrained_like_drawing()
    {
        var (f, viz) = Arrange(HubViewer);
        f.User.Role = HubRole.Viewer;
        var definition = VizFixture.Definition();
        definition.Parameters = [new QueryParameter { Name = "region", DataType = ColumnDataType.Text, DefaultValue = "EU" }];
        definition.Filters.Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = "RegionCode", DataType = ColumnDataType.Text, IsParameterized = true, ParameterName = "region"
        });
        f.Query.DefinitionJson = QueryDefinitionSerializer.Serialize(definition);
        var mappings = DashboardParameterStore.WriteMappings(new Dictionary<string, ParameterMappingDto>
        {
            ["region"] = new(ParameterMappingSource.Fixed, null, "EU", null)
        });
        var (dashboard, tile) = Publish(f, viz, mappings);

        await Drill(f, viz, Rows(dashboardId: dashboard, tileId: tile, values: new() { ["region"] = "US" }));

        var literals = f.Builders.Generated!.Parameters.Select(p => p.LiteralValue).ToList();
        Assert.Contains("EU", literals);
        Assert.DoesNotContain("US", literals);
    }

    [Fact]
    public async Task Rows_result_has_only_the_allowed_columns()
    {
        var (f, viz) = Arrange();

        var result = await Drill(f, viz, Rows());

        Assert.Equal(["Region", "Channel", "Amount", "OrderId"], result.Columns.Select(c => c.Name));
        Assert.All(result.Rows, r =>
        {
            Assert.Equal(["Amount", "Channel", "OrderId", "Region"], r.Keys.Order());
            Assert.DoesNotContain("HiddenNote", r.Keys);
        });
        Assert.Null(result.Options);
    }

    [Fact]
    public async Task Rows_are_capped_at_the_legacy_grid_size_or_the_record_limit_and_flagged()
    {
        var (f, viz) = Arrange();

        await Drill(f, viz, Rows());
        Assert.Equal(1000, f.Executor.ReceivedMaxRows);

        f.Limits = new RecordLimitSettings(5);
        f.Executor.Result = DetailResult(truncated: true);
        var result = await Drill(f, viz, Rows());

        Assert.Equal(5, f.Executor.ReceivedMaxRows);
        Assert.True(result.Truncated);
        Assert.Equal(5, result.RowLimit);
    }

    [Fact]
    public async Task Guard_receives_the_drilled_definition()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser);
        var predicates = new[] { new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, ["10"]) };
        f.Guard.Predicates = predicates;

        await Drill(f, viz, Rows([new DrillStep("Region", "EU")]));

        Assert.Equal(1, f.Guard.AuthorizeCalls);
        Assert.Equal(1, f.Catalog.ValidateCalls);
        var validated = Assert.Single(f.Catalog.LastValidated!.Filters.Groups.Last().Conditions);
        Assert.Equal(("RegionCode", "EU"), (validated.ColumnName, validated.Value));
        var seen = f.Guard.ReceivedDefinition!;
        var step = Assert.Single(seen.Filters.Groups.Last().Conditions);
        Assert.Equal(("RegionCode", "EU"), (step.ColumnName, step.Value));
        Assert.Same(predicates, f.Builders.ReceivedScope);
        Assert.Contains("@__scope0", f.Builders.Generated!.Sql);
        Assert.Empty(seen.GroupBy);
    }

    [Fact]
    public async Task Breakdown_returns_rewritten_options_and_refuses_truncated_data()
    {
        var (f, viz) = Arrange();
        f.Executor.Result = BreakdownResult();

        var result = await Drill(f, viz, Breakdown("Channel", new DrillStep("Region", "EU")));

        Assert.Equal(DrillAction.Breakdown, result.Action);
        Assert.Equal(VisualizationType.Bar, result.Type);
        Assert.Equal("Channel", result.Options!.X);
        Assert.Equal(["Channel", "Total"], result.Columns.Select(c => c.Name));
        Assert.All(result.Rows, r => Assert.DoesNotContain("HiddenNote", r.Keys));
        Assert.Equal(1000, f.Executor.ReceivedMaxRows);

        f.Executor.Result = BreakdownResult(truncated: true);
        await Assert.ThrowsAsync<RecordLimitExceededException>(() => Drill(f, viz, Breakdown("Channel")));
    }

    [Fact]
    public async Task Breakdown_of_a_chart_type_that_cannot_break_down_is_a_bad_request()
    {
        var (f, viz) = Arrange(type: VisualizationType.Table, options: new VisualizationOptions { Table = new TableOptions { Columns = ["Region"] } });

        await Assert.ThrowsAsync<CatalogValidationException>(() => Drill(f, viz, Breakdown("Channel")));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Drill_is_audited_with_steps_and_scope()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser);

        await Drill(f, viz, Rows([new DrillStep("Region", "EU")]));

        var entry = Assert.Single(f.Audit.Entries);
        Assert.Equal(AuditAction.QueryDrilled, entry.Action);
        Assert.Equal(f.Query.Id, entry.EntityId);
        var json = JsonSerializer.Serialize(entry.Details);
        Assert.Contains(viz.Id.ToString(), json);
        Assert.Contains("\"Region\"", json);
        Assert.Contains("\"EU\"", json);
        Assert.Contains("Unrestricted", json);
        Assert.Contains("\"RowCount\":1", json);
    }

    [Fact]
    public async Task Dashboard_ids_are_audited_only_when_the_grant_let_the_request_in()
    {
        var (f, viz) = Arrange(HubViewer);
        f.User.Role = HubRole.Viewer;
        var (dashboard, tile) = Publish(f, viz);
        await Drill(f, viz, Rows(dashboardId: dashboard, tileId: tile));
        var granted = JsonSerializer.Serialize(Assert.Single(f.Audit.Entries).Details);
        Assert.Contains(dashboard.ToString(), granted);
        Assert.Contains(tile.ToString(), granted);

        var (owned, ownedViz) = Arrange();
        var (d2, t2) = Publish(owned, ownedViz);
        await Drill(owned, ownedViz, Rows(dashboardId: d2, tileId: t2));
        var own = JsonSerializer.Serialize(Assert.Single(owned.Audit.Entries).Details);
        Assert.DoesNotContain("DashboardId", own);
        Assert.DoesNotContain("TileId", own);
        Assert.DoesNotContain(d2.ToString(), own);
    }

    [Fact]
    public async Task Audited_step_values_are_cut_to_200_characters_and_a_drill_has_no_format()
    {
        var (f, viz) = Arrange();

        await Drill(f, viz, Rows([new DrillStep("Region", new string('x', 300))]));

        var entry = Assert.Single(f.Audit.Entries);
        var json = JsonSerializer.Serialize(entry.Details);
        Assert.Contains(new string('x', 200), json);
        Assert.DoesNotContain(new string('x', 201), json);
        Assert.DoesNotContain("Format", json);
    }

    [Fact]
    public async Task Drill_columns_come_only_from_the_chart_options_never_the_request()
    {
        var (f, viz) = Arrange();

        await Assert.ThrowsAsync<CatalogValidationException>(() => Drill(f, viz, Rows([new DrillStep("Amount", "5")])));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task A_drill_column_step_is_typed_from_the_catalog_and_a_malformed_value_is_a_bad_request()
    {
        var options = VizFixture.BarOptions();
        options.Drill = new DrillOptions { Columns = ["Amount"] };
        var (f, viz) = Arrange(options: options);
        f.Catalog.Catalog = CatalogWithAmount();

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Drill(f, viz, Rows([new DrillStep("Amount", "abc")])));
        Assert.Contains("Amount", ex.Message);
        Assert.Equal(0, f.Executor.Calls);

        await Drill(f, viz, Rows([new DrillStep("Amount", "12.5")]));
        var step = f.Guard.ReceivedDefinition!.Filters.Groups.Last().Conditions.Single();
        Assert.Equal(ColumnDataType.Number, step.DataType);
    }

    [Theory]
    [InlineData("nullStep")]
    [InlineData("blank")]
    [InlineData("crossFilter")]
    [InlineData("tooMany")]
    [InlineData("badAction")]
    public async Task Malformed_requests_are_bad_requests_and_nothing_runs(string kind)
    {
        var (f, viz) = Arrange();
        var request = kind switch
        {
            "nullStep" => new DrillRequest(DrillAction.Rows, [null!], null, null, null, null, null),
            "blank" => Rows([new DrillStep(" ", "x")]),
            "crossFilter" => Rows(crossFilters: [new CrossFilterDto(Guid.NewGuid(), "Region", "EU")]),
            "tooMany" => Rows(Enumerable.Range(0, 9).Select(_ => new DrillStep("Region", "EU")).ToArray()),
            _ => new DrillRequest((DrillAction)99, null, null, null, null, null, null)
        };

        await Assert.ThrowsAsync<CatalogValidationException>(() => Drill(f, viz, request));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task A_missing_steps_list_means_no_steps()
    {
        var (f, viz) = Arrange();

        var result = await Drill(f, viz, new DrillRequest(DrillAction.Rows, null, null, null, null, null, null));

        Assert.Equal(1, result.RowCount);
    }

    [Fact]
    public async Task Breakdown_without_a_column_is_a_bad_request()
    {
        var (f, viz) = Arrange();

        await Assert.ThrowsAsync<CatalogValidationException>(() => Drill(f, viz, Breakdown(" ")));
    }

    [Fact]
    public async Task Export_returns_the_drilled_rows_and_audits_the_download()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser);

        var result = await Export(f, viz, Rows([new DrillStep("Region", "EU")]));

        var text = Encoding.UTF8.GetString(result.Content).TrimStart('﻿');
        Assert.StartsWith("Region,Channel,Amount,OrderId", text);
        Assert.DoesNotContain("secret", text);
        Assert.Equal("text/csv", result.ContentType);
        var entry = Assert.Single(f.Audit.Entries);
        Assert.Equal(AuditAction.QueryExported, entry.Action);
        var json = JsonSerializer.Serialize(entry.Details);
        Assert.Contains("\"Format\"", json);
        Assert.Contains("\"EU\"", json);
        Assert.Equal(100_000, f.Executor.ReceivedMaxRows);
    }

    [Fact]
    public async Task Drill_export_over_the_limit_is_refused_not_truncated()
    {
        var (f, viz) = Arrange();
        f.Limits = new RecordLimitSettings(5);
        f.Executor.Result = DetailResult(truncated: true);

        await Assert.ThrowsAsync<RecordLimitExceededException>(() => Export(f, viz, Rows()));

        Assert.Equal(5, f.Executor.ReceivedMaxRows);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Breakdown_cannot_be_exported()
    {
        var (f, viz) = Arrange();

        await Assert.ThrowsAsync<CatalogValidationException>(() => Export(f, viz, Breakdown("Channel")));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Export_of_a_stranger_is_forbidden()
    {
        var (f, viz) = Arrange(VizFixture.Stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => Export(f, viz, Rows()));

        Assert.Equal(0, f.Executor.Calls);
    }
}
