using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.Admin;

/// <summary>Replaces every data-scope rule of a data source with the submitted decisions.
/// Objects submitted as <see cref="DataScopeState.Undecided"/> (or omitted) end up with no rules.</summary>
public sealed record UpdateDataScopeCommand(Guid DataSourceId, List<DataScopeObjectRuleDto> Objects) : IRequest<Unit>;

public sealed class UpdateDataScopeCommandHandler(
    IDataSourceRepository dataSourceRepository,
    IDataCatalogService catalogService,
    IDataScopeRuleRepository ruleRepository,
    ICurrentDataScope currentScope,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateDataScopeCommand, Unit>
{
    public async Task<Unit> Handle(UpdateDataScopeCommand request, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        if (!currentScope.IsEnabled)
        {
            throw new CatalogValidationException(
                "Row-level data scoping is not enabled — configure options.DataScope in AddQueryBuilderEditor first.");
        }

        var catalog = await catalogService.GetCatalogAsync(request.DataSourceId, cancellationToken);
        var objectsByKey = catalog.Schemas
            .SelectMany(s => s.Objects)
            .ToDictionary(o => $"{o.SchemaName}.{o.Name}", StringComparer.OrdinalIgnoreCase);

        var rules = new List<DataScopeRule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in request.Objects.Where(o => o.State != DataScopeState.Undecided))
        {
            if (!objectsByKey.TryGetValue(entry.ObjectName, out var metadata))
            {
                throw new CatalogValidationException($"'{entry.ObjectName}' is not in this data source's catalog.");
            }
            var objectKey = $"{metadata.SchemaName}.{metadata.Name}";
            if (!seen.Add(objectKey))
            {
                throw new CatalogValidationException($"'{objectKey}' is listed more than once.");
            }

            if (entry.State == DataScopeState.NotScoped)
            {
                if (entry.Mappings is { Count: > 0 })
                {
                    throw new CatalogValidationException($"'{objectKey}' can't be both not scoped and mapped to scope keys.");
                }
                rules.Add(NewRule(objectKey, null, null));
                continue;
            }

            var mappings = entry.Mappings?.Where(m => !string.IsNullOrWhiteSpace(m.Value)).ToList() ?? [];
            if (mappings.Count == 0)
            {
                throw new CatalogValidationException($"'{objectKey}' is scoped but maps no scope key to a column.");
            }
            foreach (var (key, columnName) in mappings)
            {
                var declaredKey = currentScope.DeclaredKeys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    ?? throw new CatalogValidationException($"'{key}' is not a declared data-scope key.");
                var column = metadata.Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new CatalogValidationException($"Column '{columnName}' does not exist on '{objectKey}'.");
                rules.Add(NewRule(objectKey, declaredKey, column.Name));
            }
        }

        await ruleRepository.ReplaceForDataSourceAsync(request.DataSourceId, rules, cancellationToken);

        var scopedCount = rules.Where(r => r.ScopeKey is not null).Select(r => r.ObjectName).Distinct().Count();
        var notScopedCount = rules.Count(r => r.ScopeKey is null);
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.DataSourceDataScopeUpdated,
                nameof(DataSource),
                dataSource.Id,
                dataSource.Name,
                dataSource.Id,
                $"Updated row-level data scope for '{dataSource.Name}' ({scopedCount} scoped, {notScopedCount} not scoped, {objectsByKey.Count - scopedCount - notScopedCount} undecided)",
                rules.Select(r => new { r.ObjectName, r.ScopeKey, r.ColumnName }).ToList()),
            cancellationToken);

        return Unit.Value;

        DataScopeRule NewRule(string objectName, string? scopeKey, string? columnName) => new()
        {
            DataSourceId = request.DataSourceId,
            ObjectName = objectName,
            ScopeKey = scopeKey,
            ColumnName = columnName,
            CreatedBy = currentUser.UserId
        };
    }
}
