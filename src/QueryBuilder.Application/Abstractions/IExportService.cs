namespace QueryBuilder.Application.Abstractions;

public enum ExportFormat
{
    Csv = 0,
    Xlsx = 1
}

public interface IExportService
{
    Task<byte[]> ExportAsync(QueryResultSet resultSet, ExportFormat format, string sheetOrFileName, CancellationToken cancellationToken);

    string GetContentType(ExportFormat format);
    string GetFileExtension(ExportFormat format);
}
