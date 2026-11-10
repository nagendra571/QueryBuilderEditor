using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Catalog;

public sealed class DataCatalogService(
    IDataSourceRepository dataSourceRepository,
    IConnectionStringResolver connectionStringResolver,
    IEnumerable<ICatalogReader> readers,
    IMemoryCache cache,
    ILogger<DataCatalogService> logger) : IDataCatalogService
{
    public async Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken)
    {
        // The cache is keyed by id alone and shared by every app, so the app-filtered read must come first: a hit
        // would otherwise serve one app's catalog to another.
        var dataSource = await dataSourceRepository.GetByIdAsync(dataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), dataSourceId);

        var cacheKey = $"catalog:{dataSourceId}";
        if (cache.TryGetValue(cacheKey, out DataSourceCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = await BuildCatalogAsync(dataSource, cancellationToken);

        cache.Set(cacheKey, catalog, TimeSpan.FromMinutes(5));
        return catalog;
    }

    public async Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken)
    {
        // Read fresh, before anything can connect: the cached catalog may predate a turn-off, and another
        // instance would keep serving it (and sending the password) until the cache expired.
        var row = await dataSourceRepository.GetByIdAsync(dataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), dataSourceId);
        if (!row.IsActive)
        {
            throw new CatalogValidationException(DataSourceMessages.TurnedOff);
        }

        var catalog = await GetCatalogAsync(dataSourceId, cancellationToken);

        var comparison = IdentifierMatching.Comparison(catalog.Provider);
        var comparer = IdentifierMatching.Comparer(catalog.Provider);
        // SQL Server keys keep their lower-casing; on Postgres 'Orders' and 'orders' are different objects.
        string ObjectKey(string schema, string objectName) =>
            catalog.Provider == DataSourceProvider.PostgreSql ? $"{schema}.{objectName}" : $"{schema}.{objectName}".ToLowerInvariant();

        var objectsByKey = catalog.Schemas
            .SelectMany(s => s.Objects)
            .ToDictionary(o => ObjectKey(o.SchemaName, o.Name), comparer);

        var aliasMap = new Dictionary<string, SchemaObjectMetadata>(StringComparer.OrdinalIgnoreCase);

        void RegisterAlias(string alias, string schema, string objectName)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                throw new CatalogValidationException("Every table/view reference must have an alias.");
            }
            var key = ObjectKey(schema, objectName);
            if (!objectsByKey.TryGetValue(key, out var metadata))
            {
                throw new CatalogValidationException($"'{schema}.{objectName}' was not found in the data source catalog.");
            }
            // The lookup key joins schema and name, so 'a'+'b.c' would otherwise match 'a.b'+'c'.
            if (!string.Equals(metadata.SchemaName, schema, comparison) || !string.Equals(metadata.Name, objectName, comparison))
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
            if (!metadata.Columns.Any(c => string.Equals(c.Name, columnName, comparison)))
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

        foreach (var parameter in definition.Parameters)
        {
            if (!QueryParameterNames.IsValid(parameter.Name))
            {
                throw new CatalogValidationException(
                    $"'{parameter.Name}' is not a valid parameter name (letters, digits and underscores only, not starting with '{QueryParameterNames.ReservedPrefix}').");
            }
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

        var (usable, _) = await ReadPartitionedAsync(dataSource, cancellationToken);
        return usable.Where(o => InAllowedSchemas(dataSource, o.SchemaName)).ToList();
    }

    public async Task<List<AdminUnavailableObject>> GetUnavailableObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(dataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), dataSourceId);

        if (dataSource.Provider != DataSourceProvider.PostgreSql)
        {
            return [];
        }

        var (_, unavailable) = await ReadPartitionedAsync(dataSource, cancellationToken);
        return unavailable.Where(o => InAllowedSchemas(dataSource, o.SchemaName)).ToList();
    }

    public void InvalidateCatalogCache(Guid dataSourceId) => cache.Remove($"catalog:{dataSourceId}");

    private async Task<DataSourceCatalog> BuildCatalogAsync(DataSource dataSource, CancellationToken cancellationToken)
    {
        var reader = ReaderFor(dataSource.Provider);
        var connectionString = connectionStringResolver.Resolve(dataSource);

        var (usable, unavailable) = await ReadPartitionedAsync(dataSource, cancellationToken);
        var objects = usable.Where(o => Passes(dataSource, o.SchemaName, o.Name, o.Kind)).ToList();

        var columnsByObject = await GuardManaged(dataSource, () => reader.ReadColumnsAsync(connectionString, cancellationToken));
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

        return new DataSourceCatalog
        {
            DataSourceId = dataSource.Id,
            Provider = dataSource.Provider,
            Schemas = schemas,
            UnavailableObjects = unavailable
                .Where(o => Passes(dataSource, o.SchemaName, o.Name, o.Kind))
                .Select(o => new UnavailableObject { SchemaName = o.SchemaName, Name = o.Name, Kind = o.Kind, Reason = o.Reason })
                .ToList()
        };
    }

    // Name clashes are decided on everything the login can read, before any filter: an allowlist
    // that hides one of two clashing objects must not make the other look unique.
    private async Task<(List<SchemaObjectMetadata> Usable, List<AdminUnavailableObject> Unavailable)> ReadPartitionedAsync(
        DataSource dataSource, CancellationToken cancellationToken)
    {
        var reader = ReaderFor(dataSource.Provider);
        var objects = await GuardManaged(dataSource, () => reader.ReadObjectsAsync(connectionStringResolver.Resolve(dataSource), cancellationToken));
        if (dataSource.Provider != DataSourceProvider.PostgreSql)
        {
            return (objects, []);
        }

        var partitioned = AmbiguousObjects.Partition(objects);
        var logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clash in partitioned.Unavailable)
        {
            // One warning per clash group, however many objects are in it.
            if (logged.Add($"{clash.SchemaName}.{clash.Name}"))
            {
                logger.LogWarning(
                    "Data source '{DataSource}': {Object} clashes with {ClashesWith} and is unavailable, because QueryBuilder can't tell the names apart.",
                    dataSource.Name, AmbiguousObjects.Display(clash.SchemaName, clash.Name), string.Join(", ", clash.ClashesWith));
            }
        }

        return partitioned;
    }

    // A managed source's driver errors can name the login and server the admin typed; business users get the same
    // short message the executors give. Sources from host configuration keep their existing behavior.
    private static async Task<T> GuardManaged<T>(DataSource dataSource, Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (dataSource.IsManaged && ex is System.Data.Common.DbException or ArgumentException)
        {
            throw new QueryExecutionException(ConnectionStringResolver.ConnectFailed, ex);
        }
    }

    private ICatalogReader ReaderFor(DataSourceProvider provider) =>
        readers.FirstOrDefault(r => r.Provider == provider)
        ?? throw new CatalogValidationException($"Data source provider '{provider}' is not supported.");

    private static bool InAllowedSchemas(DataSource dataSource, string schemaName) =>
        dataSource.AllowedSchemas.Count == 0 ||
        dataSource.AllowedSchemas.Contains(schemaName, IdentifierMatching.Comparer(dataSource.Provider));

    private static bool Passes(DataSource dataSource, string schemaName, string name, SchemaObjectKind kind)
    {
        if (!InAllowedSchemas(dataSource, schemaName))
        {
            return false;
        }
        if (dataSource.CatalogScope == CatalogScope.Views && kind != SchemaObjectKind.View)
        {
            return false;
        }
        if (dataSource.CatalogScope == CatalogScope.Tables && kind != SchemaObjectKind.Table)
        {
            return false;
        }

        return dataSource.AllowedObjects.Count == 0 ||
               dataSource.AllowedObjects.Contains($"{schemaName}.{name}", IdentifierMatching.Comparer(dataSource.Provider));
    }
}
