using System.Security.Claims;

namespace QueryBuilder.Editor.Hub;

internal enum SuperAdminMatchKind { ObjectId, Email, Group }

internal enum SuperAdminRefusalReason { NotAllowlisted, WrongTenant, GroupsOverage, WrongScheme, NoStableId }

/// <summary>A signed-in host user who is on the allowlist. <see cref="ObjectId"/> is the stable id the session and audit rows carry.</summary>
internal sealed record SuperAdminMatch(string ObjectId, string? Name, string? Email, SuperAdminMatchKind Kind);

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
    private static readonly string[] ObjectIdClaims = ["oid", "http://schemas.microsoft.com/identity/claims/objectidentifier"];
    private static readonly string[] TenantClaims = ["tid", "http://schemas.microsoft.com/identity/claims/tenantid"];
    private static readonly string[] EmailClaims = ["email", "preferred_username", "upn", ClaimTypes.Email, ClaimTypes.Upn];
    private static readonly string[] NameClaims = ["name", ClaimTypes.Name];
    private static readonly string[] GroupClaims = ["groups"];
    private static readonly string[] SubjectClaims = ["sub", ClaimTypes.NameIdentifier];

    public static SuperAdminRecognition Recognize(ClaimsPrincipal principal, SuperAdminOptions allowed)
    {
        var objectIds = Distinct(Values(principal, ObjectIdClaims));
        var tenants = Distinct(Values(principal, TenantClaims));
        var tenantId = tenants.Count == 1 ? tenants[0] : null;

        // The session and its audit rows need exactly one stable id, so a principal without one, or with
        // conflicting ones, cannot be attributed.
        string? objectId = null;
        if (objectIds.Count == 1)
        {
            objectId = objectIds[0];
        }
        else if (objectIds.Count == 0)
        {
            var subjects = Distinct(Values(principal, SubjectClaims));
            objectId = subjects.Count == 1 ? subjects[0] : null;
        }

        if (objectId is null)
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
        if (Listed(allowed.ObjectIds).Any(e => Same(e, objectId)))
        {
            kind = SuperAdminMatchKind.ObjectId;
        }
        else if (emails.Any(v => Listed(allowed.Emails).Contains(v, StringComparer.OrdinalIgnoreCase)))
        {
            kind = SuperAdminMatchKind.Email;
        }
        else if (groups.Any(v => Listed(allowed.GroupIds).Any(g => Same(g, v))))
        {
            kind = SuperAdminMatchKind.Group;
        }

        if (kind is not { } k)
        {
            var reason = HasGroupsOverage(principal) ? SuperAdminRefusalReason.GroupsOverage : SuperAdminRefusalReason.NotAllowlisted;
            return new SuperAdminRecognition(null, reason, objectId, tenantId);
        }

        var match = new SuperAdminMatch(objectId, Values(principal, NameClaims).FirstOrDefault(), emails.FirstOrDefault(), k);
        return new SuperAdminRecognition(match, null, objectId, tenantId);
    }

    /// <summary>
    /// Entra leaves the groups out of the token when there are too many and points at a Graph lookup instead.
    /// Nothing here follows that pointer, so such a user can only match by object id or email.
    /// </summary>
    public static bool HasGroupsOverage(ClaimsPrincipal principal) =>
        principal.FindAll("_claim_names").Any(c => c.Value.Contains("groups", StringComparison.OrdinalIgnoreCase));

    public static int ConfiguredEntryCount(SuperAdminOptions options) =>
        Listed(options.ObjectIds).Count() + Listed(options.Emails).Count() + Listed(options.GroupIds).Count();

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
