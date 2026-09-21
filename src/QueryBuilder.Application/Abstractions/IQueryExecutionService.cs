using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Abstractions;

public interface IQueryExecutionService
{
    Task<QueryResultSet> ExecuteAsync(
        DataSource dataSource,
        GeneratedQuery generatedQuery,
        IReadOnlyDictionary<string, string?> runtimeParameterValues,
        int? maxRows,
        CancellationToken cancellationToken);
}

public sealed class QueryResultSet
{
    public List<ResultColumn> Columns { get; init; } = [];
    public List<Dictionary<string, object?>> Rows { get; init; } = [];
    public int RowCount => Rows.Count;
    public long ExecutionTimeMs { get; init; }
    public bool Truncated { get; init; }
}

public sealed class ResultColumn
{
    public string Name { get; init; } = string.Empty;
    public ColumnDataType DataType { get; init; }
}
