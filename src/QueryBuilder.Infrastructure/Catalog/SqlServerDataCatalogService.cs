using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Catalog;

public sealed class SqlServerDataCatalogService(
    IDataSourceRepository dataSourceRepository,
    IConnectionStringResolver connectionStringResolver,
    IMemoryCache cache) : IDataCatalogService
{
    private static readonly string[] SystemSchemas = ["sys", "INFORMATION_SCHEMA", "guest", "db_owner", "db_accessadmin",
        "db_securityadmin", "db_ddladmin", "db_backupoperator", "db_datareader", "db_datawriter", "db_denydatareader", "db_denydatawriter"];

    public async Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken)
    {
        var cacheKey = $"catalog:{dataSourceId}";
        if (cache.TryGetValue(cacheKey, out DataSourceCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var dataSource = await dataSourceRepository.GetByIdAsync(dataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), dataSourceId);

        var catalog = await BuildCatalogAsync(dataSource, cancellationToken);

        cache.Set(cacheKey, catalog, TimeSpan.FromMinutes(5));
        return catalog;
    }

    public async Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(dataSourceId, cancellationToken);
        var objectsByKey = catalog.Schemas
            .SelectMany(s => s.Objects)
            .ToDictionary(o => $"{o.SchemaName}.{o.Name}".ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

        var aliasMap = new Dictionary<string, SchemaObjectMetadata>(StringComparer.OrdinalIgnoreCase);

        void RegisterAlias(string alias, string schema, string objectName)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                throw new CatalogValidationException("Every table/view reference must have an alias.");
            }
            var key = $"{schema}.{objectName}".ToLowerInvariant();
            if (!objectsByKey.TryGetValue(key, out var metadata))
            {
                throw new CatalogValidationException($"'{schema}.{objectName}' was not found in the data source catalog.");
            }
            if (!aliasMap.TryAdd(alias, metadata))
            {
                throw new CatalogValidationException($"Alias '{alias}' is used more than once.");
            }
        }

        RegisterAlias(definition.Source.Alias, definition.Source.SchemaName, definition.Source.ObjectName);
        foreach (var join in definition.Joins)
        {
            RegisterAlias(join.Alias, join.SchemaName, join.ObjectName);
        }

        void EnsureColumn(string alias, string columnName)
        {
            if (!aliasMap.TryGetValue(alias, out var metadata))
            {
                throw new CatalogValidationException($"Unknown table alias '{alias}' referenced in the query.");
            }
            if (!metadata.Columns.Any(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new CatalogValidationException($"Column '{columnName}' does not exist on '{alias}' ({metadata.SchemaName}.{metadata.Name}).");
            }
        }

        foreach (var join in definition.Joins)
        {
            if (join.Conditions.Count == 0)
            {
                throw new CatalogValidationException($"Join to '{join.Alias}' has no join conditions.");
            }
            foreach (var condition in join.Conditions)
            {
                EnsureColumn(condition.LeftAlias, condition.LeftColumn);
                EnsureColumn(condition.RightAlias, condition.RightColumn);
            }
        }

        foreach (var column in definition.Columns)
        {
            EnsureColumn(column.TableAlias, column.ColumnName);
        }

        foreach (var groupBy in definition.GroupBy)
        {
            EnsureColumn(groupBy.TableAlias, groupBy.ColumnName);
        }

        foreach (var sort in definition.Sorts)
        {
            EnsureColumn(sort.TableAlias, sort.ColumnName);
        }

        ValidateFilterGroup(definition.Filters, EnsureColumn);
        if (definition.Having is not null)
        {
            ValidateFilterGroup(definition.Having, EnsureColumn);
        }

        foreach (var parameterRef in definition.Filters.AllConditions().Concat(definition.Having?.AllConditions() ?? [])
                     .Where(c => c.IsParameterized))
        {
            if (string.IsNullOrWhiteSpace(parameterRef.ParameterName) ||
                !definition.Parameters.Any(p => p.Name == parameterRef.ParameterName))
            {
                throw new CatalogValidationException($"Filter on '{parameterRef.ColumnName}' references an undeclared parameter.");
            }
        }
    }

    private static void ValidateFilterGroup(FilterGroup group, Action<string, string> ensureColumn)
    {
        foreach (var condition in group.Conditions)
        {
            ensureColumn(condition.TableAlias, condition.ColumnName);
        }
        foreach (var nested in group.Groups)
        {
            ValidateFilterGroup(nested, ensureColumn);
        }
    }

    public async Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(dataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), dataSourceId);

        var connectionString = connectionStringResolver.Resolve(dataSource);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await QueryObjectsAsync(connection, dataSource, applyCatalogScopeAndAllowlist: false, cancellationToken);
    }

    public void InvalidateCatalogCache(Guid dataSourceId) => cache.Remove($"catalog:{dataSourceId}");

    private async Task<DataSourceCatalog> BuildCatalogAsync(DataSource dataSource, CancellationToken cancellationToken)
    {
        var connectionString = connectionStringResolver.Resolve(dataSource);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var objects = await QueryObjectsAsync(connection, dataSource, applyCatalogScopeAndAllowlist: true, cancellationToken);

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

        var columnsByObject = new Dictionary<string, List<ColumnMetadata>>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = new SqlCommand(columnsSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var schemaName = reader.GetString(0);
                var objectName = reader.GetString(1);
                var key = $"{schemaName}.{objectName}";
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
        }

        foreach (var obj in objects)
        {
            if (columnsByObject.TryGetValue($"{obj.SchemaName}.{obj.Name}", out var cols))
            {
                obj.Columns = cols;
            }
        }

        var schemas = objects
            .GroupBy(o => o.SchemaName)
            .OrderBy(g => g.Key)
            .Select(g => new SchemaMetadata { Name = g.Key, Objects = g.OrderBy(o => o.Name).ToList() })
            .ToList();

        return new DataSourceCatalog { DataSourceId = dataSource.Id, Schemas = schemas };
    }

    private static async Task<List<SchemaObjectMetadata>> QueryObjectsAsync(
        SqlConnection connection, DataSource dataSource, bool applyCatalogScopeAndAllowlist, CancellationToken cancellationToken)
    {
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
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var schemaName = reader.GetString(0);
            var objectName = reader.GetString(1);
            var kind = reader.GetString(2) == "View" ? SchemaObjectKind.View : SchemaObjectKind.Table;

            if (SystemSchemas.Contains(schemaName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            if (dataSource.AllowedSchemas.Count > 0 &&
                !dataSource.AllowedSchemas.Contains(schemaName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (applyCatalogScopeAndAllowlist)
            {
                if (dataSource.CatalogScope == CatalogScope.Views && kind != SchemaObjectKind.View)
                {
                    continue;
                }
                if (dataSource.CatalogScope == CatalogScope.Tables && kind != SchemaObjectKind.Table)
                {
                    continue;
                }
                if (dataSource.AllowedObjects.Count > 0 &&
                    !dataSource.AllowedObjects.Contains($"{schemaName}.{objectName}", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            objects.Add(new SchemaObjectMetadata { SchemaName = schemaName, Name = objectName, Kind = kind });
        }

        return objects;
    }
}
