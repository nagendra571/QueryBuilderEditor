using System.Security.Claims;
using System.Text.Json;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Editor.Hub;

/// <summary>
/// The hub session as claims, and back. Internal (visible to the test project) because only the launch and
/// super admin endpoints may mint one; nothing a browser sends is ever turned into these claims.
/// </summary>
internal static class HubSession
{
    public const string Scheme = "QueryBuilder.Hub";

    /// <summary>Where the host's sign-in handler parks its result for a moment; see <see cref="HubOptions.SuperAdminSignInScheme"/>.</summary>
    public const string ExternalScheme = "QueryBuilder.HubExternal";

    public const string Kind = "qb:kind";
    public const string App = "qb:app";
    public const string User = "qb:user";
    public const string Name = "qb:name";
    public const string Email = "qb:email";
    public const string Role = "qb:role";
    public const string Scope = "qb:scope";
    public const string Return = "qb:return";
    public const string Issued = "qb:issued";
    public const string Viewing = "qb:viewing";
    public const string ObjectId = "qb:oid";
    public const string MatchKind = "qb:match-kind";
    public const string MatchValue = "qb:match-value";
    public const string TenantId = "qb:tid";

    public const string AppKind = "app";
    public const string SuperKind = "super";
    public const string UnrestrictedScope = "*";

    public static ClaimsPrincipal ForApp(Guid appId, string userId, string? name, string? email, HubRole role,
        ResolvedDataScope scope, string? returnUrl, DateTimeOffset issuedAt)
    {
        var claims = new List<Claim>
        {
            new(Kind, AppKind),
            new(App, appId.ToString()),
            new(User, userId),
            new(Role, role.ToString()),
            new(Scope, SerializeScope(scope)),
            new(Issued, issuedAt.ToUnixTimeSeconds().ToString()),
        };
        AddIfPresent(claims, Name, name);
        AddIfPresent(claims, Email, email);
        AddIfPresent(claims, Return, returnUrl);
        return Principal(claims);
    }

    public static ClaimsPrincipal ForSuperAdmin(string objectId, string? name, string? email, Guid? viewingAppId, DateTimeOffset issuedAt,
        SuperAdminMatchKind matchKind, string matchValue, string tenantId)
    {
        var claims = new List<Claim>
        {
            new(Kind, SuperKind),
            new(ObjectId, objectId),
            new(Issued, issuedAt.ToUnixTimeSeconds().ToString()),
            // What matched the allowlist, so every request can check it is still there.
            new(MatchKind, matchKind.ToString()),
            new(MatchValue, matchValue),
            new(TenantId, tenantId),
        };
        AddIfPresent(claims, Name, name);
        AddIfPresent(claims, Email, email);
        if (viewingAppId is { } viewing)
        {
            claims.Add(new Claim(Viewing, viewing.ToString()));
        }

        return Principal(claims);
    }

    /// <summary>Null for anything that is not a complete, well-formed hub session, so a damaged one is treated as no session.</summary>
    public static HubSessionInfo? TryRead(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not { IsAuthenticated: true } || principal.Identity.AuthenticationType != Scheme)
        {
            return null;
        }

        var kind = principal.FindFirst(Kind)?.Value;
        var email = principal.FindFirst(Email)?.Value;
        var name = principal.FindFirst(Name)?.Value;
        var returnUrl = principal.FindFirst(Return)?.Value;

        if (kind == SuperKind)
        {
            var oid = principal.FindFirst(ObjectId)?.Value;
            if (string.IsNullOrEmpty(oid))
            {
                return null;
            }

            Guid? viewing = null;
            if (principal.FindFirst(Viewing)?.Value is { } v)
            {
                if (!Guid.TryParse(v, out var parsed))
                {
                    return null;
                }

                viewing = parsed;
            }

            var userId = $"super:{oid}";
            return new HubSessionInfo(true, viewing, userId, name ?? email ?? userId, email, HubRole.Admin, ResolvedDataScope.Unrestricted, null);
        }

        if (kind == AppKind
            && Guid.TryParse(principal.FindFirst(App)?.Value, out var appId)
            && principal.FindFirst(User)?.Value is { Length: > 0 } user
            && Enum.TryParse<HubRole>(principal.FindFirst(Role)?.Value, out var role) && Enum.IsDefined(role)
            && TryParseScope(principal.FindFirst(Scope)?.Value, out var scope))
        {
            return new HubSessionInfo(false, appId, user, name ?? user, email, role, scope, returnUrl);
        }

        return null;
    }

    public static bool TryReadSuperMatch(ClaimsPrincipal principal, out SuperAdminMatchKind kind, out string value, out string tenantId)
    {
        value = principal.FindFirst(MatchValue)?.Value ?? "";
        tenantId = principal.FindFirst(TenantId)?.Value ?? "";
        return Enum.TryParse(principal.FindFirst(MatchKind)?.Value, out kind) && Enum.IsDefined(kind)
            && value.Length > 0 && tenantId.Length > 0;
    }

    public static bool TryReadIssued(ClaimsPrincipal principal, out DateTimeOffset issuedAt)
    {
        issuedAt = default;
        if (!long.TryParse(principal.FindFirst(Issued)?.Value, out var seconds))
        {
            return false;
        }

        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string SerializeScope(ResolvedDataScope scope) =>
        scope.SerializeForSession();

    private static bool TryParseScope(string? raw, out ResolvedDataScope scope)
    {
        scope = ResolvedDataScope.Denied;
        if (raw == UnrestrictedScope)
        {
            scope = ResolvedDataScope.Unrestricted;
            return true;
        }

        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string[]>>(raw, QueryBuilderJson.Options);
            if (values is null)
            {
                return false;
            }

            scope = new ResolvedDataScope(false, values.ToDictionary(
                p => p.Key, p => (IReadOnlyList<string>)p.Value.ToList(), StringComparer.OrdinalIgnoreCase));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AddIfPresent(List<Claim> claims, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            claims.Add(new Claim(type, value));
        }
    }

    private static ClaimsPrincipal Principal(List<Claim> claims) =>
        new(new ClaimsIdentity(claims, Scheme, Name, Role));
}
