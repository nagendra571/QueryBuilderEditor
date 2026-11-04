using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Parameters;

namespace QueryBuilder.Editor.Endpoints;

public static class ParameterEndpoints
{
    public static void MapParameterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/parameters").WithTags("Parameters");

        // For a parameter still being edited in the settings dialog. The caller must be able to view
        // the source query, exactly as for a saved parameter's options.
        group.MapPost("/options", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<ParameterOptionsPreviewRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            return Results.Json(await sender.Send(new PreviewParameterOptionsQuery(request.Parameter), ct), QueryBuilderJson.Options);
        });
    }
}
