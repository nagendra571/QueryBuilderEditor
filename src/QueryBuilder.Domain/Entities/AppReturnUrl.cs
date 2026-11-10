namespace QueryBuilder.Domain.Entities;

/// <summary>A URL (up to 800 characters, so the unique index fits SQL Server's key limit) a launch token's <c>returnUrl</c> may exactly equal.</summary>
public sealed class AppReturnUrl : IHubRegistryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppId { get; set; }
    public string Url { get; set; } = string.Empty;
}
