using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dashboards;
using QueryBuilder.Application.Drill;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Tests.Hub;

namespace QueryBuilder.Tests.Drill;

public class CrossFilterTests
{
    private static readonly Guid OtherDataSourceId = Guid.NewGuid();

    private sealed class Setup
    {
        public required VizFixture F { get; init; }
        public required QueryVisualization TargetViz { get; init; }
        public required QueryVisualization SourceViz { get; init; }
        public required SavedQuery SourceQuery { get; init; }
        public required Dashboard Dashboard { get; init; }
        public required DashboardTile Target { get; init; }
        public required DashboardTile Source { get; init; }
    }

    private static DataSourceCatalog Catalog() => new()
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
                        Columns =
                        [
                            new ColumnMetadata { Name = "RegionCode", DataType = ColumnDataType.Text },
                            new ColumnMetadata { Name = "Channel", DataType = ColumnDataType.Text },
                            new ColumnMetadata { Name = "Amount", DataType = ColumnDataType.Number },
                            new ColumnMetadata { Name = "Note", DataType = ColumnDataType.Text }
                        ]
                    },
                    new SchemaObjectMetadata
                    {
                        SchemaName = "sales", Name = "vw_Returns",
                        Columns = [new ColumnMetadata { Name = "RegionCode", DataType = ColumnDataType.Text }]
                    },
                    new SchemaObjectMetadata
                    {
                        SchemaName = "sales", Name = "vw_Lonely",
                        Columns = [new ColumnMetadata { Name = "Other", DataType = ColumnDataType.Text }]
                    }
                ]
            }
        ]
    };

    // The source chart is on the same view as the target by default, showing the plain columns Region (RegionCode) and Channel.
    private static Setup Arrange(
        string user = VizFixture.Owner, Action<QueryDefinition>? sourceDefinition = null, Guid? sourceDataSource = null,
        Action<QueryDefinition>? targetDefinition = null, VisualizationOptions? sourceOptions = null, VisualizationOptions? targetOptions = null)
    {
        var f = new VizFixture();
        f.User.UserId = user;
        f.Catalog.Catalog = Catalog();
        if (targetDefinition is not null)
        {
            var d = VizFixture.Definition();
            targetDefinition(d);
            f.Query.DefinitionJson = QueryDefinitionSerializer.Serialize(d);
        }

        var targetViz = f.AddVisualization(VisualizationType.Bar, targetOptions ?? VizFixture.BarOptions());

        var source = VizFixture.Definition();
        sourceDefinition?.Invoke(source);
        var sourceQuery = new SavedQuery
        {
            Name = "Source",
            DataSourceId = sourceDataSource ?? TestData.DataSourceId,
            OwnerId = VizFixture.Owner,
            OwnerName = "Owner",
            DefinitionJson = QueryDefinitionSerializer.Serialize(source)
        };
        var sourceViz = new QueryVisualization
        {
            SavedQueryId = sourceQuery.Id,
            SavedQuery = sourceQuery,
            Name = "Source chart",
            Type = VisualizationType.Bar,
            OptionsJson = VisualizationOptionsSerializer.Serialize(sourceOptions ?? VizFixture.BarOptions())
        };

        var dashboard = new Dashboard { Name = "D", OwnerId = VizFixture.Owner, OwnerName = "Owner" };
        var targetTile = new DashboardTile { DashboardId = dashboard.Id, Kind = DashboardTileKind.Chart, VisualizationId = targetViz.Id, Visualization = targetViz };
        var sourceTile = new DashboardTile { DashboardId = dashboard.Id, Kind = DashboardTileKind.Chart, VisualizationId = sourceViz.Id, Visualization = sourceViz };
        dashboard.Tiles.AddRange([targetTile, sourceTile]);
        f.Dashboards.Items.Add(dashboard);

        f.Executor.Result = VizFixture.Result(
            [new() { ["Region"] = "EU", ["Channel"] = "Web", ["OrderId"] = 7m, ["Amount"] = 12.5m }], false,
            ("Region", ColumnDataType.Text), ("Channel", ColumnDataType.Text), ("OrderId", ColumnDataType.Number), ("Amount", ColumnDataType.Number),
            ("Total", ColumnDataType.Number), ("Zone", ColumnDataType.Text), ("Returns", ColumnDataType.Number), ("Other", ColumnDataType.Text), ("N", ColumnDataType.Number));
        return new Setup { F = f, TargetViz = targetViz, SourceViz = sourceViz, SourceQuery = sourceQuery, Dashboard = dashboard, Target = targetTile, Source = sourceTile };
    }

    private static CrossFilterResolver Resolver(VizFixture f) => new(f.Dashboards, f.DataSources, f.Catalog);

    private static DrillVisualizationCommandHandler DrillHandler(VizFixture f) =>
        new(f.Loader(), f.Catalog, Resolver(f), new VizFixture.FakeScope(), f.Audit);

    private static RenderVisualizationCommandHandler RenderHandler(VizFixture f) =>
        new(f.Loader(), Resolver(f), new VizFixture.FakeScope(), f.Audit);

    private static Task<DrillResultDto> DrillRows(Setup s, params CrossFilterDto[] filters) =>
        DrillHandler(s.F).Handle(new DrillVisualizationCommand(s.F.Query.Id, s.TargetViz.Id,
            new DrillRequest(DrillAction.Rows, null, null, filters.ToList(), null, s.Dashboard.Id, s.Target.Id)), default);

    private static Task<VisualizationDataDto> Render(Setup s, params CrossFilterDto[] filters) =>
        RenderHandler(s.F).Handle(new RenderVisualizationCommand(
            s.F.Query.Id, s.TargetViz.Id, [], s.Dashboard.Id, s.Target.Id, filters.ToList()), default);

    private static IEnumerable<FilterCondition> AllConditions(FilterGroup group) =>
        group.Conditions.Concat(group.Groups.SelectMany(AllConditions));

    [Fact]
    public async Task Drill_on_a_filtered_dashboard_applies_the_cross_filters()
    {
        var s = Arrange();

        await DrillRows(s, new CrossFilterDto(s.Source.Id, "Region", "EU"));

        var seen = s.F.Guard.ReceivedDefinition!;
        var condition = Assert.Single(AllConditions(seen.Filters), c => c.ColumnName == "RegionCode");
        Assert.Equal("EU", condition.Value);
        Assert.Equal(FilterOperator.Equals, condition.Operator);
        Assert.Equal(1, s.F.Guard.AuthorizeCalls);
        var audit = JsonSerializer.Serialize(Assert.Single(s.F.Audit.Entries).Details);
        Assert.Contains("RegionCode", audit);
    }

    [Fact]
    public async Task Render_applies_the_filter_through_the_same_guarded_path_and_reports_it()
    {
        var s = Arrange();
        var predicates = new[] { new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, ["10"]) };
        s.F.Guard.Predicates = predicates;

        var data = await Render(s, new CrossFilterDto(s.Source.Id, "Region", "EU"));

        Assert.Equal(["Region"], data.CrossFilter!.Applied);
        Assert.Empty(data.CrossFilter.NotApplied);
        Assert.Equal(1, s.F.Guard.AuthorizeCalls);
        Assert.Same(predicates, s.F.Builders.ReceivedScope);
        Assert.Contains("@__scope0", s.F.Builders.Generated!.Sql);
        Assert.Contains("[RegionCode]", s.F.Builders.Generated.Sql);
        Assert.Contains("EU", s.F.Builders.Generated.Parameters.Select(p => p.LiteralValue));
    }

    [Fact]
    public async Task Render_without_filters_is_unchanged_and_reports_nothing()
    {
        var s = Arrange();
        await RenderHandler(s.F).Handle(new RenderVisualizationCommand(s.F.Query.Id, s.TargetViz.Id, []), default);
        var plain = s.F.Builders.Generated!.Sql;

        var s2 = Arrange();
        var data = await Render(s2);

        Assert.Null(data.CrossFilter);
        Assert.Equal(plain, s2.F.Builders.Generated!.Sql);
        Assert.DoesNotContain("crossFilter", JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Switch_off_refuses_filters()
    {
        var s = Arrange();
        s.Dashboard.ClickToFilterEnabled = false;

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, new CrossFilterDto(s.Source.Id, "Region", "EU")));

        Assert.Equal("Click-to-filter is turned off for this dashboard.", ex.Message);
        Assert.Equal(0, s.F.Executor.Calls);

        await Render(s);
        Assert.Equal(1, s.F.Executor.Calls);
    }

    [Fact]
    public async Task Filter_from_a_tile_of_another_dashboard_is_refused()
    {
        var s = Arrange();
        var other = Arrange();

        await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, new CrossFilterDto(other.Source.Id, "Region", "EU")));
        await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, new CrossFilterDto(Guid.NewGuid(), "Region", "EU")));

        Assert.Equal(0, s.F.Executor.Calls);
    }

    [Fact]
    public async Task Filter_from_a_text_tile_is_refused()
    {
        var s = Arrange();
        var text = new DashboardTile { DashboardId = s.Dashboard.Id, Kind = DashboardTileKind.Text, Text = "x" };
        s.Dashboard.Tiles.Add(text);

        await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, new CrossFilterDto(text.Id, "Region", "EU")));
    }

    [Fact]
    public async Task Filter_on_a_column_the_source_chart_does_not_show_is_refused()
    {
        var s = Arrange();

        await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, new CrossFilterDto(s.Source.Id, "HiddenNote", "x")));
        await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, new CrossFilterDto(s.Source.Id, "Total", "5")));
        await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, new CrossFilterDto(s.Source.Id, "Note", "x")));

        Assert.Equal(0, s.F.Executor.Calls);
    }

    [Fact]
    public async Task A_drill_column_of_the_source_chart_may_be_filtered()
    {
        var s = Arrange(sourceOptions: new VisualizationOptions { X = "Region", Measures = ["Total"], Drill = new DrillOptions { Columns = ["Note"] } });

        var data = await Render(s, new CrossFilterDto(s.Source.Id, "Note", "n"));

        Assert.Equal(["Note"], data.CrossFilter!.Applied);
    }

    [Fact]
    public async Task Too_many_filters_are_refused()
    {
        var s = Arrange();
        var filters = Enumerable.Range(0, DrillLimits.MaxCrossFilters + 1)
            .Select(_ => new CrossFilterDto(s.Source.Id, "Region", "EU")).ToArray();

        await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, filters));

        Assert.Equal(0, s.F.Executor.Calls);
    }

    [Fact]
    public async Task Filters_without_a_dashboard_context_are_refused()
    {
        var s = Arrange();

        await Assert.ThrowsAsync<CatalogValidationException>(() => RenderHandler(s.F).Handle(
            new RenderVisualizationCommand(s.F.Query.Id, s.TargetViz.Id, [], null, null, [new CrossFilterDto(s.Source.Id, "Region", "EU")]), default));
    }

    [Fact]
    public async Task A_dashboard_the_caller_cannot_see_is_forbidden()
    {
        var s = Arrange(VizFixture.ViewerUser);
        s.Dashboard.OwnerId = "someone-else";

        await Assert.ThrowsAsync<ForbiddenException>(() => Render(s, new CrossFilterDto(s.Source.Id, "Region", "EU")));

        Assert.Equal(0, s.F.Executor.Calls);
    }

    [Fact]
    public async Task A_target_tile_that_is_not_this_chart_is_refused()
    {
        var s = Arrange();

        await Assert.ThrowsAsync<CatalogValidationException>(() => RenderHandler(s.F).Handle(new RenderVisualizationCommand(
            s.F.Query.Id, s.TargetViz.Id, [], s.Dashboard.Id, s.Source.Id, [new CrossFilterDto(s.Source.Id, "Region", "EU")]), default));
    }

    [Fact]
    public async Task A_source_query_the_caller_cannot_view_is_refused_outside_the_hub()
    {
        var s = Arrange(VizFixture.ViewerUser);
        s.Dashboard.OwnerId = VizFixture.ViewerUser;
        s.SourceQuery.OwnerId = "someone-else";

        await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, new CrossFilterDto(s.Source.Id, "Region", "EU")));
    }

    [Fact]
    public async Task Filter_applies_by_catalog_column_name_to_another_query_on_the_same_view_family()
    {
        // The source chart shows RegionCode as "Region"; the target query is on another view of the same data source
        // that also has RegionCode, and does not output it at all.
        var s = Arrange(targetDefinition: d =>
        {
            d.Source = new QuerySource { SchemaName = "sales", ObjectName = "vw_Returns", Alias = "t0" };
            d.Columns = [new QueryColumn { TableAlias = "t0", ColumnName = "RegionCode", Alias = "Zone", DataType = ColumnDataType.Text },
                new QueryColumn { TableAlias = "t0", ColumnName = "RegionCode", Alias = "Returns", Aggregate = AggregateFunction.Count, DataType = ColumnDataType.Number }];
        }, targetOptions: new VisualizationOptions { X = "Zone", Measures = ["Returns"] });

        var data = await Render(s, new CrossFilterDto(s.Source.Id, "Region", "EU"));

        Assert.Equal(["Region"], data.CrossFilter!.Applied);
        var condition = Assert.Single(AllConditions(s.F.Guard.ReceivedDefinition!.Filters));
        Assert.Equal(("RegionCode", "t0", ColumnDataType.Text), (condition.ColumnName, condition.TableAlias, condition.DataType));
    }

    [Fact]
    public async Task Tile_without_the_column_reports_not_applied_and_is_unfiltered()
    {
        var s = Arrange(targetDefinition: d =>
        {
            d.Source = new QuerySource { SchemaName = "sales", ObjectName = "vw_Lonely", Alias = "t0" };
            d.Columns = [new QueryColumn { TableAlias = "t0", ColumnName = "Other", DataType = ColumnDataType.Text },
                new QueryColumn { TableAlias = "t0", ColumnName = "Other", Alias = "N", Aggregate = AggregateFunction.Count, DataType = ColumnDataType.Number }];
        }, targetOptions: new VisualizationOptions { X = "Other", Measures = ["N"] });

        var data = await Render(s, new CrossFilterDto(s.Source.Id, "Region", "EU"));

        Assert.Equal(["Region"], data.CrossFilter!.NotApplied);
        Assert.Empty(data.CrossFilter.Applied);
        Assert.Empty(AllConditions(s.F.Guard.ReceivedDefinition!.Filters));
        Assert.Equal(1, s.F.Executor.Calls);
    }

    [Fact]
    public async Task Other_data_source_reports_not_applied()
    {
        var s = Arrange(sourceDataSource: OtherDataSourceId);

        var data = await Render(s, new CrossFilterDto(s.Source.Id, "Region", "EU"));

        Assert.Equal(["Region"], data.CrossFilter!.NotApplied);
        Assert.Empty(AllConditions(s.F.Guard.ReceivedDefinition!.Filters));
    }

    [Fact]
    public async Task A_target_alias_that_means_another_column_is_not_applied_instead_of_filtering_the_wrong_one()
    {
        var s = Arrange(targetDefinition: d =>
            d.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "Channel", Alias = "RegionCode", DataType = ColumnDataType.Text }));

        var data = await Render(s, new CrossFilterDto(s.Source.Id, "Region", "EU"));

        Assert.Equal(["Region"], data.CrossFilter!.NotApplied);
        Assert.Empty(AllConditions(s.F.Guard.ReceivedDefinition!.Filters));
    }

    [Fact]
    public async Task Having_is_kept_for_cross_filtered_tiles()
    {
        var s = Arrange(targetDefinition: d => d.Having = new FilterGroup
        {
            Conditions = [new FilterCondition { TableAlias = "t0", ColumnName = "Amount", Aggregate = AggregateFunction.Sum, Operator = FilterOperator.GreaterThan, Value = "100", DataType = ColumnDataType.Number }]
        });

        await Render(s, new CrossFilterDto(s.Source.Id, "Region", "EU"));

        var sql = s.F.Builders.Generated!.Sql;
        Assert.Contains("HAVING SUM([t0].[Amount]) >", sql);
        Assert.True(sql.IndexOf("[RegionCode]", StringComparison.Ordinal) < sql.IndexOf("HAVING", StringComparison.Ordinal), sql);
        Assert.NotNull(s.F.Guard.ReceivedDefinition!.Having);
    }

    [Fact]
    public async Task A_null_value_filters_on_is_null()
    {
        var s = Arrange();

        await Render(s, new CrossFilterDto(s.Source.Id, "Region", null));

        var condition = Assert.Single(AllConditions(s.F.Guard.ReceivedDefinition!.Filters));
        Assert.Equal(FilterOperator.IsNull, condition.Operator);
    }

    [Fact]
    public async Task A_bad_value_for_the_target_column_type_is_a_bad_request()
    {
        var s = Arrange(sourceOptions: new VisualizationOptions { X = "Region", Measures = ["Total"], Drill = new DrillOptions { Columns = ["Amount"] } });

        await Assert.ThrowsAsync<CatalogValidationException>(() => Render(s, new CrossFilterDto(s.Source.Id, "Amount", "not a number")));

        Assert.Equal(0, s.F.Executor.Calls);
    }

    [Fact]
    public async Task Cross_filters_and_the_users_own_steps_both_narrow_a_drill()
    {
        var s = Arrange();

        await DrillHandler(s.F).Handle(new DrillVisualizationCommand(s.F.Query.Id, s.TargetViz.Id,
            new DrillRequest(DrillAction.Rows, [new DrillStep("Channel", "Web")], null, [new CrossFilterDto(s.Source.Id, "Region", "EU")], null,
                s.Dashboard.Id, s.Target.Id)), default);

        var columns = AllConditions(s.F.Guard.ReceivedDefinition!.Filters).Select(c => (c.ColumnName, c.Value)).ToList();
        Assert.Contains(("RegionCode", "EU"), columns);
        Assert.Contains(("Channel", "Web"), columns);
    }

    [Fact]
    public async Task Drill_with_the_switch_off_is_refused()
    {
        var s = Arrange();
        s.Dashboard.ClickToFilterEnabled = false;

        await Assert.ThrowsAsync<CatalogValidationException>(() => DrillRows(s, new CrossFilterDto(s.Source.Id, "Region", "EU")));

        Assert.Equal(0, s.F.Executor.Calls);
    }

    [Fact]
    public void New_dashboards_default_to_click_to_filter_on()
    {
        Assert.True(new Dashboard().ClickToFilterEnabled);
    }

    [Fact]
    public async Task Rename_can_turn_the_switch_off_and_bumps_version()
    {
        var f = new DashFixture();
        var handler = new RenameDashboardCommandHandler(f.Dashboards, f.User, f.Audit);

        var off = await handler.Handle(new RenameDashboardCommand(f.Dashboard.Id, "Ops overview", null, 1, false), default);
        Assert.False(off.ClickToFilterEnabled);
        Assert.Equal(2, off.Version);

        var unchanged = await handler.Handle(new RenameDashboardCommand(f.Dashboard.Id, "Ops overview", null, 2), default);
        Assert.False(unchanged.ClickToFilterEnabled);

        var on = await handler.Handle(new RenameDashboardCommand(f.Dashboard.Id, "Ops overview", null, 3, true), default);
        Assert.True(on.ClickToFilterEnabled);
    }

    [Fact]
    public async Task A_viewer_cannot_turn_the_switch_off()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.ViewerUser;

        await Assert.ThrowsAsync<ForbiddenException>(() => new RenameDashboardCommandHandler(f.Dashboards, f.User, f.Audit)
            .Handle(new RenameDashboardCommand(f.Dashboard.Id, "Ops overview", null, 1, false), default));

        Assert.True(f.Dashboard.ClickToFilterEnabled);
    }

    [MetadataSqlFact]
    public Task Migration_adds_the_switch_on_for_existing_dashboards() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        await using (var db = new AppDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261111204157_AddHubRolesAndPublishing");
        }

        var parents = new Dictionary<string, Guid>();
        await AddAppsMigrationTests.InsertSampleRowAsync(cs, "DataSources", parents);
        var dashboard = await AddAppsMigrationTests.InsertSampleRowAsync(cs, "Dashboards", parents);

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ClickToFilterEnabled FROM Dashboards WHERE Id = '{dashboard}'";
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }, migrate: false);
}
