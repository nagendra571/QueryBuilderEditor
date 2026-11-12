namespace QueryBuilder.Domain.Entities;

/// <summary>A launch token id already used; kept until the token could no longer be valid anyway.</summary>
public sealed class LaunchReplay : IHubSystemRecord
{
    public string Jti { get; set; } = string.Empty;
    public Guid AppId { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
}
