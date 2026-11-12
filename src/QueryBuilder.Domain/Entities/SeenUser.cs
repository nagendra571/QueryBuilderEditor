namespace QueryBuilder.Domain.Entities;

/// <summary>A person an app's host has launched into the hub, kept so the sharing picker has names without a host user list.</summary>
public sealed class SeenUser : IAppOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset LastLaunchUtc { get; set; } = DateTimeOffset.UtcNow;
}
