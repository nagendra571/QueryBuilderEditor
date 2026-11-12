using QueryBuilder.Editor.Hub;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Hub.Registry;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Editor.Endpoints;

/// <summary>
/// The app registry: super admins only (the group's gate), and every handler gets its hub-wide scope from
/// IHubWideScopeFactory, never from the request. Nothing here deletes an app, a key or a URL's history: keys are revoked.
/// </summary>
internal static class HubRegistryEndpoints
{
    public const string Path = QueryBuilderRoutes.BasePath + "/api/hub/apps";

    private sealed record CreateAppRequest(string? Key, string? Name);
    private sealed record UpdateAppRequest(int? Version, string? Name, bool? IsEnabled, List<string>? ScopeKeys, int? MaxRecords, bool? AppAdminsCanManageConnections);
    private sealed record AddKeyRequest(string? Kid, string? Algorithm, string? PublicKeyPem);
    private sealed record ReturnUrlsRequest(List<string>? Urls);

    public static void MapHubRegistryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup(QueryBuilderRoutes.BasePath + "/api/hub/audit").RequireSuperAdmin().WithTags("Hub registry")
            .MapGet("", async (Guid? appId, AuditAction? action, DateTimeOffset? before, int? take, ISender sender, CancellationToken ct) =>
                Results.Json(await sender.Send(new GetHubAuditQuery(appId, action, before, take), ct), QueryBuilderJson.Options));

        var group = endpoints.MapGroup(Path).RequireSuperAdmin().WithTags("Hub registry");

        group.MapGet("", async (ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAppsQuery(), ct), QueryBuilderJson.Options));

        group.MapPost("", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await Read<CreateAppRequest>(http, ct);
            var created = await sender.Send(new CreateAppCommand(request.Key, request.Name), ct);
            return Results.Json(created, QueryBuilderJson.Options, statusCode: StatusCodes.Status201Created);
        });

        group.MapPut("/{id:guid}", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await Read<UpdateAppRequest>(http, ct);
            // A missing flag is refused rather than read as "off", which would disable an app by omission.
            var isEnabled = request.IsEnabled ?? throw new BadHttpRequestException("isEnabled is required.");
            var version = request.Version ?? throw new BadHttpRequestException("version is required.");
            var canManage = request.AppAdminsCanManageConnections ?? throw new BadHttpRequestException("appAdminsCanManageConnections is required.");
            return Results.Json(await sender.Send(new UpdateAppCommand(id, version, request.Name, isEnabled, request.ScopeKeys, request.MaxRecords, canManage), ct), QueryBuilderJson.Options);
        });

        group.MapPost("/{id:guid}/keys", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await Read<AddKeyRequest>(http, ct);
            var created = await sender.Send(new AddSigningKeyCommand(id, request.Kid, request.Algorithm, request.PublicKeyPem), ct);
            return Results.Json(created, QueryBuilderJson.Options, statusCode: StatusCodes.Status201Created);
        });

        group.MapDelete("/{id:guid}/keys/{kid}", async (Guid id, string kid, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RevokeSigningKeyCommand(id, kid), ct);
            return Results.NoContent();
        });

        group.MapPut("/{id:guid}/return-urls", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await Read<ReturnUrlsRequest>(http, ct);
            return Results.Json(await sender.Send(new SetReturnUrlsCommand(id, request.Urls), ct), QueryBuilderJson.Options);
        });
    }

    private static async Task<T> Read<T>(HttpContext http, CancellationToken ct) where T : class =>
        await http.Request.ReadFromJsonAsync<T>(QueryBuilderJson.Options, ct)
            ?? throw new BadHttpRequestException("Request body is required.");
}
