using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.DataScoping;

public sealed class DataScopeGuard(
    ICurrentDataScope currentScope,
    IDataScopeRuleRepository ruleRepository,
    IDataCatalogService catalogService)
    : IDataScopeGuard
{
    public async Task<DataSourceCatalog> FilterCatalogAsync(DataSourceCatalog catalog, CancellationToken cancellationToken)
    {
        var scope = await currentScope.GetAsync(cancellationToken);
        if (scope.IsUnrestricted)
        {
            return catalog;
        }

        var rules = await ruleRepository.GetForDataSourceAsync(catalog.DataSourceId, cancellationToken);

        // A new instance, never a mutation — the catalog passed in is the shared cached one.
        return new DataSourceCatalog
        {
            DataSourceId = catalog.DataSourceId,
            Schemas = catalog.Schemas
                .Select(s => new SchemaMetadata
                {
                    Name = s.Name,
                    Objects = s.Objects
                        .Where(o => IsUsable(o, DataScopeEvaluator.Evaluate(Key(o), rules, scope, currentScope.DeclaredKeys)))
                        .ToList()
                })
                .Where(s => s.Objects.Count > 0)
                .ToList()
        };
    }

    public async Task<IReadOnlyList<ScopePredicate>> AuthorizeAsync(
        Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken)
    {
        var scope = await currentScope.GetAsync(cancellationToken);
        if (scope.IsUnrestricted)
        {
            return [];
        }

        var rules = await ruleRepository.GetForDataSourceAsync(dataSourceId, cancellationToken);
        var catalog = await catalogService.GetCatalogAsync(dataSourceId, cancellationToken);
        var objectsByKey = catalog.Schemas
            .SelectMany(s => s.Objects)
            .ToDictionary(Key, StringComparer.OrdinalIgnoreCase);

        var references = new List<(string Alias, string SchemaName, string ObjectName)>
        {
            (definition.Source.Alias, definition.Source.SchemaName, definition.Source.ObjectName)
        };
        references.AddRange(definition.Joins.Select(j => (j.Alias, j.SchemaName, j.ObjectName)));

        var predicates = new List<ScopePredicate>();
        foreach (var (alias, schemaName, objectName) in references)
        {
            var key = $"{schemaName}.{objectName}";
            var access = DataScopeEvaluator.Evaluate(key, rules, scope, currentScope.DeclaredKeys);
            if (!objectsByKey.TryGetValue(key, out var metadata) || !IsUsable(metadata, access))
            {
                throw new ForbiddenException($"You don't have access to '{key}'.");
            }

            foreach (var filter in access.Filters)
            {
                var column = metadata.Columns.First(c => string.Equals(c.Name, filter.ColumnName, StringComparison.OrdinalIgnoreCase));
                predicates.Add(new ScopePredicate(alias, column.Name, column.DataType, filter.Values));
            }
        }

        return predicates;
    }

    // A mapped column that no longer exists on the object (view redefined since the admin mapped
    // it) can't be filtered on, so the object is treated as hidden rather than served unfiltered.
    private static bool IsUsable(SchemaObjectMetadata metadata, ObjectScopeAccess access) =>
        access.IsVisible &&
        access.Filters.All(f => metadata.Columns.Any(c => string.Equals(c.Name, f.ColumnName, StringComparison.OrdinalIgnoreCase)));

    private static string Key(SchemaObjectMetadata o) => $"{o.SchemaName}.{o.Name}";
}
