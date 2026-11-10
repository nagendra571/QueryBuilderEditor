using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Hub;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

/// <summary>
/// Writes the seen-user row and launch audit entries. Audit rows are built here, not through IAuditLogger,
/// because the actor must be the launched user (or "anonymous" for a refusal), never the request's existing session.
/// </summary>
public sealed class LaunchRecorder(AppDbContext db, IHttpContextAccessor httpContextAccessor, TimeProvider clock) : ILaunchRecorder
{
    // Refused before the signature is verified, so any app they name is only what the sender claimed.
    private static readonly HashSet<LaunchRefusalReason> BeforeSignature =
    [
        LaunchRefusalReason.Malformed, LaunchRefusalReason.UnknownApp, LaunchRefusalReason.AppDisabled,
        LaunchRefusalReason.UnknownKey, LaunchRefusalReason.RevokedKey, LaunchRefusalReason.AlgorithmNotAllowed,
        LaunchRefusalReason.BadSignature,
    ];

    public async Task RecordSuccessAsync(LaunchClaims claims, CancellationToken ct)
    {
        await UpsertSeenUserAsync(claims, ct);

        var counts = claims.Scope.IsUnrestricted
            ? new Dictionary<string, int>()
            : claims.Scope.Values.ToDictionary(p => p.Key, p => p.Value.Count);

        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = claims.UserId,
            Action = AuditAction.LaunchSucceeded,
            EntityType = "App",
            EntityId = claims.AppId,
            EntityName = claims.AppKey,
            Summary = $"Launched into '{claims.AppKey}' as {claims.Role}",
            DetailsJson = JsonSerializer.Serialize(new
            {
                userId = claims.UserId,
                role = claims.Role.ToString(),
                unrestricted = claims.Scope.IsUnrestricted,
                scopeValueCounts = counts,
                returnUrlPresent = claims.ReturnUrl is not null,
            }, QueryDefinitionSerializer.Options),
            IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            TimestampUtc = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordRefusalAsync(LaunchRefusalReason reason, Guid? appId, CancellationToken ct)
    {
        // A refusal before the signature check says nothing proven about who asked or which app, so it is marked as such.
        var authenticated = appId is not null && !BeforeSignature.Contains(reason);

        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = "anonymous",
            Action = AuditAction.LaunchRefused,
            EntityType = "App",
            EntityId = appId,
            AppId = appId,
            Summary = authenticated
                ? $"Launch refused: {reason}"
                : $"Launch refused (unauthenticated, signature not verified): {reason}",
            DetailsJson = JsonSerializer.Serialize(new { reason = reason.ToString(), authenticated }, QueryDefinitionSerializer.Options),
            IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            TimestampUtc = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task UpsertSeenUserAsync(LaunchClaims claims, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // Two launches of one user at once: the unique index refuses the second insert, which then updates instead.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var existing = await db.SeenUsers.FirstOrDefaultAsync(u => u.UserId == claims.UserId, ct);
            if (existing is not null)
            {
                existing.Name = claims.Name ?? existing.Name;
                existing.Email = claims.Email ?? existing.Email;
                existing.LastLaunchUtc = now;
                await db.SaveChangesAsync(ct);
                return;
            }

            var added = new SeenUser
            {
                AppId = claims.AppId, UserId = claims.UserId, Name = claims.Name, Email = claims.Email, LastLaunchUtc = now,
            };
            await db.SeenUsers.AddAsync(added, ct);
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                db.Entry(added).State = EntityState.Detached;
            }
        }
    }
}
