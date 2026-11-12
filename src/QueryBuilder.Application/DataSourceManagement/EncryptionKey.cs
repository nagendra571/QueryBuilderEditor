using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.DataSourceManagement;

/// <param name="State">Previous (opens only with an old key, so re-encrypting fixes it) or Unreadable (no key opens it).</param>
public sealed record EncryptionKeySourceDto(Guid Id, string Name, bool IsActive, SecretKeyMatch State);

/// <summary>Fingerprints and counts only: never a key, a blob or any connection detail.</summary>
public sealed record EncryptionKeyStatusDto(
    bool KeyConfigured,
    string? Fingerprint,
    List<string> PreviousFingerprints,
    int ManagedCount,
    int OnCurrentKey,
    int OnPreviousKey,
    int Unreadable,
    List<EncryptionKeySourceDto> Sources,
    bool CanReencrypt = false);

public sealed record GetEncryptionKeyStatusQuery : IRequest<EncryptionKeyStatusDto>;

public sealed class GetEncryptionKeyStatusQueryHandler(IDataSourceRepository repository, IConnectionSecretProtector protector)
    : IRequestHandler<GetEncryptionKeyStatusQuery, EncryptionKeyStatusDto>
{
    public async Task<EncryptionKeyStatusDto> Handle(GetEncryptionKeyStatusQuery request, CancellationToken cancellationToken)
    {
        // Turned-off sources are included: they still hold a saved password.
        var managed = (await repository.GetAllAsync(cancellationToken)).Where(d => d.IsManaged).ToList();
        var sources = new List<EncryptionKeySourceDto>();
        int current = 0, previous = 0, unreadable = 0;

        foreach (var source in managed)
        {
            var match = string.IsNullOrEmpty(source.EncryptedConnection)
                ? SecretKeyMatch.Unreadable
                : protector.Identify(source.Id, source.EncryptedConnection);
            switch (match)
            {
                case SecretKeyMatch.Current: current++; continue;
                case SecretKeyMatch.Previous: previous++; break;
                default: unreadable++; break;
            }

            sources.Add(new EncryptionKeySourceDto(source.Id, source.Name, source.IsActive, match));
        }

        return new EncryptionKeyStatusDto(
            protector.CurrentFingerprint is not null,
            protector.CurrentFingerprint,
            [.. protector.PreviousFingerprints],
            managed.Count, current, previous, unreadable, sources);
    }
}

public sealed record ReencryptResultDto(int Reencrypted, int AlreadyCurrent, int Unreadable, int SkippedBecauseChanged);

public sealed record ReencryptDataSourcesCommand : IRequest<ReencryptResultDto>;

public sealed class ReencryptDataSourcesCommandHandler(
    IDataSourceRepository repository,
    IConnectionSecretProtector protector,
    IAuditLogger auditLogger)
    : IRequestHandler<ReencryptDataSourcesCommand, ReencryptResultDto>
{
    public async Task<ReencryptResultDto> Handle(ReencryptDataSourcesCommand request, CancellationToken cancellationToken)
    {
        if (protector.CurrentFingerprint is not { } toFingerprint)
        {
            throw new CatalogValidationException(
                "No encryption key is configured, so there is nothing to re-encrypt with. Set DataSourceManagement.EncryptionKey first.");
        }

        int reencrypted = 0, alreadyCurrent = 0, unreadable = 0, skipped = 0;

        foreach (var source in (await repository.GetAllAsync(cancellationToken)).Where(d => d.IsManaged))
        {
            var oldBlob = source.EncryptedConnection;
            if (string.IsNullOrEmpty(oldBlob))
            {
                unreadable++;
                continue;
            }

            switch (protector.Identify(source.Id, oldBlob))
            {
                case SecretKeyMatch.Current:
                    alreadyCurrent++;
                    continue;
                case SecretKeyMatch.Unreadable:
                    unreadable++;
                    continue;
            }

            string replacement;
            try
            {
                replacement = protector.Protect(source.Id, protector.Unprotect(source.Id, oldBlob));
            }
            catch (ConnectionSecretException)
            {
                unreadable++;
                continue;
            }

            // Conditional on the blob still being the one read: an admin saving this source right now writes new
            // details under the current key, and an unconditional write would put the old details back.
            // The connection string is identical afterwards, so no pool clear or catalog invalidation is needed.
            if (await repository.TryReplaceEncryptedConnectionAsync(source.Id, oldBlob, replacement, cancellationToken))
            {
                reencrypted++;
            }
            else
            {
                skipped++;
            }
        }

        var result = new ReencryptResultDto(reencrypted, alreadyCurrent, unreadable, skipped);

        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.EncryptionKeyRotated, nameof(DataSource), null, null, null,
                $"Re-encrypted {reencrypted} data source(s) with key {toFingerprint} " +
                $"({alreadyCurrent} already current, {unreadable} unreadable, {skipped} changed meanwhile)",
                new
                {
                    reencrypted, alreadyCurrent, unreadable, skippedBecauseChanged = skipped,
                    fromFingerprints = protector.PreviousFingerprints,
                    toFingerprint
                }),
            cancellationToken);

        return result;
    }
}
