namespace QueryBuilder.Editor.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin");

        // Trivial 204 if the caller passes AdminAuthorization — lets the SPA decide whether to
        // show the Admin nav item without needing a real admin payload.
        group.MapGet("/access", () => Results.NoContent());

        return group;
    }
}
