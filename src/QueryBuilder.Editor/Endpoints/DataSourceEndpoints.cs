using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSources;

namespace QueryBuilder.Editor.Endpoints;

public static class DataSourceEndpoints
{
    public static void MapDataSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data-sources").WithTags("Data Sources");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetDataSourcesQuery(), ct), QueryBuilderJson.Options));

        group.MapGet("/{id:guid}/catalog", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetCatalogQuery(id), ct), QueryBuilderJson.Options));

        group.MapGet("/{id:guid}/app-users", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAppUsersQuery(id), ct), QueryBuilderJson.Options));
    }
}
