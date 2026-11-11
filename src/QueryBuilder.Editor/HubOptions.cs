namespace QueryBuilder.Editor;

/// <summary>Hub mode: one QueryBuilder serving several apps from one metadata database. Off by default.</summary>
public sealed class HubOptions
{
    public bool Enabled { get; set; }

    /// <summary>The value every launch token's <c>aud</c> must equal (typically the hub's public URL). Required in hub mode.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>The session cookie's name. The <c>__Host-</c> prefix makes browsers refuse it unless it is Secure, Path=/ and has no Domain.</summary>
    public string CookieName { get; set; } = "__Host-qb-hub";

    /// <summary>How long a session lives from the launch, however active the user is.</summary>
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>How long a session may sit unused before it ends.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Who may sign in as a super admin through the host's own sign-in. A launch token can never make one.</summary>
    public SuperAdminOptions SuperAdmins { get; set; } = new();

    /// <summary>
    /// The host's authentication scheme that signs a super admin in (typically its OpenID Connect handler). Required in hub mode.
    /// Use a dedicated OpenID Connect registration for this, whose <c>SignInScheme</c> is <c>"QueryBuilder.HubExternal"</c>, a
    /// short-lived cookie scheme the package registers: a remote handler cannot be read back on demand, so the package
    /// reads the user it leaves there, checks the tenant and the allowlist, and deletes it. The ticket is accepted only
    /// if it was started by <c>/querybuilder/signin</c> and stamped by this very scheme, so another handler of the host
    /// that also signs into that cookie cannot create a super admin. Entra group claims are only honoured when the
    /// token carries them; when it omits them for being too many, such a user can only match by object id or email.
    /// </summary>
    public string? SuperAdminSignInScheme { get; set; }
}

/// <summary>The allowlist for super admin sign-in: Entra object ids, emails or group ids.</summary>
public sealed class SuperAdminOptions
{
    /// <summary>
    /// Entra tenant ids (the <c>tid</c> claim) a super admin may sign in from. Required: an email or object id means
    /// nothing outside the tenant that issued it, and a multi-tenant app registration lets any tenant's users in.
    /// </summary>
    public List<string> TenantIds { get; set; } = [];

    public List<string> ObjectIds { get; set; } = [];
    public List<string> Emails { get; set; } = [];
    public List<string> GroupIds { get; set; } = [];
}
