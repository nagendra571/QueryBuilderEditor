using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;

namespace QueryBuilder.Editor.Endpoints;

public static class MeEndpoints
{
    /// <summary>The caller's own identity and row-level scope, for the shell's "your zone" chip. Only
    /// ever describes the requester, so it reveals nothing they couldn't already infer from their data.</summary>
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", async (ICurrentUserService user, ICurrentDataScope scope, CancellationToken ct) =>
        {
            object? zone = null;
            if (scope.IsEnabled)
            {
                var resolved = await scope.GetAsync(ct);
                zone = new
                {
                    unrestricted = resolved.IsUnrestricted,
                    values = resolved.IsUnrestricted
                        ? new Dictionary<string, IReadOnlyList<string>>()
                        : resolved.Values.ToDictionary(kv => kv.Key, kv => kv.Value)
                };
            }

            return Results.Json(new { userId = user.UserId, displayName = user.DisplayName, zone }, QueryBuilderJson.Options);
        }).WithTags("Me");
    }
}
