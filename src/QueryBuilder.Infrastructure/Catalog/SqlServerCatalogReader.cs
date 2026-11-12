using Microsoft.Data.SqlClient;
using QueryBuilder.Application.Common;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Infrastructure.Catalog;

public sealed class SqlServerCatalogReader : ICatalogReader
{
    private static readonly string[] SystemSchemas = ["sys", "INFORMATION_SCHEMA", "guest", "db_owner", "db_accessadmin",
        "db_securityadmin", "db_ddladmin", "db_backupoperator", "db_datareader", "db_datawriter", "db_denydatareader", "db_denydatawriter"];

    public DataSourceProvider Provider => DataSourceProvider.SqlServer;

    public async Task<List<SchemaObjectMetadata>> ReadObjectsAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var objects = new List<SchemaObjectMetadata>();

        const string objectsSql = """
            SELECT s.name AS SchemaName, t.name AS ObjectName, 'Table' AS Kind
            FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT s.name AS SchemaName, v.name AS ObjectName, 'View' AS Kind
            FROM sys.views v JOIN sys.schemas s ON v.schema_id = s.schema_id
            WHERE v.is_ms_shipped = 0
            ORDER BY SchemaName, ObjectName
            """;

        await using var cmd = new SqlCommand(objectsSql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var schemaName = reader.GetString(0);
            if (SystemSchemas.Contains(schemaName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            objects.Add(new SchemaObjectMetadata
            {
                SchemaName = schemaName,
                Name = reader.GetString(1),
                Kind = reader.GetString(2) == "View" ? SchemaObjectKind.View : SchemaObjectKind.Table
            });
        }

        return objects;
    }

    public async Task<Dictionary<string, List<ColumnMetadata>>> ReadColumnsAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        const string columnsSql = """
            SELECT
                s.name AS SchemaName,
                o.name AS ObjectName,
                c.name AS ColumnName,
                ty.name AS SqlType,
                c.is_nullable AS IsNullable,
                c.column_id AS OrdinalPosition,
                CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
                fk.ReferencedTable AS ForeignKeyTable,
                fk.ReferencedColumn AS ForeignKeyColumn
            FROM sys.columns c
            JOIN sys.objects o ON c.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.index_columns ic
                JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
            LEFT JOIN (
                SELECT
                    fkc.parent_object_id,
                    fkc.parent_column_id,
                    OBJECT_NAME(fkc.referenced_object_id) AS ReferencedTable,
                    rc.name AS ReferencedColumn
                FROM sys.foreign_key_columns fkc
                JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            ) fk ON fk.parent_object_id = c.object_id AND fk.parent_column_id = c.column_id
            WHERE o.is_ms_shipped = 0
            ORDER BY s.name, o.name, c.column_id
            """;

        var columnsByObject = new Dictionary<string, List<ColumnMetadata>>(IdentifierMatching.Comparer(Provider));

        await using var cmd = new SqlCommand(columnsSql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
            var sqlType = reader.GetString(3);

            var column = new ColumnMetadata
            {
                Name = reader.GetString(2),
                SqlType = sqlType,
                DataType = SqlTypeMapper.Map(sqlType),
                IsNullable = reader.GetBoolean(4),
                OrdinalPosition = reader.GetInt32(5),
                IsPrimaryKey = reader.GetInt32(6) == 1,
                IsForeignKey = !reader.IsDBNull(7),
                ForeignKeyTable = reader.IsDBNull(7) ? null : reader.GetString(7),
                ForeignKeyColumn = reader.IsDBNull(8) ? null : reader.GetString(8)
            };

            if (!columnsByObject.TryGetValue(key, out var list))
            {
                list = [];
                columnsByObject[key] = list;
            }
            list.Add(column);
        }

        return columnsByObject;
    }
}
