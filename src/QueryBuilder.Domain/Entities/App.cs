namespace QueryBuilder.Domain.Entities;

/// <summary>One tenant of the hub. Embedded (single-app) mode has exactly one, <see cref="DefaultId"/>.</summary>
public sealed class App : IHubRegistryRecord
{
    public const string DefaultKey = "default";

    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-00000000000d");

    /// <summary>Never a row: what a hub request without an app resolves to, so it sees nothing.</summary>
    public static readonly Guid NoneId = Guid.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Lowercase letters, digits and hyphens, up to 64 characters.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    /// <summary>Set when the app is turned off or one of its signing keys is revoked: sessions issued before it end.</summary>
    public DateTimeOffset? SessionsValidAfterUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Scope keys the app declares itself (hub mode has no host options); a data source may add its own.</summary>
    public List<string> ScopeKeys { get; set; } = [];
}
