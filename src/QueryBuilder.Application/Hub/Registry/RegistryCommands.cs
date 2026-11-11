using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;

namespace QueryBuilder.Application.Hub.Registry;

public sealed record SigningKeyDto(string Kid, string Algorithm, string? Fingerprint, DateTimeOffset CreatedAtUtc, DateTimeOffset? RevokedAtUtc);

public sealed record AppDto(
    Guid Id,
    string Key,
    string Name,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<string> ScopeKeys,
    IReadOnlyList<SigningKeyDto> Keys,
    IReadOnlyList<string> ReturnUrls);

public sealed record ReturnUrlsDto(IReadOnlyList<string> Urls);

/// <summary>
/// The registry's storage and audit, run inside a hub-wide scope. Every call is one atomic save that also writes
/// its <c>AppRegistryChanged</c> audit row, so a change can never exist without its record.
/// </summary>
public interface IAppRegistry
{
    Task<IReadOnlyList<AppDto>> ListAsync(CancellationToken ct);
    Task<AppDto> CreateAsync(string key, string name, CancellationToken ct);
    Task<AppDto> UpdateAsync(Guid id, string name, bool isEnabled, List<string> scopeKeys, CancellationToken ct);
    Task<SigningKeyDto> AddKeyAsync(Guid appId, string kid, string algorithm, string pem, string fingerprint, CancellationToken ct);
    Task RevokeKeyAsync(Guid appId, string kid, CancellationToken ct);
    Task<ReturnUrlsDto> SetReturnUrlsAsync(Guid appId, List<string> urls, CancellationToken ct);
}

public sealed record GetAppsQuery : IRequest<IReadOnlyList<AppDto>>;
public sealed record CreateAppCommand(string? Key, string? Name) : IRequest<AppDto>;
public sealed record UpdateAppCommand(Guid Id, string? Name, bool IsEnabled, List<string>? ScopeKeys) : IRequest<AppDto>;
public sealed record AddSigningKeyCommand(Guid AppId, string? Kid, string? Algorithm, string? PublicKeyPem) : IRequest<SigningKeyDto>;
public sealed record RevokeSigningKeyCommand(Guid AppId, string? Kid) : IRequest<Unit>;
public sealed record SetReturnUrlsCommand(Guid AppId, List<string>? Urls) : IRequest<ReturnUrlsDto>;

/// <summary>
/// Shared by every registry handler. The endpoint gate has already required a super admin; this checks again because
/// a hub-wide scope is the one thing that must never be reachable by a mistake in routing. The scope comes only from
/// <see cref="IHubWideScopeFactory"/>, a new DI scope, so the request's own app is never widened.
/// </summary>
internal static class RegistryAccess
{
    public static void RequireSuperAdmin(ICurrentUserService user)
    {
        if (!user.IsSuperAdmin)
        {
            throw new ForbiddenException("Only a super admin may change the app registry.");
        }
    }

    public static async Task<T> RunAsync<T>(IHubWideScopeFactory factory, Func<IAppRegistry, Task<T>> action)
    {
        await using var scope = factory.Create();
        return await action(scope.ServiceProvider.GetRequiredService<IAppRegistry>());
    }
}

public sealed class GetAppsQueryHandler(ICurrentUserService user, IHubWideScopeFactory factory) : IRequestHandler<GetAppsQuery, IReadOnlyList<AppDto>>
{
    public Task<IReadOnlyList<AppDto>> Handle(GetAppsQuery request, CancellationToken ct)
    {
        RegistryAccess.RequireSuperAdmin(user);
        return RegistryAccess.RunAsync(factory, r => r.ListAsync(ct));
    }
}

public sealed class CreateAppCommandHandler(ICurrentUserService user, IHubWideScopeFactory factory) : IRequestHandler<CreateAppCommand, AppDto>
{
    public Task<AppDto> Handle(CreateAppCommand request, CancellationToken ct)
    {
        RegistryAccess.RequireSuperAdmin(user);
        var key = RegistryRules.AppKey(request.Key);
        var name = RegistryRules.AppName(request.Name);
        return RegistryAccess.RunAsync(factory, r => r.CreateAsync(key, name, ct));
    }
}

public sealed class UpdateAppCommandHandler(ICurrentUserService user, IHubWideScopeFactory factory) : IRequestHandler<UpdateAppCommand, AppDto>
{
    public Task<AppDto> Handle(UpdateAppCommand request, CancellationToken ct)
    {
        RegistryAccess.RequireSuperAdmin(user);
        var name = RegistryRules.AppName(request.Name);
        var keys = RegistryRules.ScopeKeys(request.ScopeKeys);
        return RegistryAccess.RunAsync(factory, r => r.UpdateAsync(request.Id, name, request.IsEnabled, keys, ct));
    }
}

public sealed class AddSigningKeyCommandHandler(ICurrentUserService user, IHubWideScopeFactory factory) : IRequestHandler<AddSigningKeyCommand, SigningKeyDto>
{
    public Task<SigningKeyDto> Handle(AddSigningKeyCommand request, CancellationToken ct)
    {
        RegistryAccess.RequireSuperAdmin(user);
        var kid = RegistryRules.Kid(request.Kid);
        var fingerprint = RegistryRules.ValidatePublicKey(request.Algorithm, request.PublicKeyPem);
        return RegistryAccess.RunAsync(factory, r => r.AddKeyAsync(request.AppId, kid, request.Algorithm!, request.PublicKeyPem!, fingerprint, ct));
    }
}

public sealed class RevokeSigningKeyCommandHandler(ICurrentUserService user, IHubWideScopeFactory factory) : IRequestHandler<RevokeSigningKeyCommand, Unit>
{
    public async Task<Unit> Handle(RevokeSigningKeyCommand request, CancellationToken ct)
    {
        RegistryAccess.RequireSuperAdmin(user);
        var kid = RegistryRules.Kid(request.Kid);
        await RegistryAccess.RunAsync(factory, async r => { await r.RevokeKeyAsync(request.AppId, kid, ct); return Unit.Value; });
        return Unit.Value;
    }
}

public sealed class SetReturnUrlsCommandHandler(ICurrentUserService user, IHubWideScopeFactory factory) : IRequestHandler<SetReturnUrlsCommand, ReturnUrlsDto>
{
    public Task<ReturnUrlsDto> Handle(SetReturnUrlsCommand request, CancellationToken ct)
    {
        RegistryAccess.RequireSuperAdmin(user);
        var urls = RegistryRules.ReturnUrls(request.Urls);
        return RegistryAccess.RunAsync(factory, r => r.SetReturnUrlsAsync(request.AppId, urls, ct));
    }
}
