using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Abstractions;

/// <summary>What the signed-in hub session says about its caller. Super admins carry the app they chose to view, if any.</summary>
public sealed record HubSessionInfo(
    bool IsSuperAdmin,
    Guid? AppId,
    string UserId,
    string DisplayName,
    string? Email,
    HubRole Role,
    ResolvedDataScope Scope,
    string? ReturnUrl);

/// <summary>Where hub mode learns which app the current request belongs to. Null means no valid session.</summary>
public interface IAppSessionSource
{
    HubSessionInfo? Current { get; }
}
