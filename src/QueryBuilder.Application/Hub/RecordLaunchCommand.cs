using QueryBuilder.Application.Common;

namespace QueryBuilder.Application.Hub;

/// <summary>
/// Records one launch attempt: for an accepted launch the seen-user upsert and the audit entry, for a refused one
/// only the audit entry. The caller picks the DI scope (and so the app) this runs in; the token is never an input.
/// </summary>
public sealed record RecordLaunchCommand(LaunchResult Result) : IRequest<Unit>;

public interface ILaunchRecorder
{
    Task RecordSuccessAsync(LaunchClaims claims, CancellationToken ct);
    Task RecordRefusalAsync(LaunchRefusalReason reason, Guid? appId, CancellationToken ct);
}

public sealed class RecordLaunchCommandHandler(ILaunchRecorder recorder) : IRequestHandler<RecordLaunchCommand, Unit>
{
    public async Task<Unit> Handle(RecordLaunchCommand request, CancellationToken cancellationToken)
    {
        if (request.Result.Claims is { } claims)
        {
            await recorder.RecordSuccessAsync(claims, cancellationToken);
        }
        else
        {
            await recorder.RecordRefusalAsync(request.Result.Refusal ?? LaunchRefusalReason.Malformed, request.Result.AppId, cancellationToken);
        }

        return Unit.Value;
    }
}
