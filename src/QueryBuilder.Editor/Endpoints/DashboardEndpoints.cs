using QueryBuilder.Editor.Hub;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dashboards;
using QueryBuilder.Application.Dtos;

namespace QueryBuilder.Editor.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/visualizations/pickable", async (ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetPickableVisualizationsQuery(), ct), QueryBuilderJson.Options))
            .WithTags("Dashboards");

        var group = app.MapGroup("/api/dashboards").WithTags("Dashboards");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetDashboardsQuery(), ct), QueryBuilderJson.Options))
            .AllowViewers();

        group.MapPost("/", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<SaveDashboardRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            return Results.Json(await sender.Send(new CreateDashboardCommand(request.Name, request.Description), ct), QueryBuilderJson.Options);
        });

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetDashboardQuery(id), ct), QueryBuilderJson.Options))
            .AllowViewers();

        group.MapPut("/{id:guid}/published", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<PublishDashboardRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            await sender.Send(
                new PublishDashboardCommand(id, request.Published ?? throw new BadHttpRequestException("'published' is required.")), ct);
            return Results.NoContent();
        });

        group.MapPut("/{id:guid}", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<SaveDashboardRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            return Results.Json(
                await sender.Send(new RenameDashboardCommand(id, request.Name, request.Description, request.Version), ct),
                QueryBuilderJson.Options);
        });

        group.MapPut("/{id:guid}/tiles", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<SaveDashboardTilesRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            return Results.Json(
                await sender.Send(new SaveDashboardTilesCommand(id, request.Version, request.Tiles ?? [], request.Parameters), ct),
                QueryBuilderJson.Options);
        });

        group.MapPost("/{id:guid}/tiles/chart", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<AddChartToDashboardRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            return Results.Json(
                await sender.Send(new AddChartToDashboardCommand(id, request.VisualizationId), ct),
                QueryBuilderJson.Options);
        });

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteDashboardCommand(id), ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/shares", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetDashboardSharesQuery(id), ct), QueryBuilderJson.Options));

        group.MapPost("/{id:guid}/shares", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<ShareDashboardRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            var shareId = await sender.Send(
                new ShareDashboardCommand(id, request.SharedWithUserId, request.SharedWithUserEmail, request.AccessLevel, request.AlsoShareQueryIds),
                ct);
            return Results.Json(new { id = shareId }, QueryBuilderJson.Options);
        });

        group.MapDelete("/{id:guid}/shares/{shareId:guid}", async (Guid id, Guid shareId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RemoveDashboardShareCommand(id, shareId), ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/share-preview", async (Guid id, string userId, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetDashboardSharePreviewQuery(id, userId), ct), QueryBuilderJson.Options));
    }
}
