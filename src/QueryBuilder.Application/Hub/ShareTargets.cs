using QueryBuilder.Application.Abstractions;

namespace QueryBuilder.Application.Hub;

/// <summary>
/// Who a share may name in hub mode: a person the app has launched into the hub, or a row of the relevant data source's
/// <c>AppUsers</c> view. The server checks this itself, so a hand-made request cannot share with an arbitrary id.
/// </summary>
public sealed class ShareTargets(ISeenUserDirectory seenUsers, IAppUsersDirectory directory)
{
    public const string LaunchSource = "launch";
    public const string DirectorySource = "directory";
    public const string NotKnownMessage = "Choose someone from the list.";

    public async Task<List<AppUserDto>> PeopleAsync(CancellationToken ct)
    {
        var seen = await seenUsers.ListAsync(ct);
        return seen
            .Select(u => new AppUserDto(u.UserId, u.Name ?? u.Email ?? u.UserId, u.Email, LaunchSource))
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Launched people first, then directory rows that are neither the same id (ordinal) nor the same email (ignoring case).</summary>
    public async Task<List<AppUserDto>> MergedAsync(Guid dataSourceId, CancellationToken ct)
    {
        var merged = await PeopleAsync(ct);
        var ids = new HashSet<string>(merged.Select(u => u.Id), StringComparer.Ordinal);
        var emails = new HashSet<string>(merged.Where(u => !string.IsNullOrWhiteSpace(u.Email)).Select(u => u.Email!), StringComparer.OrdinalIgnoreCase);

        var fromDirectory = await directory.GetUsersAsync(dataSourceId, ct);
        foreach (var user in fromDirectory.Users)
        {
            var hasEmail = !string.IsNullOrWhiteSpace(user.Email);
            if (ids.Contains(user.Id) || (hasEmail && emails.Contains(user.Email!)))
            {
                continue;
            }

            ids.Add(user.Id);
            if (hasEmail)
            {
                emails.Add(user.Email!);
            }

            merged.Add(user with { Source = DirectorySource });
        }

        return merged.OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.Id, StringComparer.Ordinal).ToList();
    }

    public async Task<bool> IsKnownAsync(string userId, IEnumerable<Guid> dataSourceIds, CancellationToken ct)
    {
        if (await seenUsers.ExistsAsync(userId, ct))
        {
            return true;
        }

        foreach (var dataSourceId in dataSourceIds.Distinct())
        {
            var fromDirectory = await directory.GetUsersAsync(dataSourceId, ct);
            if (fromDirectory.Users.Any(u => string.Equals(u.Id, userId, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
