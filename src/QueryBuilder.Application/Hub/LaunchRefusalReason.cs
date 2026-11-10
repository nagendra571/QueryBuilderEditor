namespace QueryBuilder.Application.Hub;

/// <summary>Audit-only: users see a generic page, never one of these.</summary>
public enum LaunchRefusalReason
{
    Malformed, UnknownApp, AppDisabled, UnknownKey, RevokedKey, BadSignature, AlgorithmNotAllowed, WrongAudience,
    Expired, NotYetValid, LifetimeTooLong, Replayed, MissingClaim, UnknownRole, ReturnUrlNotRegistered,
    UndeclaredScopeKey, ScopeMissing, ScopeAndUnrestricted, ScopeTooLarge
}
