using System.Security.Claims;

namespace QueryBuilder.Editor.Hub;

internal enum SuperAdminMatchKind { ObjectId, Email, Group, UserId, Code }

internal enum SuperAdminRefusalReason { NotAllowlisted, WrongTenant, GroupsOverage, WrongScheme, NoStableId }

/// <summary>A signed-in host user who is on the allowlist. <see cref="ObjectId"/> is the stable id the session and audit rows carry.</summary>
internal sealed record SuperAdminMatch(string ObjectId, string? Name, string? Email, SuperAdminMatchKind Kind, string MatchedValue = "", string? TenantId = null);

/// <summary>
/// The outcome of looking at a signed-in host principal. <see cref="ObjectId"/> and <see cref="TenantId"/> are kept
/// even on a refusal so the refusal can be audited; they are the only identity facts that ever are.
/// </summary>
internal sealed record SuperAdminRecognition(
    SuperAdminMatch? Match, SuperAdminRefusalReason? Refusal, string? ObjectId, string? TenantId);

/// <summary>
/// Decides whether a principal the host signed in is a super admin: the tenant must be an allowed one, then an exact
/// match on the allowlist, nothing inferred. A principal that matches no entry is not a super admin, however
/// privileged it is in the host.
/// </summary>
internal static class SuperAdminRecognizer
{
    private const int MaxIdLength = 200;

    private static readonly string[] ObjectIdClaims = ["oid", "http://schemas.microsoft.com/identity/claims/objectidentifier"];
    private static readonly string[] TenantClaims = ["tid", "http://schemas.microsoft.com/identity/claims/tenantid"];
    private static readonly string[] EmailClaims = ["email", "preferred_username", "upn", ClaimTypes.Email, ClaimTypes.Upn];
    private static readonly string[] OidcEmailClaims = ["email", ClaimTypes.Email];
    private static readonly string[] EmailVerifiedClaims = ["email_verified"];
    private static readonly string[] NameClaims = ["name", ClaimTypes.Name];
    private static readonly string[] GroupClaims = ["groups"];
    private static readonly string[] SubjectClaims = ["sub", ClaimTypes.NameIdentifier];

    public static SuperAdminRecognition Recognize(ClaimsPrincipal principal, SuperAdminOptions allowed) =>
        allowed.ProviderKind == SuperAdminProviderKind.OpenIdConnect ? RecognizeOpenIdConnect(principal, allowed) : RecognizeEntra(principal, allowed);

