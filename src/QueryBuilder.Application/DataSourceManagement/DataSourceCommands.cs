using FluentValidation;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.DataSourceManagement;

public sealed record CreateDataSourceCommand(string? Name, string? Description, DataSourceProvider Provider, ConnectionInput? Connection)
    : IRequest<AdminDataSourceSummaryDto>;

public sealed class CreateDataSourceCommandValidator : AbstractValidator<CreateDataSourceCommand>
{
    public CreateDataSourceCommandValidator(IDataSourceRepository repository)
    {
        RuleFor(x => x.Name).UniqueName(repository, _ => null);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Provider).SupportedProvider();
        RuleFor(x => x.Connection).NotNull().SetValidator(new ConnectionInputValidator()!);
    }
}

public sealed class CreateDataSourceCommandHandler(
    IDataSourceRepository repository,
    IConnectionSecretProtector protector,
    DataSourceManagementSettings settings,
    ICurrentUserService currentUser,
    ICurrentAppReader apps,
    IAuditLogger auditLogger)
    : IRequestHandler<CreateDataSourceCommand, AdminDataSourceSummaryDto>
{
    public async Task<AdminDataSourceSummaryDto> Handle(CreateDataSourceCommand request, CancellationToken cancellationToken)
    {
        ManagedConnections.RequireEnabled(settings);
        var actor = AccessActor.From(currentUser);
        await DataSourceLock.EnsureCanManageConnectionsAsync(apps, actor, cancellationToken);

        var details = ManagedConnections.Resolve(request.Provider, request.Connection!, stored: null);
        var dataSource = new DataSource
        {
            Name = request.Name!.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Provider = request.Provider,
            ConnectionStringName = DataSource.ManagedConnectionSentinel,
            CatalogScope = CatalogScope.Views,
            CreatedBy = currentUser.UserId,
            // A super admin's source is locked from the start; an app admin's stays in the app's own hands.
            LockedBySuperAdmin = actor.InHub && actor.IsSuperAdmin
        };
        // The Id is part of what the blob is bound to, so it has to exist before encrypting.
        dataSource.EncryptedConnection = protector.Protect(dataSource.Id, ManagedConnections.Serialize(details));

        await repository.AddAsync(dataSource, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.DataSourceCreated, nameof(DataSource), dataSource.Id, dataSource.Name, dataSource.Id,
                $"Added data source '{dataSource.Name}' ({request.Provider}) on {details.Host}",
                new { dataSource.Name, Provider = request.Provider.ToString(), details.Host, details.Port, details.Database }),
            cancellationToken);

        return new AdminDataSourceSummaryDto(dataSource.Id, dataSource.Name, dataSource.Provider, dataSource.IsActive, dataSource.LockedBySuperAdmin);
    }
}

public sealed record UpdateDataSourceCommand(Guid Id, string? Name, string? Description, ConnectionInput? Connection) : IRequest<Unit>;

public sealed class UpdateDataSourceCommandValidator : AbstractValidator<UpdateDataSourceCommand>
{
    public UpdateDataSourceCommandValidator(IDataSourceRepository repository)
    {
        RuleFor(x => x.Name).UniqueName(repository, c => c.Id);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Connection!).SetValidator(new ConnectionInputValidator()).When(x => x.Connection is not null);
    }
}

public sealed class UpdateDataSourceCommandHandler(
    IDataSourceRepository repository,
    IConnectionSecretProtector protector,
    IDataCatalogService catalogService,
    IConnectionPoolCleaner pools,
    DataSourceManagementSettings settings,
    ICurrentUserService currentUser,
    ICurrentAppReader apps,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateDataSourceCommand, Unit>
{
    public async Task<Unit> Handle(UpdateDataSourceCommand request, CancellationToken cancellationToken)
    {
        ManagedConnections.RequireEnabled(settings);

        var dataSource = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.Id);
        var actor = AccessActor.From(currentUser);
        DataSourceLock.EnsureCanChange(dataSource, actor);
        await DataSourceLock.EnsureCanManageConnectionsAsync(apps, actor, cancellationToken);

        if (request.Connection is not null && !dataSource.IsManaged)
        {
            throw new CatalogValidationException(
                "This data source's connection comes from host configuration and can't be changed here.");
        }

        var changed = new List<string>();
        var name = request.Name!.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        string? host = null;

        if (request.Connection is not null)
        {
            var stored = ManagedConnections.TryRead(dataSource, protector);
            var details = ManagedConnections.Resolve(dataSource.Provider, request.Connection, stored, storedUnreadable: stored is null);
            host = details.Host;
            pools.Clear(dataSource);

            if (stored is null)
            {
                changed.Add("connection");
            }
            else
            {
                if (!string.Equals(stored.Host, details.Host, StringComparison.Ordinal)) changed.Add("host");
                if (stored.Port != details.Port) changed.Add("port");
                if (!string.Equals(stored.Database, details.Database, StringComparison.Ordinal)) changed.Add("database");
                if (!string.Equals(stored.Username, details.Username, StringComparison.Ordinal)) changed.Add("username");
                if (!string.Equals(stored.Password, details.Password, StringComparison.Ordinal)) changed.Add("password");
                if (stored.SslMode != details.SslMode) changed.Add("encryption");
            }

            dataSource.EncryptedConnection = protector.Protect(dataSource.Id, ManagedConnections.Serialize(details));
        }

        if (!string.Equals(dataSource.Name, name, StringComparison.Ordinal)) changed.Add("name");
        if (!string.Equals(dataSource.Description, description, StringComparison.Ordinal)) changed.Add("description");

        dataSource.Name = name;
        dataSource.Description = description;
        dataSource.UpdatedBy = currentUser.UserId;
        dataSource.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.UpdateOnly(
            dataSource,
            request.Connection is not null
                ? [nameof(DataSource.Name), nameof(DataSource.Description), nameof(DataSource.EncryptedConnection)]
                : [nameof(DataSource.Name), nameof(DataSource.Description)]);
        await repository.SaveChangesAsync(cancellationToken);

        // Rules, limits and the allowlist stay as they were; only what was read from the old connection goes.
        catalogService.InvalidateCatalogCache(dataSource.Id);

        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.DataSourceUpdated, nameof(DataSource), dataSource.Id, dataSource.Name, dataSource.Id,
                changed.Count == 0
                    ? $"Saved data source '{dataSource.Name}' with no changes"
                    : $"Updated data source '{dataSource.Name}': {string.Join(", ", changed)}",
                new { dataSource.Name, Provider = dataSource.Provider.ToString(), Host = host, Changed = changed }),
            cancellationToken);

        return Unit.Value;
    }
}

