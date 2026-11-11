using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

/// <summary>Reads through the request's own app filter, so it can only ever return the caller's app.</summary>
public sealed class SeenUserDirectory(AppDbContext db) : ISeenUserDirectory
{
    private const int MaxPeople = 2000;

    public Task<List<SeenUser>> ListAsync(CancellationToken cancellationToken) =>
        db.SeenUsers.AsNoTracking().OrderByDescending(u => u.LastLaunchUtc).Take(MaxPeople).ToListAsync(cancellationToken);

    public async Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken)
    {
        // The column's collation ignores case, so the SQL match is only a narrowing step.
        var matches = await db.SeenUsers.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.UserId).ToListAsync(cancellationToken);
        return matches.Any(m => string.Equals(m, userId, StringComparison.Ordinal));
    }
}
