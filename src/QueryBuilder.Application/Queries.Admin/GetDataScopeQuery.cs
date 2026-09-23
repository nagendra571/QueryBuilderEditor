using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
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
        _ = await dataSourceRepository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        var declaredKeys = currentScope.DeclaredKeys.ToList();
        if (!currentScope.IsEnabled)
        {
            return new AdminDataScopeDto(false, declaredKeys, []);
        }

        // The unfiltered-by-scope catalog (still CatalogScope/AllowedObjects-filtered): exactly the
        // set of objects business users could ever see, which is what needs a scoping decision.
        var catalog = await catalogService.GetCatalogAsync(request.DataSourceId, cancellationToken);
        var rules = await ruleRepository.GetForDataSourceAsync(request.DataSourceId, cancellationToken);

        var objects = catalog.Schemas
            .SelectMany(s => s.Objects)
            .Select(o =>
            {
                var key = $"{o.SchemaName}.{o.Name}";
                var objectRules = rules.Where(r => string.Equals(r.ObjectName, key, StringComparison.OrdinalIgnoreCase)).ToList();
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

        return new AdminDataScopeDto(true, declaredKeys, objects);
    }
}