public sealed record SetDataSourceActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetDataSourceActiveCommandHandler(
    IDataSourceRepository repository,
    IDataCatalogService catalogService,
    IConnectionPoolCleaner pools,
    DataSourceManagementSettings settings,
    ICurrentUserService currentUser,
    ICurrentAppReader apps,
    IAuditLogger auditLogger)
    : IRequestHandler<SetDataSourceActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetDataSourceActiveCommand request, CancellationToken cancellationToken)
    {
        ManagedConnections.RequireEnabled(settings);

        var dataSource = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.Id);
        var actor = AccessActor.From(currentUser);
        DataSourceLock.EnsureCanChange(dataSource, actor);
        await DataSourceLock.EnsureCanManageConnectionsAsync(apps, actor, cancellationToken);

        if (dataSource.IsActive == request.IsActive)
        {
            return Unit.Value;
        }

        dataSource.IsActive = request.IsActive;
        dataSource.UpdatedBy = currentUser.UserId;
        dataSource.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.UpdateOnly(dataSource, nameof(DataSource.IsActive));
        await repository.SaveChangesAsync(cancellationToken);
        pools.Clear(dataSource);

        // The cached catalog was built while the source was in its old state (and a turn-on should read it fresh).
        catalogService.InvalidateCatalogCache(dataSource.Id);

        await auditLogger.LogAsync(
            new AuditEntry(
                request.IsActive ? AuditAction.DataSourceActivated : AuditAction.DataSourceDeactivated,
                nameof(DataSource), dataSource.Id, dataSource.Name, dataSource.Id,
                $"{(request.IsActive ? "Turned on" : "Turned off")} data source '{dataSource.Name}'",
                new { dataSource.Name, Provider = dataSource.Provider.ToString() }),
            cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Tests values that aren't saved (yet). A blank password reuses <paramref name="ExistingId"/>'s stored one
/// only when host, port and username are unchanged and encryption isn't lowered.</summary>
public sealed record TestConnectionCommand(DataSourceProvider Provider, ConnectionInput? Connection, Guid? ExistingId)
    : IRequest<ConnectionTestResult>;

public sealed class TestConnectionCommandValidator : AbstractValidator<TestConnectionCommand>
{
    public TestConnectionCommandValidator()
    {
        RuleFor(x => x.Provider).SupportedProvider();
        RuleFor(x => x.Connection).NotNull().SetValidator(new ConnectionInputValidator()!);
    }
}

public sealed class TestConnectionCommandHandler(
    IDataSourceRepository repository,
    IConnectionSecretProtector protector,
    IConnectionTester tester,
    DataSourceManagementSettings settings,
    ICurrentUserService currentUser,
    ICurrentAppReader apps)
    : IRequestHandler<TestConnectionCommand, ConnectionTestResult>
{
    public async Task<ConnectionTestResult> Handle(TestConnectionCommand request, CancellationToken cancellationToken)
    {
        ManagedConnections.RequireEnabled(settings);
        var actor = AccessActor.From(currentUser);
        await DataSourceLock.EnsureCanManageConnectionsAsync(apps, actor, cancellationToken);

        ManagedConnectionDetails? stored = null;
        var unreadable = false;
        if (request.ExistingId is { } id)
        {
            var existing = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(DataSource), id);
            DataSourceLock.EnsureCanChange(existing, actor);
            if (existing.Provider != request.Provider)
            {
                throw new CatalogValidationException("The provider of an existing data source can't change.");
            }

            stored = ManagedConnections.TryRead(existing, protector);
            unreadable = existing.IsManaged && stored is null;
        }

        var details = ManagedConnections.Resolve(request.Provider, request.Connection!, stored, unreadable);
        return await tester.TestAsync(request.Provider, details, cancellationToken);
    }
}

public sealed record TestStoredConnectionCommand(Guid Id) : IRequest<ConnectionTestResult>;

public sealed class TestStoredConnectionCommandHandler(
    IDataSourceRepository repository,
    IConnectionTester tester,
    DataSourceManagementSettings settings,
    ICurrentUserService currentUser,
    ICurrentAppReader apps)
    : IRequestHandler<TestStoredConnectionCommand, ConnectionTestResult>
{
    public async Task<ConnectionTestResult> Handle(TestStoredConnectionCommand request, CancellationToken cancellationToken)
    {
        ManagedConnections.RequireEnabled(settings);

        var dataSource = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.Id);
        var actor = AccessActor.From(currentUser);
        DataSourceLock.EnsureCanChange(dataSource, actor);
        await DataSourceLock.EnsureCanManageConnectionsAsync(apps, actor, cancellationToken);
        return await tester.TestStoredAsync(dataSource, cancellationToken);
    }
}
