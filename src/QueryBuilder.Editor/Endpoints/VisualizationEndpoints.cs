using QueryBuilder.Editor.Hub;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Visualizations;

namespace QueryBuilder.Editor.Endpoints;

public static class VisualizationEndpoints
{
    public static void MapVisualizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/queries/{queryId:guid}/visualizations").WithTags("Visualizations");

        group.MapGet("/", async (Guid queryId, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetVisualizationsQuery(queryId), ct), QueryBuilderJson.Options));

        group.MapPost("/", async (Guid queryId, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<SaveVisualizationRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            var created = await sender.Send(
                new SaveVisualizationCommand(queryId, null, request.Name, request.Type, request.Options), ct);
            return Results.Json(created, QueryBuilderJson.Options);
        });

        group.MapPut("/{visualizationId:guid}", async (Guid queryId, Guid visualizationId, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<SaveVisualizationRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            var updated = await sender.Send(
                new SaveVisualizationCommand(queryId, visualizationId, request.Name, request.Type, request.Options), ct);
            return Results.Json(updated, QueryBuilderJson.Options);
        });

        group.MapDelete("/{visualizationId:guid}", async (Guid queryId, Guid visualizationId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteVisualizationCommand(queryId, visualizationId), ct);
            return Results.NoContent();
        });

        group.MapPost("/{visualizationId:guid}/data", async (Guid queryId, Guid visualizationId, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = http.Request.HasJsonContentType()
                ? await http.Request.ReadFromJsonAsync<RenderVisualizationRequest>(QueryBuilderJson.Options, ct)
                : null;
            var data = await sender.Send(
                new RenderVisualizationCommand(queryId, visualizationId, request?.ParameterValues ?? []), ct);
            return Results.Json(data, QueryBuilderJson.Options);
        }).AllowViewers();
    }
}
