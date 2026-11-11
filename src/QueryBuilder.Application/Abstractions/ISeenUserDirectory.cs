using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Abstractions;

/// <summary>The people the current app has launched into the hub (always the caller's own app).</summary>
public interface ISeenUserDirectory
{
    Task<List<SeenUser>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Exact (ordinal) match: the metadata collation ignores case, so a case-only difference must not count.</summary>
    Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken);
}
