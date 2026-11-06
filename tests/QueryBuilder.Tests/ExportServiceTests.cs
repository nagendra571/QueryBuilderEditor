using ClosedXML.Excel;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Export;

namespace QueryBuilder.Tests;

public class ExportServiceTests
{
    private static Task<byte[]> Xlsx(params object?[] values)
    {
        var resultSet = new QueryResultSet
        {
            Columns = [new ResultColumn { Name = "Value", DataType = ColumnDataType.Unknown }],
            Rows = values.Select(v => new Dictionary<string, object?> { ["Value"] = v }).ToList()
        };
        return new ExportService().ExportAsync(resultSet, ExportFormat.Xlsx, "Results", default);
    }

    private static IXLWorksheet Open(byte[] bytes) => new XLWorkbook(new MemoryStream(bytes)).Worksheet(1);

    [Fact]
    public async Task A_normal_date_stays_a_date()
    {
        var sheet = Open(await Xlsx(new DateTime(2024, 3, 5)));

        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 1).DataType);
        Assert.Equal(new DateTime(2024, 3, 5), sheet.Cell(2, 1).GetDateTime());
    }

    [Theory]
    [InlineData(23, 5, 1, "0023-05-01")]
    [InlineData(1, 1, 2, "0001-01-02")]
    [InlineData(99, 12, 31, "0099-12-31")]
    public async Task A_date_excel_cannot_hold_is_written_as_iso_text(int year, int month, int day, string expected)
    {
        var sheet = Open(await Xlsx(new DateTime(year, month, day)));

        Assert.Equal(XLDataType.Text, sheet.Cell(2, 1).DataType);
        Assert.Equal(expected, sheet.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task Text_over_the_excel_cell_limit_refuses_the_export_naming_the_row()
    {
        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() => Xlsx("ok", new string('x', 32768)));

        Assert.Equal("Row 2, column 'Value' is too long for Excel. Export as CSV instead.", ex.Message);
    }

    [Fact]
    public async Task Text_at_the_excel_cell_limit_is_written_and_csv_never_refuses()
    {
        var sheet = Open(await Xlsx(new string('x', 32767)));
        Assert.Equal(32767, sheet.Cell(2, 1).GetString().Length);

        var resultSet = new QueryResultSet
        {
            Columns = [new ResultColumn { Name = "Value" }],
            Rows = [new Dictionary<string, object?> { ["Value"] = new string('x', 40000) }]
        };
        var csv = await new ExportService().ExportAsync(resultSet, ExportFormat.Csv, "r", default);
        Assert.True(csv.Length > 40000);
    }
}
