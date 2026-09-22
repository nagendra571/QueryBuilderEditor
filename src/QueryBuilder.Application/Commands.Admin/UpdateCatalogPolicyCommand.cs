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

        var rawObjects = await catalogService.GetRawObjectsAsync(request.DataSourceId, cancellationToken);
        var kindByKey = rawObjects
            .GroupBy(o => $"{o.SchemaName}.{o.Name}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.OrdinalIgnoreCase);
        var unknown = request.AllowedObjects.Where(key => !kindByKey.ContainsKey(key)).ToList();
        if (unknown.Count > 0)
        {
            throw new CatalogValidationException($"Unknown table/view reference(s): {string.Join(", ", unknown)}.");
        }

        if (request.CatalogScope != CatalogScope.TablesAndViews)
        {
            var requiredKind = request.CatalogScope == CatalogScope.Tables ? SchemaObjectKind.Table : SchemaObjectKind.View;
            var outOfScope = request.AllowedObjects.Where(key => kindByKey[key] != requiredKind).ToList();
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
        repository.Update(dataSource);
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
