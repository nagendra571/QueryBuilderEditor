using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Editor.Hub;

public interface ISuperAdminCodeIssuer
{
    /// <summary>Issues a code and records that it was issued; the code is returned only once that record is saved.</summary>
    Task<IssuedCode> IssueAsync(string name, int? minutes, CancellationToken ct);
}

internal sealed class SuperAdminCodeIssuer(SuperAdminSignInCodes codes, IHubWideScopeFactory hubWide, TimeProvider clock) : ISuperAdminCodeIssuer
{
    public async Task<IssuedCode> IssueAsync(string name, int? minutes, CancellationToken ct)
    {
        var issued = codes.Issue(name, minutes);

        await using var scope = hubWide.Create();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = "server-command",
            Action = AuditAction.SuperAdminCodeIssued,
            EntityType = "SuperAdmin",
            EntityName = name,
            Summary = $"A sign-in code was issued for '{name}'",
            // The id lets an operator match this entry to a later sign-in; the code itself is never stored.
            DetailsJson = JsonSerializer.Serialize(new
            {
                name, minutes = (int)Math.Round((issued.ExpiresUtc - clock.GetUtcNow()).TotalMinutes), jti = issued.Jti,
            }, QueryDefinitionSerializer.Options),
            TimestampUtc = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return issued;
    }
}
