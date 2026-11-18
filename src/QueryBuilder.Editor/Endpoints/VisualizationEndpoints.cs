using QueryBuilder.Editor.Hub;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Drill;
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
                new RenderVisualizationCommand(queryId, visualizationId, request?.ParameterValues ?? [], request?.DashboardId, request?.TileId), ct);
            return Results.Json(data, QueryBuilderJson.Options);
        }).AllowViewers();

        // Same access as drawing the chart (a Viewer gets it through a dashboard), so it is on the Viewer allowlist too.
        group.MapPost("/{visualizationId:guid}/export", async (Guid queryId, Guid visualizationId, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<ExportVisualizationRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            if (!ExportFormats.TryParse(request.Format, out var format))
            {
                throw new BadHttpRequestException("'format' must be 'csv' or 'xlsx'.");
            }
            var result = await sender.Send(
                new ExportVisualizationCommand(queryId, visualizationId, request.ParameterValues ?? [], format, request.DashboardId, request.TileId), ct);
            return Results.File(result.Content, result.ContentType, result.FileName);
        }).AllowViewers();

        // Rows behind a chart mark, or the chart regrouped by another column. Same access as drawing the chart (a hub Viewer
        // gets it through a dashboard), and the row-level scope applies at every level, so both are on the Viewer allowlist.
        group.MapPost("/{visualizationId:guid}/drill", async (Guid queryId, Guid visualizationId, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<DrillRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            var data = await sender.Send(new DrillVisualizationCommand(queryId, visualizationId, request), ct);
            return Results.Json(data, QueryBuilderJson.Options);
        }).AllowViewers();

        group.MapPost("/{visualizationId:guid}/drill/export", async (Guid queryId, Guid visualizationId, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<DrillExportRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            if (!ExportFormats.TryParse(request.Format, out var format))
            {
                throw new BadHttpRequestException("'format' must be 'csv' or 'xlsx'.");
            }
            var drill = new DrillRequest(
                request.Action, request.Steps, request.BreakdownColumn, request.CrossFilters, request.ParameterValues, request.DashboardId, request.TileId);
            var result = await sender.Send(new ExportDrillCommand(queryId, visualizationId, drill, format), ct);
            return Results.File(result.Content, result.ContentType, result.FileName);
        }).AllowViewers();
    }
}
