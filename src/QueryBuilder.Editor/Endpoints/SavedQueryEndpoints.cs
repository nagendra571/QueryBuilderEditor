using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Commands.SavedQueries;
using QueryBuilder.Application.DataSources;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Queries.SavedQueries;

namespace QueryBuilder.Editor.Endpoints;

public static class SavedQueryEndpoints
{
    public static void MapSavedQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/queries").WithTags("Saved Queries");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetSavedQueriesQuery(), ct)));

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetSavedQueryByIdQuery(id), ct)));

        group.MapPost("/", async (SaveQueryRequest request, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(new SaveQueryCommand(request.Id, request.Name, request.Description, request.DataSourceId, request.Definition), ct);
            return Results.Ok(new { id });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteSavedQueryCommand(id), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/favorite", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var isFavorite = await sender.Send(new ToggleFavoriteCommand(id), ct);
            return Results.Ok(new { isFavorite });
        });

        group.MapPost("/preview-sql", async (RunQueryRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new BuildQuerySqlQuery(request.DataSourceId, request.Definition), ct)));

        group.MapPost("/run", async (RunQueryRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new RunQueryCommand(request.DataSourceId, request.Definition, request.ParameterValues, request.MaxRows, request.SavedQueryId), ct)));

        group.MapPost("/export", async (ExportQueryRequest request, ISender sender, CancellationToken ct) =>
        {
            var format = Enum.Parse<ExportFormat>(request.Format, ignoreCase: true);
            var result = await sender.Send(
                new ExportQueryCommand(request.DataSourceId, request.Definition, request.ParameterValues, format, request.FileName, request.SavedQueryId), ct);
            return Results.File(result.Content, result.ContentType, result.FileName);
        });
    }
}
