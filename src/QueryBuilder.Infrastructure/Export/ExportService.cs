using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using QueryBuilder.Application.Abstractions;

namespace QueryBuilder.Infrastructure.Export;

public sealed class ExportService : IExportService
{
    public Task<byte[]> ExportAsync(QueryResultSet resultSet, ExportFormat format, string sheetOrFileName, CancellationToken cancellationToken) =>
        format switch
        {
            ExportFormat.Csv => Task.FromResult(ExportCsv(resultSet)),
            ExportFormat.Xlsx => Task.FromResult(ExportXlsx(resultSet, sheetOrFileName)),
            _ => throw new NotSupportedException($"Export format '{format}' is not supported.")
        };

    public string GetContentType(ExportFormat format) => format switch
    {
        ExportFormat.Csv => "text/csv",
        ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    public string GetFileExtension(ExportFormat format) => format switch
    {
        ExportFormat.Csv => ".csv",
        ExportFormat.Xlsx => ".xlsx",
        _ => ".bin"
    };

    private static byte[] ExportCsv(QueryResultSet resultSet)
    {
        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var column in resultSet.Columns)
            {
                csv.WriteField(column.Name);
            }
            csv.NextRecord();

            foreach (var row in resultSet.Rows)
            {
                foreach (var column in resultSet.Columns)
                {
                    csv.WriteField(row.GetValueOrDefault(column.Name));
                }
                csv.NextRecord();
            }
        }
        return memoryStream.ToArray();
    }

    private static byte[] ExportXlsx(QueryResultSet resultSet, string sheetName)
    {
        using var workbook = new XLWorkbook();
        var safeSheetName = string.IsNullOrWhiteSpace(sheetName) ? "Results" : sheetName;
        if (safeSheetName.Length > 31)
        {
            safeSheetName = safeSheetName[..31];
        }
        var worksheet = workbook.Worksheets.Add(safeSheetName);

        for (var c = 0; c < resultSet.Columns.Count; c++)
        {
            worksheet.Cell(1, c + 1).Value = resultSet.Columns[c].Name;
            worksheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < resultSet.Rows.Count; r++)
        {
            var row = resultSet.Rows[r];
            for (var c = 0; c < resultSet.Columns.Count; c++)
            {
                var value = row.GetValueOrDefault(resultSet.Columns[c].Name);
                worksheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(value);
            }
        }

        worksheet.Columns().AdjustToContents();

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return memoryStream.ToArray();
    }
}
