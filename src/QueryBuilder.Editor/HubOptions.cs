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

    /// <summary>One-time sign-in codes an operator creates on the server, as a second way (or the only way) to sign in as a super admin.</summary>
    public SignInCodeOptions SignInCodes { get; set; } = new();
}

/// <summary>Settings for super admin sign-in with a one-time code. Off by default; needs <see cref="SuperAdminOptions.CodeNames"/>.</summary>
public sealed class SignInCodeOptions
{
    public bool Enabled { get; set; }

    /// <summary>How long a new code lives when the operator does not say (1 to 60).</summary>
    public int DefaultMinutes { get; set; } = 15;
}

/// <summary>Which kind of identity provider the host's super admin sign-in scheme is.</summary>
public enum SuperAdminProviderKind
{
    /// <summary>Microsoft Entra ID: the tenant is checked, and the object id, email or group id may match.</summary>
    Entra,

    /// <summary>Any OpenID Connect provider: the <c>sub</c> claim, a verified email or a group may match; no tenant.</summary>
    OpenIdConnect,
}

/// <summary>The allowlist for super admin sign-in: ids, emails or group ids of the provider, and names for one-time codes.</summary>
public sealed class SuperAdminOptions
{
    /// <summary>Decides which of the lists below apply. Default <see cref="SuperAdminProviderKind.Entra"/>.</summary>
    public SuperAdminProviderKind ProviderKind { get; set; } = SuperAdminProviderKind.Entra;

    /// <summary>
    /// Entra only: tenant ids (the <c>tid</c> claim) a super admin may sign in from. Required: an email or object id means
    /// nothing outside the tenant that issued it, and a multi-tenant app registration lets any tenant's users in.
    /// </summary>
    public List<string> TenantIds { get; set; } = [];

    public List<string> ObjectIds { get; set; } = [];
    public List<string> Emails { get; set; } = [];
    public List<string> GroupIds { get; set; } = [];

    /// <summary>OpenID Connect only: the provider's <c>sub</c> values that may sign in. Emails count only when the provider says they are verified.</summary>
    public List<string> UserIds { get; set; } = [];

    /// <summary>OpenID Connect only: the claim that carries group ids.</summary>
    public string GroupsClaim { get; set; } = "groups";

    /// <summary>Names a one-time sign-in code may be issued for (compared exactly, case-sensitive).</summary>
    public List<string> CodeNames { get; set; } = [];
}
