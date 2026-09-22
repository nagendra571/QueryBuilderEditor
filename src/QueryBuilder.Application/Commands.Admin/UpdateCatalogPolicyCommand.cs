using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.Admin;

public sealed record UpdateCatalogPolicyCommand(Guid DataSourceId, CatalogScope CatalogScope, List<string> AllowedObjects)
    : IRequest<Unit>;

public sealed class UpdateCatalogPolicyCommandHandler(
    IDataSourceRepository repository, IDataCatalogService catalogService, IAuditLogger auditLogger)
    : IRequestHandler<UpdateCatalogPolicyCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCatalogPolicyCommand request, CancellationToken cancellationToken)
    {
        var dataSource = await repository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        var rawObjects = await catalogService.GetRawObjectsAsync(request.DataSourceId, cancellationToken);
        var validKeys = rawObjects.Select(o => $"{o.SchemaName}.{o.Name}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = request.AllowedObjects.Where(key => !validKeys.Contains(key)).ToList();
        if (unknown.Count > 0)
        {
            throw new CatalogValidationException($"Unknown table/view reference(s): {string.Join(", ", unknown)}.");
        }

        dataSource.CatalogScope = request.CatalogScope;
        dataSource.AllowedObjects = request.AllowedObjects;
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
