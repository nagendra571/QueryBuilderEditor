using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataScoping;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.Admin;

/// <summary>Replaces the scope keys an admin added to one data source. Rules that map a removed key are left alone,
/// so they stay stale (hiding their objects from scoped users) and work again if the key is re-added.</summary>
public sealed record UpdateScopeKeysCommand(Guid DataSourceId, List<string>? Keys) : IRequest<Unit>;

public sealed class UpdateScopeKeysCommandHandler(
    IDataSourceRepository repository,
    ICurrentDataScope currentScope,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateScopeKeysCommand, Unit>
{
    public async Task<Unit> Handle(UpdateScopeKeysCommand request, CancellationToken cancellationToken)
    {
        var dataSource = await repository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);
        DataSourceLock.EnsureCanChange(dataSource, AccessActor.From(currentUser));

        if (!currentScope.IsEnabled)
        {
            throw new CatalogValidationException("Data scoping is turned off in the app's code, so keys would do nothing.");
        }

        var keys = request.Keys ?? throw new CatalogValidationException("A list of keys is required.");
        if (keys.Count > DataScopeKeys.MaxPerDataSource)
        {
            throw new CatalogValidationException($"A data source can have at most {DataScopeKeys.MaxPerDataSource} scope keys.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!DataScopeKeys.IsValidName(key))
            {
                throw new CatalogValidationException(
                    $"Key '{key}' is invalid — use letters, digits and underscores only (max 128 characters).");
            }
            if (currentScope.HostKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new CatalogValidationException($"'{key}' is already set in the app's code.");
            }
            if (!seen.Add(key))
            {
                throw new CatalogValidationException($"'{key}' is listed more than once.");
            }
        }

        var before = dataSource.ScopeKeys.ToList();
        var after = keys.ToList();
        dataSource.ScopeKeys = after;
        dataSource.UpdatedBy = currentUser.UserId;
        dataSource.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.UpdateOnly(dataSource, nameof(DataSource.ScopeKeys));
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.DataSourceScopeKeysUpdated,
                nameof(DataSource),
                dataSource.Id,
                dataSource.Name,
                dataSource.Id,
                $"Set the data-scope keys of '{dataSource.Name}' to {Describe(after)} (was {Describe(before)})",
                new { before, after }),
            cancellationToken);

        return Unit.Value;
    }

    private static string Describe(List<string> keys) => keys.Count == 0 ? "none" : string.Join(", ", keys);
}
