using System.Security.Claims;

namespace QueryBuilder.Editor.Hub;

internal enum SuperAdminMatchKind { ObjectId, Email, Group }

/// <summary>A signed-in host user who is on the allowlist. <see cref="ObjectId"/> is the stable id the session and audit rows carry.</summary>
internal sealed record SuperAdminMatch(string ObjectId, string? Name, string? Email, SuperAdminMatchKind Kind);

/// <summary>
/// Decides whether a principal the host signed in is a super admin: an exact match on the allowlist, nothing
/// inferred. A principal that matches no entry is not a super admin, however privileged it is in the host.
/// </summary>
internal static class SuperAdminRecognizer
{
    private static readonly string[] ObjectIdClaims = ["oid", "http://schemas.microsoft.com/identity/claims/objectidentifier"];
    private static readonly string[] EmailClaims = ["email", "preferred_username", "upn", ClaimTypes.Email, ClaimTypes.Upn];
    private static readonly string[] NameClaims = ["name", ClaimTypes.Name];
    private static readonly string[] GroupClaims = ["groups"];
    private static readonly string[] SubjectClaims = ["sub", ClaimTypes.NameIdentifier];

    public static SuperAdminMatch? Recognize(ClaimsPrincipal principal, SuperAdminOptions allowed)
    {
        var objectIds = Values(principal, ObjectIdClaims);
        var emails = Values(principal, EmailClaims);
        var groups = Values(principal, GroupClaims);

        // The session and its audit rows need one stable id, so a principal without one cannot be attributed.
        var objectId = objectIds.FirstOrDefault() ?? Values(principal, SubjectClaims).FirstOrDefault();
        if (objectId is null)
        {
            return null;
        }

        var name = Values(principal, NameClaims).FirstOrDefault();
        var email = emails.FirstOrDefault();

        SuperAdminMatchKind? kind = null;
        if (objectIds.Any(v => Listed(allowed.ObjectIds).Contains(v, StringComparer.Ordinal)))
        {
            kind = SuperAdminMatchKind.ObjectId;
        }
        else if (emails.Any(v => Listed(allowed.Emails).Contains(v, StringComparer.OrdinalIgnoreCase)))
        {
            kind = SuperAdminMatchKind.Email;
        }
        else if (groups.Any(v => Listed(allowed.GroupIds).Contains(v, StringComparer.Ordinal)))
        {
            kind = SuperAdminMatchKind.Group;
        }

        return kind is { } k ? new SuperAdminMatch(objectId, name, email, k) : null;
    }

    /// <summary>
    /// Entra leaves the groups out of the token when there are too many and points at a Graph lookup instead.
    /// Nothing here follows that pointer, so such a user can only match by object id or email.
    /// </summary>
    public static bool HasGroupsOverage(ClaimsPrincipal principal) =>
        principal.FindAll("_claim_names").Any(c => c.Value.Contains("groups", StringComparison.OrdinalIgnoreCase));

    public static int ConfiguredEntryCount(SuperAdminOptions options) =>
        Listed(options.ObjectIds).Count() + Listed(options.Emails).Count() + Listed(options.GroupIds).Count();

    // A blank entry must never match a blank claim.
    private static IEnumerable<string> Listed(IEnumerable<string>? entries) =>
        (entries ?? []).Where(e => !string.IsNullOrWhiteSpace(e));

    private static List<string> Values(ClaimsPrincipal principal, string[] types) =>
        principal.Claims.Where(c => types.Contains(c.Type, StringComparer.Ordinal) && !string.IsNullOrEmpty(c.Value))
            .Select(c => c.Value).ToList();
}
