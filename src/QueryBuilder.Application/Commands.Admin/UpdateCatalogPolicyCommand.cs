using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.Admin;

public sealed record UpdateCatalogPolicyCommand(Guid DataSourceId, CatalogScope CatalogScope, List<string> AllowedObjects)
    : IRequest<Unit>;

public sealed class UpdateCatalogPolicyCommandHandler(
    IDataSourceRepository repository,
    IDataCatalogService catalogService,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateCatalogPolicyCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCatalogPolicyCommand request, CancellationToken cancellationToken)
    {
        var dataSource = await repository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        var comparer = IdentifierMatching.Comparer(dataSource.Provider);
        var rawObjects = await catalogService.GetRawObjectsAsync(request.DataSourceId, cancellationToken);
        var kindByKey = rawObjects
            .GroupBy(o => $"{o.SchemaName}.{o.Name}", comparer)
            .ToDictionary(g => g.Key, g => g.First().Kind, comparer);

        // An entry that was already allowed and has since become unavailable stays, or saving any
        // other change would be refused; a newly added one is still unknown.
        var unavailableKeys = (await catalogService.GetUnavailableObjectsAsync(request.DataSourceId, cancellationToken))
            .Select(u => $"{u.SchemaName}.{u.Name}")
            .ToHashSet(comparer);
        var keptUnavailable = dataSource.AllowedObjects.Where(unavailableKeys.Contains).ToHashSet(comparer);
        var unknown = request.AllowedObjects
            .Where(key => !kindByKey.ContainsKey(key) && !keptUnavailable.Contains(key))
            .ToList();
        if (unknown.Count > 0)
        {
            throw new CatalogValidationException($"Unknown table/view reference(s): {string.Join(", ", unknown)}.");
        }

        if (request.CatalogScope != CatalogScope.TablesAndViews)
        {
            var requiredKind = request.CatalogScope == CatalogScope.Tables ? SchemaObjectKind.Table : SchemaObjectKind.View;
            var outOfScope = request.AllowedObjects.Where(key => kindByKey.TryGetValue(key, out var kind) && kind != requiredKind).ToList();
            if (outOfScope.Count > 0)
            {
                throw new CatalogValidationException(
                    $"Allowed object(s) out of scope for '{request.CatalogScope}': {string.Join(", ", outOfScope)}.");
            }
        }

        dataSource.CatalogScope = request.CatalogScope;
        dataSource.AllowedObjects = request.AllowedObjects;
        dataSource.UpdatedBy = currentUser.UserId;
        dataSource.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.UpdateOnly(dataSource, nameof(DataSource.CatalogScope), nameof(DataSource.AllowedObjects));
        await repository.SaveChangesAsync(cancellationToken);

        catalogService.InvalidateCatalogCache(request.DataSourceId);

        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.DataSourceCatalogPolicyUpdated,
                nameof(DataSource),
                dataSource.Id,
                dataSource.Name,
                dataSource.Id,
                $"Updated catalog policy for '{dataSource.Name}' (scope: {request.CatalogScope}, {request.AllowedObjects.Count} allowed object(s) explicitly listed)"),
            cancellationToken);

        return Unit.Value;
    }
}
