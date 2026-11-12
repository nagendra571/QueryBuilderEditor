using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Editor.Hub;

namespace QueryBuilder.Editor.Endpoints;

/// <summary>
/// Super-admin routes about data sources across apps: handing one to another app, and the encryption key, which is shared
/// by every app. Each handler gets its hub-wide scope from IHubWideScopeFactory, never from the request.
/// </summary>
internal static class HubDataSourceEndpoints
{
    public const string Path = QueryBuilderRoutes.BasePath + "/api/hub";

    private sealed record MoveRequest(Guid? TargetAppId);

    public static void MapHubDataSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).RequireSuperAdmin().WithTags("Hub data sources");

        group.MapPost("/apps/{appId:guid}/data-sources/{id:guid}/move", async (Guid appId, Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<MoveRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            return Results.Json(await sender.Send(new MoveDataSourceCommand(appId, id, request.TargetAppId), ct), QueryBuilderJson.Options);
        });

        group.MapGet("/encryption-key", async (IHubWideScopeFactory factory, CancellationToken ct) =>
        {
            await using var scope = factory.Create();
            var status = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetEncryptionKeyStatusQuery(), ct);
            // Super admins are always signed in, so the anonymous-admin caution behind the embedded flag does not apply.
            return Results.Json(status with { CanReencrypt = status.KeyConfigured }, QueryBuilderJson.Options);
        });

        group.MapPost("/encryption-key/reencrypt", async (IHubWideScopeFactory factory, CancellationToken ct) =>
        {
            await using var scope = factory.Create();
            return Results.Json(await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ReencryptDataSourcesCommand(), ct), QueryBuilderJson.Options);
        });
    }
}
