using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Hub;

/// <summary>Claims taken from a launch token that passed every check; nothing here is read from the token before that.</summary>
public sealed record LaunchClaims(
    Guid AppId,
    string AppKey,
    string UserId,
    string? Name,
    string? Email,
    HubRole Role,
    ResolvedDataScope Scope,
    string? ReturnUrl,
    string Jti,
    DateTimeOffset ExpiresUtc);

/// <summary>Either claims or a refusal. <see cref="AppId"/> is set whenever the app was identified, even for a refusal, for the audit entry.</summary>
public sealed record LaunchResult(LaunchClaims? Claims, LaunchRefusalReason? Refusal, Guid? AppId)
{
    public static LaunchResult Accepted(LaunchClaims claims) => new(claims, null, claims.AppId);
    public static LaunchResult Refused(LaunchRefusalReason reason, Guid? appId = null) => new(null, reason, appId);
}
