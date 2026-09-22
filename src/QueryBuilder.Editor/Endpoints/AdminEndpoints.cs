using QueryBuilder.Application.Common;
using QueryBuilder.Application.Queries.Admin;

namespace QueryBuilder.Editor.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin");

        // Trivial 204 if the caller passes AdminAuthorization — lets the SPA decide whether to
        // show the Admin nav item without needing a real admin payload.
        group.MapGet("/access", () => Results.NoContent());

        group.MapGet("/data-sources", async (ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAdminDataSourcesQuery(), ct), QueryBuilderJson.Options));

        group.MapGet("/data-sources/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAdminDataSourceDetailQuery(id), ct), QueryBuilderJson.Options));

        return group;
    }
}
