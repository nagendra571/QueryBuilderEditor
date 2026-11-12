using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Abstractions;

/// <summary>The people the current app has launched into the hub (always the caller's own app).</summary>
public interface ISeenUserDirectory
{
    Task<List<SeenUser>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The app's stored ids equal to <paramref name="userId"/> ignoring case (the metadata collation does), so
    /// callers can tell an exact match from a case-only variant.</summary>
    Task<IReadOnlyList<string>> MatchingIdsAsync(string userId, CancellationToken cancellationToken);
}
