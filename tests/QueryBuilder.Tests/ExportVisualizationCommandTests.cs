using System.Text;
using ClosedXML.Excel;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSources;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Export;

namespace QueryBuilder.Tests;

public class ExportVisualizationCommandTests
{
    private static Dictionary<string, object?> Row(string region, decimal total) =>
        new() { ["Region"] = region, ["Channel"] = "Web", ["Total"] = total, ["Orders"] = 1m, ["HiddenNote"] = "secret" };

    private static VisualizationOptions TableOptions() => new() { Table = new TableOptions { Columns = ["Region", "Total"] } };

    private static (VizFixture Fixture, QueryVisualization Viz) Arrange(
        string user = VizFixture.Owner, VisualizationType type = VisualizationType.Bar, VisualizationOptions? options = null)
    {
        var f = new VizFixture();
        f.User.UserId = user;
        var viz = f.AddVisualization(type, options ?? VizFixture.BarOptions());
        f.Executor.Result = VizFixture.Result([Row("EU", 10), Row("US", 20)]);
        return (f, viz);
    }

    private static Task<ExportResult> Download(VizFixture f, QueryVisualization viz, ExportFormat format = ExportFormat.Csv) =>
        new ExportVisualizationCommandHandler(f.Loader(), new ExportService(), new VizFixture.FakeScope(), f.Audit)
            .Handle(new ExportVisualizationCommand(f.Query.Id, viz.Id, [], format), default);

    private static string[] Lines(ExportResult result) =>
        Encoding.UTF8.GetString(result.Content).TrimStart('﻿').Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task Csv_has_only_the_columns_the_chart_uses()
    {
        var (f, viz) = Arrange();

        var result = await Download(f, viz);

        Assert.Equal(["Region,Total", "EU,10", "US,20"], Lines(result));
        Assert.DoesNotContain("secret", Encoding.UTF8.GetString(result.Content));
        Assert.Equal("text/csv", result.ContentType);
    }

    [Fact]
    public async Task Xlsx_holds_the_chart_rows_on_a_sheet_named_after_a_cleaned_chart_name()
    {
        var (f, viz) = Arrange();
        viz.Name = "Sales: by [region]?";

        var result = await Download(f, viz, ExportFormat.Xlsx);

        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        var sheet = Assert.Single(workbook.Worksheets);
        Assert.Equal("Sales  by  region", sheet.Name);
        Assert.Equal("Region", sheet.Cell(1, 1).GetString());
        Assert.Equal("Total", sheet.Cell(1, 2).GetString());
        Assert.True(sheet.Cell(1, 3).IsEmpty());
        Assert.Equal(20m, (decimal)sheet.Cell(3, 2).GetDouble());
        Assert.Equal("Sales by [region].xlsx", result.FileName);
    }

    [Fact]
    public async Task Stranger_is_forbidden_and_nothing_runs_or_is_audited()
    {
        var (f, viz) = Arrange(VizFixture.Stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => Download(f, viz));

        Assert.Equal(0, f.Executor.Calls);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Viewer_share_can_download_and_the_scope_guard_runs()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser);

        await Download(f, viz);

