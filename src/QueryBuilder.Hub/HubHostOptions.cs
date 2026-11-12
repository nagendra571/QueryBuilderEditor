namespace QueryBuilder.Hub;

/// <summary>The <c>Hub</c> configuration section. Secrets (client secret, key, connection string) belong in environment variables or a server-only file, never in the committed appsettings.json.</summary>
public sealed class HubHostOptions
{
    public const string SectionName = "Hub";

    /// <summary>The hub's own metadata database. Must be empty or already a hub database.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>The value every launch token's <c>aud</c> must equal, typically the hub's public URL.</summary>
    public string? Audience { get; set; }

    public SignInSettings SignIn { get; set; } = new();
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

public sealed class SignInSettings
{
    public ProviderSettings Provider { get; set; } = new();
    public CodeSettings Codes { get; set; } = new();
}

/// <summary>The identity provider super admins sign in with. Leave <see cref="Kind"/> empty to use sign-in codes only.</summary>
public sealed class ProviderSettings
{
    /// <summary>Empty (no provider), <c>Entra</c> or <c>OpenIdConnect</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>What the sign-in page's button says ("Sign in with ...").</summary>
    public string? DisplayName { get; set; }

    /// <summary>Entra only.</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>Entra only: one tenant's id or domain, never <c>common</c>, <c>organizations</c> or <c>consumers</c>.</summary>
    public string? TenantId { get; set; }

    /// <summary>OpenID Connect only: the provider's issuer address, absolute https.</summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string CallbackPath { get; set; } = "/signin-oidc";
    public string[] Scopes { get; set; } = ["openid", "profile", "email"];

    /// <summary>OpenID Connect only: the claim that carries group ids.</summary>
    public string GroupsClaim { get; set; } = "groups";
}

public sealed class CodeSettings
{
    public bool Enabled { get; set; }
    public int DefaultMinutes { get; set; } = 15;

    /// <summary>Code attempts one address may make per window.</summary>
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}

public sealed class SuperAdminSettings
{
    public string[] TenantIds { get; set; } = [];
    public string[] ObjectIds { get; set; } = [];
    public string[] UserIds { get; set; } = [];
    public string[] Emails { get; set; } = [];
    public string[] GroupIds { get; set; } = [];
    public string[] CodeNames { get; set; } = [];
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

    /// <summary>How many proxy hops to read from <c>X-Forwarded-For</c>, counted from the right. Every hop must be a known proxy; at least 1.</summary>
    public int ForwardLimit { get; set; } = 1;
}
