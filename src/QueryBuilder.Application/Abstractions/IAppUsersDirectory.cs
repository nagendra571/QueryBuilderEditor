namespace QueryBuilder.Application.Abstractions;

public sealed record AppUserDto(string Id, string DisplayName, string? Email);

public sealed record AppUsersResult(bool Available, List<AppUserDto> Users);

/// <summary>
/// Looks up the people a query's data source owner has made shareable, by querying a
/// <c>dbo.AppUsers</c> view (Id, DisplayName, Email) in that specific data source's own database —
/// a convention, not a requirement. <see cref="AppUsersResult.Available"/> is false whenever the
/// view doesn't exist there, which is the expected/normal state unless an admin opted in.
/// </summary>
public interface IAppUsersDirectory
{
    Task<AppUsersResult> GetUsersAsync(Guid dataSourceId, CancellationToken cancellationToken);
}