    // Any OpenID Connect provider: no tenant, and the only identifier trusted is the `sub` the provider issued. An email
    // counts only when the provider vouches for it, since many providers let users set an unverified one.
    private static SuperAdminRecognition RecognizeOpenIdConnect(ClaimsPrincipal principal, SuperAdminOptions allowed)
    {
        var subjects = Distinct(Values(principal, SubjectClaims));
        var subject = subjects.Count == 1 ? subjects[0] : null;
        if (subject is null || subject.Length > MaxIdLength)
        {
            return new SuperAdminRecognition(null, SuperAdminRefusalReason.NoStableId, null, null);
        }

        var verified = Values(principal, EmailVerifiedClaims);
        var emailsVerified = verified.Count > 0 && verified.All(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
        var emails = emailsVerified ? Values(principal, OidcEmailClaims) : [];
        var groups = Values(principal, [allowed.GroupsClaim]);

        SuperAdminMatchKind? kind = null;
        var matched = "";
        if (Listed(allowed.UserIds).Any(e => Same(e, subject)))
        {
            kind = SuperAdminMatchKind.UserId;
            matched = subject;
        }
        else if (emails.FirstOrDefault(v => Listed(allowed.Emails).Contains(v, StringComparer.OrdinalIgnoreCase)) is { } email)
        {
            kind = SuperAdminMatchKind.Email;
            matched = email;
        }
        else if (groups.FirstOrDefault(v => Listed(allowed.GroupIds).Any(g => Same(g, v))) is { } group)
        {
            kind = SuperAdminMatchKind.Group;
            matched = group;
        }

        if (kind is not { } k)
        {
            return new SuperAdminRecognition(null, SuperAdminRefusalReason.NotAllowlisted, subject, null);
        }

        var match = new SuperAdminMatch(subject, Values(principal, NameClaims).FirstOrDefault(), Values(principal, OidcEmailClaims).FirstOrDefault(), k, matched);
        return new SuperAdminRecognition(match, null, subject, null);
    }

    private static SuperAdminRecognition RecognizeEntra(ClaimsPrincipal principal, SuperAdminOptions allowed)
    {
        var objectIds = Distinct(Values(principal, ObjectIdClaims));
        var tenants = Distinct(Values(principal, TenantClaims));
        var tenantId = tenants.Count == 1 ? tenants[0] : null;

        // The session and its audit rows need exactly one stable id, so a principal without one, or with
        // conflicting ones, cannot be attributed.
        string? objectId = null;
        var realObjectId = false;
        if (objectIds.Count == 1)
        {
            objectId = objectIds[0];
            realObjectId = true;
        }
        else if (objectIds.Count == 0)
        {
            var subjects = Distinct(Values(principal, SubjectClaims));
            objectId = subjects.Count == 1 ? subjects[0] : null;
        }

        // The id becomes the audit row's EntityName (200) and part of its Actor, so one that cannot fit is refused,
        // not truncated: a truncated id could collide with another person's.
        if (objectId is null || objectId.Length > MaxIdLength)
        {
            return new SuperAdminRecognition(null, SuperAdminRefusalReason.NoStableId, null, tenantId);
        }

        if (tenantId is null || !Listed(allowed.TenantIds).Any(t => Same(t, tenantId)))
        {
            return new SuperAdminRecognition(null, SuperAdminRefusalReason.WrongTenant, objectId, tenantId);
        }

        var emails = Values(principal, EmailClaims);
        var groups = Values(principal, GroupClaims);

        SuperAdminMatchKind? kind = null;
        var matched = "";
        // Only a real object id claim may match the object id list. A subject claim can label the session but is an
        // identifier of another kind (per app, per client) and must never be compared with the allowlist.
        if (realObjectId && Listed(allowed.ObjectIds).Any(e => Same(e, objectId)))
        {
            kind = SuperAdminMatchKind.ObjectId;
            matched = objectId;
        }
        else if (emails.FirstOrDefault(v => Listed(allowed.Emails).Contains(v, StringComparer.OrdinalIgnoreCase)) is { } email)
        {
            kind = SuperAdminMatchKind.Email;
            matched = email;
        }
        else if (groups.FirstOrDefault(v => Listed(allowed.GroupIds).Any(g => Same(g, v))) is { } group)
        {
            kind = SuperAdminMatchKind.Group;
            matched = group;
        }

        if (kind is not { } k)
        {
            var reason = HasGroupsOverage(principal) ? SuperAdminRefusalReason.GroupsOverage : SuperAdminRefusalReason.NotAllowlisted;
            return new SuperAdminRecognition(null, reason, objectId, tenantId);
        }

        var match = new SuperAdminMatch(objectId, Values(principal, NameClaims).FirstOrDefault(), emails.FirstOrDefault(), k, matched, tenantId);
        return new SuperAdminRecognition(match, null, objectId, tenantId);
    }

    /// <summary>
    /// The sign-in's own comparison run again against the current options, for a session that recorded what matched:
    /// the tenant must still be allowed and the matched value must still be on its list.
    /// </summary>
    public static bool StillAllowed(SuperAdminMatchKind kind, string value, string tenantId, SuperAdminOptions allowed)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // A code names no provider and no tenant; it stands or falls with the name list.
        if (kind == SuperAdminMatchKind.Code)
        {
            return Listed(allowed.CodeNames).Any(n => string.Equals(n, value, StringComparison.Ordinal));
        }

        var oidc = allowed.ProviderKind == SuperAdminProviderKind.OpenIdConnect;
        if (!oidc && (string.IsNullOrEmpty(tenantId) || !Listed(allowed.TenantIds).Any(t => Same(t, tenantId))))
        {
            return false;
        }

        return kind switch
        {
            SuperAdminMatchKind.ObjectId => !oidc && Listed(allowed.ObjectIds).Any(e => Same(e, value)),
            SuperAdminMatchKind.UserId => oidc && Listed(allowed.UserIds).Any(e => Same(e, value)),
            SuperAdminMatchKind.Email => Listed(allowed.Emails).Contains(value, StringComparer.OrdinalIgnoreCase),
            SuperAdminMatchKind.Group => Listed(allowed.GroupIds).Any(g => Same(g, value)),
            _ => false,
        };
    }

    /// <summary>
    /// Entra leaves the groups out of the token when there are too many and points at a Graph lookup instead.
    /// Nothing here follows that pointer, so such a user can only match by object id or email.
    /// </summary>
    public static bool HasGroupsOverage(ClaimsPrincipal principal) =>
        principal.FindAll("_claim_names").Any(c => c.Value.Contains("groups", StringComparison.OrdinalIgnoreCase));

    public static int ConfiguredEntryCount(SuperAdminOptions options) =>
        (options.ProviderKind == SuperAdminProviderKind.OpenIdConnect ? Listed(options.UserIds).Count() : Listed(options.ObjectIds).Count())
        + Listed(options.Emails).Count() + Listed(options.GroupIds).Count();

    public static int ConfiguredCodeNameCount(SuperAdminOptions options) => Listed(options.CodeNames).Count();

    public static int ConfiguredTenantCount(SuperAdminOptions options) => Listed(options.TenantIds).Count();

    // Ids are GUIDs and may differ in case or braces; anything that is not a GUID on both sides is compared exactly.
    private static bool Same(string a, string b) =>
        Guid.TryParse(a, out var ga) && Guid.TryParse(b, out var gb) ? ga == gb : string.Equals(a, b, StringComparison.Ordinal);

    private static List<string> Distinct(List<string> values)
    {
        var result = new List<string>();
        foreach (var v in values)
        {
            if (!result.Any(r => Same(r, v)))
            {
                result.Add(v);
            }
        }

        return result;
    }

    // A blank entry must never match a blank claim.
    private static IEnumerable<string> Listed(IEnumerable<string>? entries) =>
        (entries ?? []).Where(e => !string.IsNullOrWhiteSpace(e));

    private static List<string> Values(ClaimsPrincipal principal, string[] types) =>
        principal.Claims.Where(c => types.Contains(c.Type, StringComparer.Ordinal) && !string.IsNullOrEmpty(c.Value))
            .Select(c => c.Value).ToList();
}
