using System.Text;
using System.Text.Json;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSources;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Drill;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Export;

namespace QueryBuilder.Tests.Drill;

public class DrillQueryCommandTests
{
    private static QueryResultSet DetailResult(bool truncated = false) =>
        VizFixture.Result(
            [new() { ["Region"] = "EU", ["Channel"] = "Web", ["Amount"] = 12.5m, ["OrderId"] = 7m, ["HiddenNote"] = "secret" }],
            truncated,
            ("Region", ColumnDataType.Text), ("Channel", ColumnDataType.Text), ("Amount", ColumnDataType.Number),
            ("OrderId", ColumnDataType.Number), ("HiddenNote", ColumnDataType.Text));

    private static VizFixture Arrange(string user = VizFixture.Owner)
    {
        var f = new VizFixture();
        f.User.UserId = user;
        f.Executor.Result = DetailResult();
        return f;
    }

    private static Task<DrillResultDto> Drill(VizFixture f, DrillStep[]? steps = null, Dictionary<string, string?>? values = null) =>
        new DrillQueryCommandHandler(f.Loader(), new VizFixture.FakeScope(), f.Audit)
            .Handle(new DrillQueryCommand(f.Query.Id, steps?.ToList() ?? [], values ?? []), default);

    private static Task<ExportResult> Export(VizFixture f, DrillStep[]? steps = null) =>
        new ExportDrillQueryCommandHandler(f.Loader(), new ExportService(), new VizFixture.FakeScope(), f.Audit)
            .Handle(new ExportDrillQueryCommand(f.Query.Id, steps?.ToList() ?? [], [], ExportFormat.Csv), default);

    [Fact]
    public async Task Results_drill_returns_rows_of_the_clicked_group()
    {
        var f = Arrange(VizFixture.ViewerUser);
        f.Guard.Predicates = [new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, ["10"])];

        var result = await Drill(f, [new DrillStep("Region", "EU")]);

        Assert.Equal(DrillAction.Rows, result.Action);
        Assert.Equal(["Region", "Channel", "Amount", "OrderId"], result.Columns.Select(c => c.Name));
        Assert.All(result.Rows, r => Assert.DoesNotContain("HiddenNote", r.Keys));
        var step = Assert.Single(f.Guard.ReceivedDefinition!.Filters.Groups.Last().Conditions);
        Assert.Equal(("RegionCode", "EU"), (step.ColumnName, step.Value));
        Assert.Empty(f.Guard.ReceivedDefinition.GroupBy);
        Assert.Equal(1, f.Catalog.ValidateCalls);
        Assert.Same(f.Guard.Predicates, f.Builders.ReceivedScope);
        Assert.Contains("@__scope0", f.Builders.Generated!.Sql);
        Assert.Equal(1000, f.Executor.ReceivedMaxRows);
    }

    [Fact]
    public async Task Parameter_values_go_through_the_binder()
    {
        var f = Arrange();
        var definition = VizFixture.Definition();
        definition.Parameters = [new QueryParameter { Name = "region", DataType = ColumnDataType.Text, DefaultValue = "EU" }];
        definition.Filters.Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = "RegionCode", DataType = ColumnDataType.Text, IsParameterized = true, ParameterName = "region"
        });
        f.Query.DefinitionJson = QueryDefinitionSerializer.Serialize(definition);

        await Drill(f, values: new() { ["region"] = "US" });

        var literals = f.Builders.Generated!.Parameters.Select(p => p.LiteralValue).ToList();
        Assert.Contains("US", literals);
    }

    [Fact]
    public async Task Unaggregated_query_drill_is_refused()
    {
        var f = Arrange();
        var definition = VizFixture.Definition();
        definition.Columns = definition.Columns.Where(c => c.Aggregate == AggregateFunction.None).ToList();
        f.Query.DefinitionJson = QueryDefinitionSerializer.Serialize(definition);

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Drill(f));

        Assert.Equal("This query has no totals to drill into.", ex.Message);
        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Stranger_is_forbidden()
    {
        var f = Arrange(VizFixture.Stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => Drill(f));
        await Assert.ThrowsAsync<ForbiddenException>(() => Export(f));

        Assert.Equal(0, f.Executor.Calls);
        Assert.Equal(0, f.Guard.AuthorizeCalls);
    }

    [Fact]
    public async Task A_hub_viewer_without_a_share_is_forbidden()
    {
        var f = Arrange("hub-viewer");
        f.User.Role = HubRole.Viewer;

        await Assert.ThrowsAsync<ForbiddenException>(() => Drill(f));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Disabled_query_is_refused()
    {
        var f = Arrange();
        f.Query.IsDisabled = true;

        await Assert.ThrowsAsync<QueryDisabledException>(() => Drill(f));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Results_drill_is_audited()
    {
        var f = Arrange(VizFixture.ViewerUser);

        await Drill(f, [new DrillStep("Region", "EU")]);

        var entry = Assert.Single(f.Audit.Entries);
        Assert.Equal(AuditAction.QueryDrilled, entry.Action);
        Assert.Equal(f.Query.Id, entry.EntityId);
        var json = JsonSerializer.Serialize(entry.Details);
        Assert.Contains("\"EU\"", json);
        Assert.Contains("Unrestricted", json);
        Assert.Contains("\"RowCount\":1", json);
        Assert.DoesNotContain("VisualizationId", json);
        Assert.DoesNotContain("DashboardId", json);
    }

    [Fact]
    public async Task Malformed_trail_is_a_bad_request_and_nothing_runs()
    {
        var f = Arrange();

        await Assert.ThrowsAsync<CatalogValidationException>(() => Drill(f, [new DrillStep(" ", "x")]));
        await Assert.ThrowsAsync<CatalogValidationException>(() => Drill(f, Enumerable.Range(0, 9).Select(_ => new DrillStep("Region", "EU")).ToArray()));
        await Assert.ThrowsAsync<CatalogValidationException>(() => Drill(f, [new DrillStep("Amount", "5")]));

        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Rows_over_the_limit_are_flagged_and_export_refuses_them()
    {
        var f = Arrange();
        f.Limits = new RecordLimitSettings(5);
        f.Executor.Result = DetailResult(truncated: true);

        var result = await Drill(f);
        Assert.True(result.Truncated);
        Assert.Equal(5, result.RowLimit);

        await Assert.ThrowsAsync<RecordLimitExceededException>(() => Export(f));
    }

    [Fact]
    public async Task Export_returns_the_drilled_rows_and_audits_the_download()
    {
        var f = Arrange(VizFixture.ViewerUser);

        var result = await Export(f, [new DrillStep("Region", "EU")]);

        var text = Encoding.UTF8.GetString(result.Content).TrimStart('﻿');
        Assert.StartsWith("Region,Channel,Amount,OrderId", text);
        Assert.DoesNotContain("secret", text);
        Assert.StartsWith("Orders by region", result.FileName);
        var entry = Assert.Single(f.Audit.Entries);
        Assert.Equal(AuditAction.QueryExported, entry.Action);
        Assert.Contains("\"Format\":\"Csv\"", JsonSerializer.Serialize(entry.Details));
        Assert.Equal(100_000, f.Executor.ReceivedMaxRows);
    }
}
