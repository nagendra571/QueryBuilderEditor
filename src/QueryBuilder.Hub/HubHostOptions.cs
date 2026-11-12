namespace QueryBuilder.Hub;

/// <summary>The <c>Hub</c> configuration section. Secrets (client secret, key, connection string) belong in environment variables or a server-only file, never in the committed appsettings.json.</summary>
public sealed class HubHostOptions
{
    public const string SectionName = "Hub";

    /// <summary>The hub's own metadata database. Must be empty or already a hub database.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>The value every launch token's <c>aud</c> must equal, typically the hub's public URL.</summary>
    public string? Audience { get; set; }

    public EntraOptions Entra { get; set; } = new();
    public SuperAdminSettings SuperAdmins { get; set; } = new();

    /// <summary>Base64, 32 bytes. The same value on every instance.</summary>
    public string? DataSourceKey { get; set; }

    public string[] PreviousDataSourceKeys { get; set; } = [];
    public LaunchRateLimitOptions LaunchRateLimit { get; set; } = new();
    public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();
    public int? DefaultMaxRecords { get; set; }

    /// <summary>Set to false when a DBA applies the schema script; the host then never runs migrations.</summary>
    public bool ApplyMigrations { get; set; } = true;
}

public sealed class EntraOptions
{
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string CallbackPath { get; set; } = "/signin-oidc";
}

public sealed class SuperAdminSettings
{
    public string[] TenantIds { get; set; } = [];
    public string[] ObjectIds { get; set; } = [];
    public string[] Emails { get; set; } = [];
    public string[] GroupIds { get; set; } = [];
}

public sealed class LaunchRateLimitOptions
{
    public int PermitLimit { get; set; } = 30;
    public int WindowSeconds { get; set; } = 60;
}

public sealed class ForwardedHeadersSettings
{
    /// <summary>Addresses of the reverse proxies whose X-Forwarded-* headers are trusted.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>CIDR ranges (for example <c>10.0.0.0/8</c>) of proxies whose headers are trusted.</summary>
    public string[] KnownNetworks { get; set; } = [];
}
