using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Infrastructure.Execution;

public sealed class PostgresQueryExecutionService(
    IConnectionStringResolver connectionStringResolver,
    ILogger<PostgresQueryExecutionService> logger) : IQueryExecutionService
{
    // Read-only has to be the first statement of the transaction. The date, zone, interval and float settings
    // pin the text the server sends, which PostgresValueParser relies on; they are LOCAL so a pooled
    // connection never carries them to the next user.
    internal const string SessionStatement =
        "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = 60000; SET LOCAL DateStyle = 'ISO, YMD'; " +
        "SET LOCAL TimeZone = 'UTC'; SET LOCAL IntervalStyle = 'postgres'; SET LOCAL extra_float_digits = 3";

    private const string ConnectFailed = "Couldn't connect to this data source.";

    private static readonly PostgresDialect Dialect = new();

    public async Task<QueryResultSet> ExecuteAsync(
        DataSource dataSource,
        GeneratedQuery generatedQuery,
        IReadOnlyDictionary<string, string?> runtimeParameterValues,
        int? maxRows,
        CancellationToken cancellationToken)
    {
        var sql = maxRows is > 0 ? Dialect.ApplyRowLimit(generatedQuery.Sql, maxRows.Value + 1) : generatedQuery.Sql;
        var stopwatch = Stopwatch.StartNew();
        var opened = false;

        try
        {
            var connectionString = connectionStringResolver.Resolve(dataSource);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            opened = true;

            // Never committed: disposing rolls back, so nothing a query does can persist even if read-only were bypassed.
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var session = new NpgsqlCommand(SessionStatement, connection, transaction))
            {
                await session.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand(sql, connection, transaction)
            {
                CommandTimeout = 65,
                AllResultTypesAreUnknown = true
            };
            foreach (var parameter in generatedQuery.Parameters)
            {
                command.Parameters.Add(new NpgsqlParameter(parameter.Name, QueryParameterValues.ResolveForPostgres(parameter, runtimeParameterValues)));
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = new List<ResultColumn>();
            var kinds = new PostgresValueParser.Kind[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var typeName = reader.GetDataTypeName(i);
                kinds[i] = PostgresValueParser.Resolve(typeName);
                columns.Add(new ResultColumn { Name = reader.GetName(i), DataType = PostgresValueParser.ColumnType(typeName) });
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
                for (var i = 0; i < columns.Count; i++)
                {
                    row[columns[i].Name] = reader.IsDBNull(i) ? null : PostgresValueParser.Parse(kinds[i], reader.GetString(i));
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (!opened)
        {
            logger.LogWarning(ex, "Couldn't connect to data source {DataSourceId}.", dataSource.Id);
            throw new QueryExecutionException(ConnectFailed, ex);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.QueryCanceled)
        {
            throw new QueryExecutionException("The query took too long and was stopped.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ReadOnlySqlTransaction)
        {
            throw new QueryExecutionException("This query tried to change data, which isn't allowed.", ex);
        }
        catch (PostgresException ex)
        {
            throw new QueryExecutionException($"The database rejected the query: {ex.MessageText}", ex);
        }
        catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
        {
            throw new QueryExecutionException("The query took too long and was stopped.", ex);
        }
        catch (DbException ex)
        {
            logger.LogWarning(ex, "Query against data source {DataSourceId} failed.", dataSource.Id);
            throw new QueryExecutionException("The query couldn't be completed.", ex);
        }
    }
}
