using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Common;

/// <summary>The caller as access checks see them. Embedded mode has no role, so both flags stay false and
/// visibility is today's owner-or-share rule. <c>InHub</c> is true exactly when the session carries a role, i.e. in
/// hub mode (hub sessions always have one, embedded never does).</summary>
public sealed record AccessActor(string UserId, bool SeesWholeApp, bool IsViewer, bool IsSuperAdmin, bool InHub = false)
{
    public static AccessActor From(ICurrentUserService user) => new(
        user.UserId,
        SeesWholeApp: user.IsSuperAdmin || user.Role == HubRole.Admin,
        IsViewer: !user.IsSuperAdmin && user.Role == HubRole.Viewer,
        IsSuperAdmin: user.IsSuperAdmin,
        InHub: user.Role is not null);
}
