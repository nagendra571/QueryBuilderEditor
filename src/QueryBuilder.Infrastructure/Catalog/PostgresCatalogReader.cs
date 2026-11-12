using System.Data.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Infrastructure.Catalog;

public sealed class PostgresCatalogReader(ILogger<PostgresCatalogReader> logger) : ICatalogReader
{
    // Shared by both queries so the objects list and the columns can't disagree about what's visible.
    // Privileges are checked by OID, so a name with odd characters can't be mis-resolved.
    private const string VisibleObjects = """
        c.relkind IN ('r','p','f','v','m')
          AND NOT c.relispartition
          AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
          AND has_schema_privilege(n.oid, 'USAGE')
          AND has_table_privilege(c.oid, 'SELECT')
        """;

    private const string ObjectsSql = $"""
        SELECT n.nspname::text, c.relname::text, c.relkind::text
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE {VisibleObjects}
        ORDER BY n.nspname, c.relname
        """;

    private const string ColumnsSql = $"""
        SELECT n.nspname::text, c.relname::text, a.attname::text, a.attnum::int, a.attnotnull,
               base.typname::text, base.typtype::text,
               COALESCE(pk.is_pk, false), fk.ref_table::text, fk.ref_column::text,
               COALESCE(NOT coll.collisdeterministic, false)
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
        JOIN pg_type t ON t.oid = a.atttypid
        LEFT JOIN pg_collation coll ON coll.oid = a.attcollation
        CROSS JOIN LATERAL (
            WITH RECURSIVE b AS (
                SELECT t.oid, t.typname, t.typtype, t.typbasetype
                UNION ALL
                SELECT bt.oid, bt.typname, bt.typtype, bt.typbasetype
                FROM pg_type bt JOIN b ON bt.oid = b.typbasetype WHERE b.typtype = 'd'
            )
            SELECT typname, typtype FROM b WHERE typtype <> 'd' LIMIT 1
        ) base
        LEFT JOIN LATERAL (
            SELECT true AS is_pk FROM pg_constraint pc
            WHERE pc.conrelid = c.oid AND pc.contype = 'p' AND a.attnum = ANY (pc.conkey) LIMIT 1
        ) pk ON true
        LEFT JOIN LATERAL (
            SELECT rc.relname AS ref_table, ra.attname AS ref_column
            FROM pg_constraint fc
            CROSS JOIN LATERAL unnest(fc.conkey, fc.confkey) AS k(col, refcol)
            JOIN pg_class rc ON rc.oid = fc.confrelid
            JOIN pg_attribute ra ON ra.attrelid = fc.confrelid AND ra.attnum = k.refcol
            WHERE fc.conrelid = c.oid AND fc.contype = 'f' AND k.col = a.attnum
            ORDER BY fc.conname LIMIT 1
        ) fk ON true
        WHERE {VisibleObjects}
        ORDER BY n.nspname, c.relname, a.attnum
        """;

    public DataSourceProvider Provider => DataSourceProvider.PostgreSql;

    public Task<List<SchemaObjectMetadata>> ReadObjectsAsync(string connectionString, CancellationToken ct) =>
        RunAsync(connectionString, async connection =>
        {
            await using var command = new NpgsqlCommand(ObjectsSql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);

            var objects = new List<SchemaObjectMetadata>();
            while (await reader.ReadAsync(ct))
            {
                objects.Add(new SchemaObjectMetadata
                {
                    SchemaName = reader.GetString(0),
                    Name = reader.GetString(1),
                    Kind = reader.GetString(2) is "v" or "m" ? SchemaObjectKind.View : SchemaObjectKind.Table
                });
            }

            return objects;
        }, ct);

    public Task<Dictionary<string, List<ColumnMetadata>>> ReadColumnsAsync(string connectionString, CancellationToken ct) =>
        RunAsync(connectionString, async connection =>
        {
            await using var command = new NpgsqlCommand(ColumnsSql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);

            var columnsByObject = new Dictionary<string, List<ColumnMetadata>>(IdentifierMatching.Comparer(Provider));
            while (await reader.ReadAsync(ct))
            {
                var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
                var baseType = reader.GetString(5);

                var column = new ColumnMetadata
                {
                    Name = reader.GetString(2),
                    OrdinalPosition = reader.GetInt32(3),
                    IsNullable = !reader.GetBoolean(4),
                    SqlType = baseType,
                    DataType = PostgresTypeMapper.Map(baseType, reader.GetString(6)),
                    IsPrimaryKey = reader.GetBoolean(7),
                    IsForeignKey = !reader.IsDBNull(8),
                    ForeignKeyTable = reader.IsDBNull(8) ? null : reader.GetString(8),
                    ForeignKeyColumn = reader.IsDBNull(9) ? null : reader.GetString(9),
                    HasNondeterministicCollation = reader.GetBoolean(10)
                };

                if (!columnsByObject.TryGetValue(key, out var list))
                {
                    list = [];
                    columnsByObject[key] = list;
                }
                list.Add(column);
            }

            return columnsByObject;
        }, ct);

    // Driver messages can echo the host and user from the connection string, so before the connection is
    // open they stay in the log only. A malformed string throws from the NpgsqlConnection constructor itself.
    private async Task<T> RunAsync<T>(string connectionString, Func<NpgsqlConnection, Task<T>> read, CancellationToken ct)
    {
        var opened = false;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            opened = true;
            return await read(connection);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PostgresException ex) when (opened)
        {
            logger.LogWarning(ex, "A PostgreSQL data source rejected a catalog query.");
            throw new QueryExecutionException($"The database rejected the query: {ex.MessageText}", ex);
        }
        catch (Exception ex) when (!opened)
        {
            logger.LogWarning(ex, "Couldn't connect to a PostgreSQL data source to read its catalog.");
            throw new QueryExecutionException("Couldn't connect to this data source.");
        }
        catch (DbException ex)
        {
            logger.LogWarning(ex, "A PostgreSQL catalog query failed.");
            throw new QueryExecutionException("The query couldn't be completed.", ex);
        }
    }
}
