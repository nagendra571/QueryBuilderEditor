using QueryBuilder.Application.Commands.Admin;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Queries.Admin;

namespace QueryBuilder.Editor.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin");

        // Trivial 204 if the caller passes AdminAuthorization — lets the SPA decide whether to
        // show the Admin nav item without needing a real admin payload.
        group.MapGet("/access", () => Results.NoContent());

        group.MapGet("/data-source-management", (DataSourceManagementSettings settings) =>
            Results.Json(new { enabled = settings.Enabled }, QueryBuilderJson.Options));

        // Every mutating or testing route answers 404 while the feature is off, before it reads a body.
        group.MapPost("/data-sources", async (HttpContext http, DataSourceManagementSettings settings, ISender sender, CancellationToken ct) =>
        {
            if (!settings.Enabled) return Results.NotFound();
            var request = await http.Request.ReadFromJsonAsync<CreateDataSourceRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            var created = await sender.Send(
                new CreateDataSourceCommand(request.Name, request.Description, request.Provider, request.Connection), ct);
            return Results.Json(created, QueryBuilderJson.Options, statusCode: StatusCodes.Status201Created);
        });

        group.MapPut("/data-sources/{id:guid}", async (Guid id, HttpContext http, DataSourceManagementSettings settings, ISender sender, CancellationToken ct) =>
        {
            if (!settings.Enabled) return Results.NotFound();
            var request = await http.Request.ReadFromJsonAsync<UpdateDataSourceRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            await sender.Send(new UpdateDataSourceCommand(id, request.Name, request.Description, request.Connection), ct);
            return Results.NoContent();
        });

        group.MapPut("/data-sources/{id:guid}/active", async (Guid id, HttpContext http, DataSourceManagementSettings settings, ISender sender, CancellationToken ct) =>
        {
            if (!settings.Enabled) return Results.NotFound();
            var request = await http.Request.ReadFromJsonAsync<SetDataSourceActiveRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            await sender.Send(new SetDataSourceActiveCommand(id, request.IsActive), ct);
            return Results.NoContent();
        });

        group.MapPost("/data-sources/test", async (HttpContext http, DataSourceManagementSettings settings, ISender sender, CancellationToken ct) =>
        {
            if (!settings.Enabled) return Results.NotFound();
            var request = await http.Request.ReadFromJsonAsync<TestConnectionRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            return Results.Json(
                await sender.Send(new TestConnectionCommand(request.Provider, request.Connection, request.ExistingId), ct),
                QueryBuilderJson.Options);
        });

        group.MapPost("/data-sources/{id:guid}/test", async (Guid id, DataSourceManagementSettings settings, ISender sender, CancellationToken ct) =>
        {
            if (!settings.Enabled) return Results.NotFound();
            return Results.Json(await sender.Send(new TestStoredConnectionCommand(id), ct), QueryBuilderJson.Options);
        });

        // Not gated on Enabled, unlike the routes above: a host may keep the add/edit form off and still rotate its key.
        // Re-encrypting has its own gate (ReencryptAllowed), set at startup.
        group.MapGet("/encryption-key", async (DataSourceManagementSettings settings, ISender sender, CancellationToken ct) =>
        {
            var status = await sender.Send(new GetEncryptionKeyStatusQuery(), ct);
            return Results.Json(status with { CanReencrypt = settings.ReencryptAllowed }, QueryBuilderJson.Options);
        });

        group.MapPost("/encryption-key/reencrypt", async (DataSourceManagementSettings settings, ISender sender, CancellationToken ct) =>
        {
            if (!settings.ReencryptAllowed) return Results.NotFound();
            return Results.Json(await sender.Send(new ReencryptDataSourcesCommand(), ct), QueryBuilderJson.Options);
        });

        group.MapGet("/data-sources", async (ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAdminDataSourcesQuery(), ct), QueryBuilderJson.Options));

        group.MapGet("/data-sources/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAdminDataSourceDetailQuery(id), ct), QueryBuilderJson.Options));

        group.MapPut("/data-sources/{id:guid}/catalog-policy", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<UpdateCatalogPolicyRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            await sender.Send(new UpdateCatalogPolicyCommand(id, request.CatalogScope, request.AllowedObjects), ct);
            return Results.NoContent();
        });

        group.MapGet("/data-sources/{id:guid}/data-scope", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetDataScopeQuery(id), ct), QueryBuilderJson.Options));

        group.MapPut("/data-sources/{id:guid}/data-scope", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<UpdateDataScopeRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            await sender.Send(new UpdateDataScopeCommand(id, request.Objects ?? []), ct);
            return Results.NoContent();
        });

        group.MapPut("/data-sources/{id:guid}/record-limit", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<UpdateRecordLimitRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            await sender.Send(new UpdateRecordLimitCommand(id, request.MaxRecords), ct);
            return Results.NoContent();
        });

        return group;
    }
}
