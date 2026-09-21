using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSources;

namespace QueryBuilder.Editor.Endpoints;

public static class DataSourceEndpoints
{
    public static void MapDataSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data-sources").WithTags("Data Sources");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetDataSourcesQuery(), ct)));

        group.MapGet("/{id:guid}/catalog", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetCatalogQuery(id), ct)));
    }
}
