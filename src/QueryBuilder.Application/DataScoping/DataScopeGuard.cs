using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.DataScoping;

public sealed class DataScopeGuard(
    ICurrentDataScope currentScope,
    IDataSourceRepository dataSourceRepository,
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

        var keys = await EffectiveKeysAsync(catalog.DataSourceId, cancellationToken);
        var rules = await ruleRepository.GetForDataSourceAsync(catalog.DataSourceId, cancellationToken);

        var comparison = IdentifierMatching.Comparison(catalog.Provider);

        // A new instance, never a mutation — the catalog passed in is the shared cached one.
        // UnavailableObjects stays empty: listing names a scoped user has no rule for would reveal them.
        return new DataSourceCatalog
        {
            DataSourceId = catalog.DataSourceId,
            Provider = catalog.Provider,
            Schemas = catalog.Schemas
                .Select(s => new SchemaMetadata
                {
                    Name = s.Name,
                    Objects = s.Objects
                        .Where(o => IsUsable(o, DataScopeEvaluator.Evaluate(Key(o), rules, scope, keys, comparison), comparison))
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

        var keys = await EffectiveKeysAsync(dataSourceId, cancellationToken);
        var rules = await ruleRepository.GetForDataSourceAsync(dataSourceId, cancellationToken);
        var catalog = await catalogService.GetCatalogAsync(dataSourceId, cancellationToken);
        var comparison = IdentifierMatching.Comparison(catalog.Provider);
        var objectsByKey = catalog.Schemas
            .SelectMany(s => s.Objects)
            .ToDictionary(Key, IdentifierMatching.Comparer(catalog.Provider));

        var references = new List<(string Alias, string SchemaName, string ObjectName)>
        {
            (definition.Source.Alias, definition.Source.SchemaName, definition.Source.ObjectName)
        };
        references.AddRange(definition.Joins.Select(j => (j.Alias, j.SchemaName, j.ObjectName)));

        var predicates = new List<ScopePredicate>();
        foreach (var (alias, schemaName, objectName) in references)
        {
            var key = $"{schemaName}.{objectName}";
            var access = DataScopeEvaluator.Evaluate(key, rules, scope, keys, comparison);
            if (!objectsByKey.TryGetValue(key, out var metadata) || !IsUsable(metadata, access, comparison))
            {
                throw new ForbiddenException($"You don't have access to '{key}'.");
            }

            foreach (var filter in access.Filters)
            {
                var column = metadata.Columns.First(c => string.Equals(c.Name, filter.ColumnName, comparison));
                predicates.Add(new ScopePredicate(alias, column.Name, column.DataType, filter.Values));
            }
        }

        return predicates;
    }

    // Read from the row on every call, never from the cached catalog, so an admin's key change applies at once.
    // A missing row throws: guessing "host keys only" would be a decision about a data source we can't see.
    private async Task<IReadOnlyList<string>> EffectiveKeysAsync(Guid dataSourceId, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(dataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), dataSourceId);
        return DataScopeKeys.Effective(currentScope.HostKeys, dataSource);
    }

    // A mapped column that no longer exists on the object (view redefined since the admin mapped
    // it) can't be filtered on, so the object is treated as hidden rather than served unfiltered.
    // A column with a nondeterministic collation can match values that differ by case or accent, so
    // it can't be an exact scope filter either.
    private static bool IsUsable(SchemaObjectMetadata metadata, ObjectScopeAccess access, StringComparison comparison) =>
        access.IsVisible &&
        access.Filters.All(f => metadata.Columns.Any(c =>
            string.Equals(c.Name, f.ColumnName, comparison) && !c.HasNondeterministicCollation));

    private static string Key(SchemaObjectMetadata o) => $"{o.SchemaName}.{o.Name}";
}