        Assert.Equal(1, f.Guard.AuthorizeCalls);
        Assert.Equal(1, f.Catalog.ValidateCalls);
    }

    [Fact]
    public async Task Disabled_query_is_refused_as_an_export()
    {
        var (f, viz) = Arrange();
        f.Query.IsDisabled = true;

        var ex = await Assert.ThrowsAsync<QueryDisabledException>(() => Download(f, viz));

        Assert.Contains("cannot be exported", ex.Message);
        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task Download_is_audited_as_an_export_with_format_and_row_count()
    {
        var (f, viz) = Arrange(VizFixture.ViewerUser);

        await Download(f, viz, ExportFormat.Xlsx);

        var entry = Assert.Single(f.Audit.Entries);
        Assert.Equal(AuditAction.QueryExported, entry.Action);
        Assert.Equal(f.Query.Id, entry.EntityId);
        var details = entry.Details!;
        Assert.Equal(viz.Id, details.GetType().GetProperty("VisualizationId")!.GetValue(details));
        Assert.Equal(ExportFormat.Xlsx, details.GetType().GetProperty("Format")!.GetValue(details));
        Assert.Equal(2, details.GetType().GetProperty("RowCount")!.GetValue(details));
        Assert.NotNull(details.GetType().GetProperty("Scope"));
    }

    [Fact]
    public async Task Table_downloads_up_to_the_record_limit_not_the_chart_cap()
    {
        var (f, viz) = Arrange(type: VisualizationType.Table, options: TableOptions());
        f.Limits = new RecordLimitSettings(5_000);

        await Download(f, viz);

        Assert.Equal(5_000, f.Executor.ReceivedMaxRows);
    }

    [Fact]
    public async Task Table_without_a_limit_downloads_up_to_the_legacy_export_cap()
    {
        var (f, viz) = Arrange(type: VisualizationType.Table, options: TableOptions());

        await Download(f, viz);

        Assert.Equal(RecordLimits.LegacyExportRows, f.Executor.ReceivedMaxRows);
    }

    [Fact]
    public async Task Table_over_the_record_limit_is_refused_not_cut_off()
    {
        var (f, viz) = Arrange(type: VisualizationType.Table, options: TableOptions());
        f.Limits = new RecordLimitSettings(2);
        f.Executor.Result = VizFixture.Result([Row("A", 1), Row("B", 2)], truncated: true);

        var ex = await Assert.ThrowsAsync<RecordLimitExceededException>(() => Download(f, viz));

        Assert.Contains("more than 2 records, the record limit", ex.Message);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Table_over_the_legacy_cap_is_refused()
    {
        var (f, viz) = Arrange(type: VisualizationType.Table, options: TableOptions());
        f.Executor.Result = VizFixture.Result([Row("A", 1)], truncated: true);

        var ex = await Assert.ThrowsAsync<RecordLimitExceededException>(() => Download(f, viz));

        Assert.Contains("100,000", ex.Message);
    }

    [Fact]
    public async Task Chart_too_big_to_draw_is_refused_like_drawing_it()
    {
        var (f, viz) = Arrange();
        f.Executor.Result = VizFixture.Result([Row("A", 1)], truncated: true);

        await Assert.ThrowsAsync<RecordLimitExceededException>(() => Download(f, viz));

        Assert.Equal(RecordLimits.ChartMaxRows, f.Executor.ReceivedMaxRows);
    }

    [Theory]
    [InlineData("csv", true, ExportFormat.Csv)]
    [InlineData(" XLSX ", true, ExportFormat.Xlsx)]
    [InlineData("1", false, ExportFormat.Csv)]
    [InlineData("7", false, ExportFormat.Csv)]
    [InlineData("pdf", false, ExportFormat.Csv)]
    [InlineData(null, false, ExportFormat.Csv)]
    public void Only_csv_and_xlsx_names_parse(string? text, bool ok, ExportFormat expected)
    {
        Assert.Equal(ok, ExportFormats.TryParse(text, out var format));
        Assert.Equal(expected, format);
    }

    [Theory]
    [InlineData("Sales / Region: 2026?", "Sales Region 2026")]
    [InlineData("  ...  ", "chart")]
    [InlineData("CON<>|\"*", "_CON")]
    [InlineData("nul.report", "_nul.report")]
    [InlineData("Console", "Console")]
    [InlineData("a\tb", "a b")]
    public void File_names_lose_characters_file_systems_refuse(string name, string expected) =>
        Assert.Equal(expected, ExportNames.FileName(name, "chart"));

    [Fact]
    public void Long_file_names_are_shortened() => Assert.Equal(100, ExportNames.FileName(new string('x', 300), "chart").Length);

    [Fact]
    public void Cutting_a_name_never_splits_an_emoji()
    {
        Assert.Equal(new string('a', 30), ExportNames.SheetName(new string('a', 30) + "\U0001F4CA"));
        Assert.Equal(new string('a', 99), ExportNames.FileName(new string('a', 99) + "\U0001F4CA", "chart"));
    }

    [Fact]
    public async Task Xlsx_of_a_chart_whose_name_ends_in_an_emoji_at_the_cut_is_written()
    {
        var (f, viz) = Arrange();
        viz.Name = new string('a', 30) + "\U0001F4CA";

        var result = await Download(f, viz, ExportFormat.Xlsx);

        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        Assert.Equal(new string('a', 30), Assert.Single(workbook.Worksheets).Name);
    }

    [Fact]
    public async Task Counter_downloads_its_one_row_and_value_column()
    {
        var (f, viz) = Arrange(type: VisualizationType.Counter, options: new VisualizationOptions { Counter = new CounterOptions { Value = "Total" } });

        var result = await Download(f, viz);

        Assert.Equal(["Total", "10"], Lines(result));
    }

    [Theory]
    [InlineData("history", "History data")]
    [InlineData("'quoted'", "quoted")]
    [InlineData("", "Results")]
    [InlineData("[]", "Results")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789", "abcdefghijklmnopqrstuvwxyz01234")]
    public void Sheet_names_follow_excels_rules(string name, string expected) => Assert.Equal(expected, ExportNames.SheetName(name));
}
