using System.Diagnostics;
using Microsoft.Data.SqlClient;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Infrastructure.Execution;

public sealed class SqlServerQueryExecutionService(IConnectionStringResolver connectionStringResolver) : IQueryExecutionService
{
    private static readonly SqlServerDialect Dialect = new();

    public async Task<QueryResultSet> ExecuteAsync(
        DataSource dataSource,
        GeneratedQuery generatedQuery,
        IReadOnlyDictionary<string, string?> runtimeParameterValues,
        int? maxRows,
        CancellationToken cancellationToken)
    {
        var connectionString = connectionStringResolver.Resolve(dataSource);
        var sql = maxRows is > 0 ? Dialect.ApplyRowLimit(generatedQuery.Sql, maxRows.Value + 1) : generatedQuery.Sql;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            foreach (var parameter in generatedQuery.Parameters)
            {
                command.Parameters.Add(new SqlParameter(parameter.Name, QueryParameterValues.Resolve(parameter, runtimeParameterValues)));
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = new List<ResultColumn>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(new ResultColumn { Name = reader.GetName(i), DataType = InferDataType(reader.GetFieldType(i)) });
            }

            var rows = new List<Dictionary<string, object?>>();
            var truncated = false;

            while (await reader.ReadAsync(cancellationToken))
            {
                if (maxRows is > 0 && rows.Count >= maxRows.Value)
                {
                    truncated = true;
                    break;
                }

                var row = new Dictionary<string, object?>(columns.Count);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[columns[i].Name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }

            stopwatch.Stop();

            return new QueryResultSet
            {
                Columns = columns,
                Rows = rows,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                Truncated = truncated
            };
        }
        catch (SqlException ex)
        {
            throw new QueryExecutionException(
                $"The database rejected the query: {ex.Message.Split('\n')[0]}", ex);
        }
    }

    private static ColumnDataType InferDataType(Type clrType) => Type.GetTypeCode(clrType) switch
    {
        TypeCode.Boolean => ColumnDataType.Boolean,
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
            or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal => ColumnDataType.Number,
        TypeCode.DateTime => ColumnDataType.DateTime,
        _ when clrType == typeof(Guid) => ColumnDataType.Guid,
        _ when clrType == typeof(DateTimeOffset) => ColumnDataType.DateTime,
        _ => ColumnDataType.Text
    };
}
