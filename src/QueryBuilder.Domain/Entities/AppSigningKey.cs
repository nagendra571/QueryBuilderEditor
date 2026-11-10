namespace QueryBuilder.Domain.Entities;

/// <summary>A public key an app's host uses to sign launch tokens; identified by <see cref="Kid"/> within the app.</summary>
public sealed class AppSigningKey : IHubRegistryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppId { get; set; }
    public string Kid { get; set; } = string.Empty;

    /// <summary>"RS256" or "ES256".</summary>
    public string Algorithm { get; set; } = string.Empty;

    public string PublicKeyPem { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
