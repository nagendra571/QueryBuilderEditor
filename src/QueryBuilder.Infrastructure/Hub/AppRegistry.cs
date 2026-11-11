using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Hub.Registry;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

/// <summary>Only ever resolved from a hub-wide scope (see RegistryAccess), which is what lets it write the registry tables.</summary>
internal sealed class AppRegistry(AppDbContext db, ICurrentUserService user, IHttpContextAccessor http, TimeProvider clock) : IAppRegistry
{
    public async Task<IReadOnlyList<AppDto>> ListAsync(CancellationToken ct)
    {
        var apps = await db.Apps.AsNoTracking().OrderBy(a => a.Name).ThenBy(a => a.Key).ToListAsync(ct);
        var keys = await db.AppSigningKeys.AsNoTracking().OrderBy(k => k.CreatedAtUtc).ToListAsync(ct);
        var urls = await db.AppReturnUrls.AsNoTracking().OrderBy(u => u.Url).ToListAsync(ct);

        return apps.Select(a => ToDto(a,
            keys.Where(k => k.AppId == a.Id).ToList(),
            urls.Where(u => u.AppId == a.Id).Select(u => u.Url).ToList())).ToList();
    }

    public async Task<AppDto> CreateAsync(string key, string name, CancellationToken ct)
    {
        if (await db.Apps.AnyAsync(a => a.Key == key, ct))
        {
            throw new CatalogValidationException($"An app with the key '{key}' already exists.");
        }

        var app = new App { Key = key, Name = name, CreatedAtUtc = clock.GetUtcNow() };
        await db.Apps.AddAsync(app, ct);
        Audit(app, $"Created app '{name}' ({key})", new { after = new { key, name, isEnabled = app.IsEnabled } });
        await SaveAsync(ct, $"An app with the key '{key}' already exists.");
        return ToDto(app, [], []);
    }

    public async Task<AppDto> UpdateAsync(Guid id, string name, bool isEnabled, List<string> scopeKeys, CancellationToken ct)
    {
        var app = await FindAsync(id, ct);
        var before = new { name = app.Name, isEnabled = app.IsEnabled, scopeKeys = app.ScopeKeys.ToList() };

        app.Name = name;
        app.IsEnabled = isEnabled;
        app.ScopeKeys = scopeKeys;
        Audit(app, $"Updated app '{name}' ({app.Key})", new { before, after = new { name, isEnabled, scopeKeys } });
        await SaveAsync(ct);

        return await DtoAsync(app, ct);
    }

    public async Task<SigningKeyDto> AddKeyAsync(Guid appId, string kid, string algorithm, string pem, string fingerprint, CancellationToken ct)
    {
        var app = await FindAsync(appId, ct);
        if (await db.AppSigningKeys.AnyAsync(k => k.AppId == appId && k.Kid == kid, ct))
        {
            throw new CatalogValidationException($"A key with the id '{kid}' already exists for this app.");
        }

        var key = new AppSigningKey { AppId = appId, Kid = kid, Algorithm = algorithm, PublicKeyPem = pem, CreatedAtUtc = clock.GetUtcNow() };
        await db.AppSigningKeys.AddAsync(key, ct);
        // The key itself is never audited, only which one it was.
        Audit(app, $"Added signing key '{kid}' to '{app.Key}'", new { added = new { kid, algorithm, fingerprint } });
        await SaveAsync(ct, $"A key with the id '{kid}' already exists for this app.");
        return new SigningKeyDto(kid, algorithm, fingerprint, key.CreatedAtUtc, null);
    }

    public async Task RevokeKeyAsync(Guid appId, string kid, CancellationToken ct)
    {
        var app = await FindAsync(appId, ct);
        // The database compares ignoring case; the validator matches kids exactly, so only an exact match is revoked.
        var key = (await db.AppSigningKeys.Where(k => k.AppId == appId && k.Kid == kid).ToListAsync(ct))
            .FirstOrDefault(k => string.Equals(k.Kid, kid, StringComparison.Ordinal))
            ?? throw new NotFoundException("Signing key", kid);

        // Repeating a revoke changes nothing and so writes nothing.
        if (key.RevokedAtUtc is not null)
        {
            return;
        }

        key.RevokedAtUtc = clock.GetUtcNow();
        Audit(app, $"Revoked signing key '{kid}' of '{app.Key}'",
            new { revoked = new { kid, key.Algorithm, fingerprint = RegistryRules.FingerprintOrNull(key.Algorithm, key.PublicKeyPem) } });
        await SaveAsync(ct);
    }

    public async Task<ReturnUrlsDto> SetReturnUrlsAsync(Guid appId, List<string> urls, CancellationToken ct)
    {
        var app = await FindAsync(appId, ct);
        var existing = await db.AppReturnUrls.Where(u => u.AppId == appId).ToListAsync(ct);

        var removed = existing.Where(e => !urls.Contains(e.Url, StringComparer.Ordinal)).ToList();
        var added = urls.Where(u => !existing.Any(e => string.Equals(e.Url, u, StringComparison.Ordinal))).ToList();

        db.AppReturnUrls.RemoveRange(removed);
        foreach (var url in added)
        {
            await db.AppReturnUrls.AddAsync(new AppReturnUrl { AppId = appId, Url = url }, ct);
        }

        Audit(app, $"Set the return URLs of '{app.Key}' to {urls.Count} entr{(urls.Count == 1 ? "y" : "ies")}",
            new { before = existing.Select(e => e.Url).ToList(), after = urls });
        await SaveAsync(ct);
        return new ReturnUrlsDto(urls);
    }

    private async Task<App> FindAsync(Guid id, CancellationToken ct) =>
        id != App.NoneId && await db.Apps.FirstOrDefaultAsync(a => a.Id == id, ct) is { } app
            ? app
            : throw new NotFoundException(nameof(App), id);

    private async Task<AppDto> DtoAsync(App app, CancellationToken ct) => ToDto(
        app,
        await db.AppSigningKeys.AsNoTracking().Where(k => k.AppId == app.Id).OrderBy(k => k.CreatedAtUtc).ToListAsync(ct),
        await db.AppReturnUrls.AsNoTracking().Where(u => u.AppId == app.Id).OrderBy(u => u.Url).Select(u => u.Url).ToListAsync(ct));

    private static AppDto ToDto(App app, List<AppSigningKey> keys, List<string> urls) => new(
        app.Id, app.Key, app.Name, app.IsEnabled, app.CreatedAtUtc, app.ScopeKeys.ToList(),
        keys.Select(k => new SigningKeyDto(k.Kid, k.Algorithm, RegistryRules.FingerprintOrNull(k.Algorithm, k.PublicKeyPem), k.CreatedAtUtc, k.RevokedAtUtc)).ToList(),
        urls);

    private void Audit(App app, string summary, object details) =>
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = user.UserId,
            Action = AuditAction.AppRegistryChanged,
            EntityType = nameof(App),
            EntityId = app.Id,
            EntityName = app.Name,
            // The app the change is about, so an app's own audit view can show it and the hub-level view can find it.
            AppId = app.Id,
            Summary = summary.Length > 500 ? summary[..500] : summary,
            DetailsJson = JsonSerializer.Serialize(details, QueryDefinitionSerializer.Options),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            TimestampUtc = clock.GetUtcNow(),
        });

    private async Task SaveAsync(CancellationToken ct, string? duplicateMessage = null)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (duplicateMessage is not null)
        {
            // Two admins adding the same key at once: the unique index is the real guard, the pre-check only gives the nice message.
            throw new CatalogValidationException(duplicateMessage);
        }
    }
}
