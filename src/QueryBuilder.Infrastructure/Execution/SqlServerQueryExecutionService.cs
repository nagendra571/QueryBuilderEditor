using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.SqlClient;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Execution;

public sealed class SqlServerQueryExecutionService(IConnectionStringResolver connectionStringResolver) : IQueryExecutionService
{
    public async Task<QueryResultSet> ExecuteAsync(
        DataSource dataSource,
        GeneratedQuery generatedQuery,
        IReadOnlyDictionary<string, string?> runtimeParameterValues,
        int? maxRows,
        CancellationToken cancellationToken)
    {
        var connectionString = connectionStringResolver.Resolve(dataSource);
        var sql = maxRows is > 0 ? ApplyRowLimit(generatedQuery.Sql, maxRows.Value + 1) : generatedQuery.Sql;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            foreach (var parameter in generatedQuery.Parameters)
            {
                command.Parameters.Add(BuildParameter(parameter, runtimeParameterValues));
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

    private static string ApplyRowLimit(string sql, int rowLimit)
    {
        var selectIndex = sql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
        return sql.Insert(selectIndex + "SELECT".Length, $" TOP ({rowLimit})");
    }

    private static SqlParameter BuildParameter(GeneratedQueryParameter parameter, IReadOnlyDictionary<string, string?> runtimeValues)
    {
        string? rawValue = parameter.LiteralValue;
        if (parameter.IsRuntimeParameter && parameter.RuntimeParameterName is not null &&
            runtimeValues.TryGetValue(parameter.RuntimeParameterName, out var supplied))
        {
            rawValue = supplied;
        }

        var sqlParameter = new SqlParameter(parameter.Name, ConvertValue(parameter.DataType, rawValue));
        return sqlParameter;
    }

    private static object ConvertValue(ColumnDataType dataType, string? rawValue)
    {
        if (string.IsNullOrEmpty(rawValue))
        {
            return DBNull.Value;
        }

        try
        {
            return dataType switch
            {
                ColumnDataType.Number => decimal.Parse(rawValue, CultureInfo.InvariantCulture),
                ColumnDataType.Boolean => ParseBool(rawValue),
                ColumnDataType.Date => DateTime.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None).Date,
                ColumnDataType.DateTime => DateTime.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None),
                ColumnDataType.Guid => Guid.Parse(rawValue),
                _ => rawValue
            };
        }
        catch (FormatException ex)
        {
            throw new QueryExecutionException($"Value '{rawValue}' is not a valid {dataType}.", ex);
        }
    }

    private static bool ParseBool(string value) => value is "1" or "true" or "True" or "TRUE";

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
