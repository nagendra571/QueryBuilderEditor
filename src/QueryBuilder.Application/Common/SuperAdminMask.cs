using System.Diagnostics.CodeAnalysis;

namespace QueryBuilder.Application.Common;

/// <summary>Staff object ids are not the app's business (hub Decision 2): inside an app, only a super admin sees who the staff member was.</summary>
public static class SuperAdminMask
{
    public const string Label = "Super admin";
    public const string Prefix = "super:";

    public static bool IsSuperAdminId(string? id) => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);

    private static bool Hides(AccessActor viewer) => viewer is { InHub: true, IsSuperAdmin: false };

    [return: NotNullIfNotNull(nameof(id))]
    public static string? Actor(string? id, AccessActor viewer) => Hides(viewer) && IsSuperAdminId(id) ? Label : id;

    /// <summary>The name stays as stored unless the owner is staff, whose name may be their object id or real name.</summary>
    public static string Name(string ownerId, string name, AccessActor viewer) => Hides(viewer) && IsSuperAdminId(ownerId) ? Label : name;
}
