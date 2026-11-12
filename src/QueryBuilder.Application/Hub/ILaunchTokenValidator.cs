namespace QueryBuilder.Application.Hub;

public interface ILaunchTokenValidator
{
    /// <summary>Never throws for a bad token; the refusal carries the reason. The token is not logged or kept.</summary>
    Task<LaunchResult> ValidateAsync(string token, CancellationToken ct);
}

/// <summary>The hub's own audience, which every launch token's <c>aud</c> must equal.</summary>
public sealed record LaunchValidationSettings(string Audience);
