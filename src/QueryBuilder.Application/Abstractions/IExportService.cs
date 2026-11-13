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

public static class ExportFormats
{
    /// <summary>Only the names "csv" and "xlsx" (any case). <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> would also
    /// take "1" or "7", and an undefined number would fail deep in the export instead of as a bad request.</summary>
    public static bool TryParse(string? text, out ExportFormat format)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "csv":
                format = ExportFormat.Csv;
                return true;
            case "xlsx":
                format = ExportFormat.Xlsx;
                return true;
            default:
                format = default;
                return false;
        }
    }
}

public static class ExportNames
{
    private const int MaxFileNameLength = 100;
    private const int MaxSheetNameLength = 31;
    private static readonly HashSet<string> ReservedFileNames = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(i => new[] { $"COM{i}", $"LPT{i}" })],
        StringComparer.OrdinalIgnoreCase);
    private static readonly char[] SheetNameForbidden = [':', '\\', '/', '?', '*', '[', ']'];

    /// <summary>A file name without extension that every common file system accepts; <paramref name="fallback"/> when
    /// nothing usable is left.</summary>
    public static string FileName(string? name, string fallback)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        var cleaned = new string((name ?? string.Empty).Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (cleaned.Length > MaxFileNameLength)
        {
            cleaned = Cut(cleaned, MaxFileNameLength).TrimEnd('.', ' ');
        }
        if (cleaned.Length == 0)
        {
            return fallback;
        }
        // Windows can't save a file whose name (before any dot) is a device name such as CON or COM1.
        var stem = cleaned.Split('.')[0].TrimEnd(' ');
        return ReservedFileNames.Contains(stem) ? "_" + cleaned : cleaned;
    }

    /// <summary>Excel refuses a sheet name with : \ / ? * [ ], one over 31 characters, one starting or ending with an
    /// apostrophe, and the reserved name "History".</summary>
    public static string SheetName(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Select(c => SheetNameForbidden.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (cleaned.Length > MaxSheetNameLength)
        {
            cleaned = Cut(cleaned, MaxSheetNameLength);
        }
        cleaned = cleaned.Trim('\'', ' ');
        if (cleaned.Length == 0)
        {
            return "Results";
        }
        return string.Equals(cleaned, "History", StringComparison.OrdinalIgnoreCase) ? "History data" : cleaned;
    }

    // Never leave half of a surrogate pair (an emoji, say): the workbook's XML writer throws on it.
    private static string Cut(string text, int length) =>
        char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];
}
