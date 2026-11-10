namespace QueryBuilder.Application.Hub;

/// <summary>
/// Records one launch attempt. The caller picks the DI scope (and so the app) it runs in; the token is never an input.
/// Resolved directly by the launch endpoint, not through the mediator, so embedded mode has no hub types to wire.
/// </summary>
public interface ILaunchRecorder
{
    Task RecordSuccessAsync(LaunchClaims claims, CancellationToken ct);
    Task RecordRefusalAsync(LaunchRefusalReason reason, Guid? claimedAppId, CancellationToken ct);
}
