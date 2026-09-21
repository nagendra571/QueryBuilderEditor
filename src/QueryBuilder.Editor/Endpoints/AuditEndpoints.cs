using QueryBuilder.Application.Audit;
using QueryBuilder.Application.Common;

namespace QueryBuilder.Editor.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit").WithTags("Audit");

        group.MapGet("/", async (string? entityType, Guid? entityId, Guid? dataSourceId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetAuditLogQuery(entityType, entityId, dataSourceId), ct)));
    }
}
