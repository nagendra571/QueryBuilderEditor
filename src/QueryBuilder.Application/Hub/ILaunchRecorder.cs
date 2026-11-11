namespace QueryBuilder.Application.Hub;

/// <summary>
/// Records one launch attempt. The caller picks the DI scope (and so the app) it runs in; the token is never an input.
/// Resolved directly by the launch endpoint, not through the mediator, so embedded mode has no hub types to wire.
/// </summary>
public interface ILaunchRecorder
{
    /// <summary>False when a row for a case-variant of the same user id exists; nothing is recorded then and the caller must refuse the launch.</summary>
    Task<bool> RecordSuccessAsync(LaunchClaims claims, CancellationToken ct);
    Task RecordRefusalAsync(LaunchRefusalReason reason, Guid? claimedAppId, CancellationToken ct);
}
