using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataScoping;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Queries.Admin;

public sealed record GetDataScopeQuery(Guid DataSourceId) : IRequest<AdminDataScopeDto>;

public sealed class GetDataScopeQueryHandler(
    IDataSourceRepository dataSourceRepository,
    IDataCatalogService catalogService,
    IDataScopeRuleRepository ruleRepository,
    ICurrentDataScope currentScope)
    : IRequestHandler<GetDataScopeQuery, AdminDataScopeDto>
{
    public async Task<AdminDataScopeDto> Handle(GetDataScopeQuery request, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        var hostKeys = currentScope.HostKeys.ToList();
        // A key the host later declared in code is the host's now; listing it here too would make the client resend it
        // with every key change, which the command refuses as a duplicate of a host key.
        var dataSourceKeys = dataSource.ScopeKeys
            .Where(k => !hostKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var declaredKeys = DataScopeKeys.Effective(hostKeys, dataSourceKeys).ToList();
        if (!currentScope.IsEnabled)
        {
            return new AdminDataScopeDto(false, declaredKeys, hostKeys, dataSourceKeys, [], []);
        }

        // The unfiltered-by-scope catalog (still CatalogScope/AllowedObjects-filtered): exactly the
        // set of objects business users could ever see, which is what needs a scoping decision.
        var catalog = await catalogService.GetCatalogAsync(request.DataSourceId, cancellationToken);
        var rules = await ruleRepository.GetForDataSourceAsync(request.DataSourceId, cancellationToken);

        var comparison = IdentifierMatching.Comparison(catalog.Provider);
        var unavailable = await catalogService.GetUnavailableObjectsAsync(request.DataSourceId, cancellationToken);

        var objects = catalog.Schemas
            .SelectMany(s => s.Objects)
            .Select(o =>
            {
                var key = $"{o.SchemaName}.{o.Name}";
                var objectRules = rules.Where(r => string.Equals(r.ObjectName, key, comparison)).ToList();
                var keyRules = objectRules.Where(r => r.ScopeKey is not null).ToList();
                var state = objectRules.Count == 0 ? DataScopeState.Undecided
                    : keyRules.Count == 0 ? DataScopeState.NotScoped
                    : DataScopeState.Scoped;
                var mappings = keyRules
                    .Where(r => declaredKeys.Contains(r.ScopeKey!, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(
                        r => declaredKeys.First(k => string.Equals(k, r.ScopeKey, StringComparison.OrdinalIgnoreCase)),
                        r => r.ColumnName!);
                var staleKeys = keyRules
                    .Where(r => !declaredKeys.Contains(r.ScopeKey!, StringComparer.OrdinalIgnoreCase))
                    .Select(r => r.ScopeKey!)
                    .ToList();
                return new AdminDataScopeObjectDto(
                    key, o.Kind, o.Columns.Select(c => c.Name).ToList(), state, mappings, staleKeys);
            })
            .ToList();

        return new AdminDataScopeDto(true, declaredKeys, hostKeys, dataSourceKeys, objects, unavailable);
    }
}
