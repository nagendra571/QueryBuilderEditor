using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Editor.Authorization;
using QueryBuilder.Editor.Endpoints;

namespace QueryBuilder.Editor;

public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps every QueryBuilder API endpoint (data sources, saved queries, audit log) and applies
    /// the access control configured via <c>options.Authorization</c> in <c>AddQueryBuilderEditor</c>
    /// to all of them uniformly — no per-endpoint changes needed. For anything other than
    /// <see cref="QueryBuilderAuthorizationMode.Anonymous"/>, your pipeline must call, in order,
    /// <c>app.UseAuthentication()</c> then <c>app.UseAuthorization()</c> before this is reached.
    /// Once registered, visit <c>GET /_setup</c> in Development to verify every integration
    /// requirement at once — it works independently of whether this method was even called.
    /// </summary>
    public static IEndpointRouteBuilder MapQueryBuilderEditor(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<QueryBuilderEditorOptions>();

        // An empty-prefix group is a pure convention carrier: nested route groups keep their own
        // paths untouched, but RequireAuthorization applied here cascades to every one of them.
        var group = endpoints.MapGroup(string.Empty);
        ApplyAuthorization(group, options.Authorization);

        group.MapDataSourceEndpoints();
        group.MapSavedQueryEndpoints();
        group.MapAuditEndpoints();

        return endpoints;
    }

    private static void ApplyAuthorization(RouteGroupBuilder group, QueryBuilderAuthorizationOptions auth)
    {
        if (!string.IsNullOrWhiteSpace(auth.PolicyName))
        {
            group.RequireAuthorization(auth.PolicyName);
            return;
        }

        switch (auth.Mode)
        {
            case QueryBuilderAuthorizationMode.Authenticated:
                group.RequireAuthorization();
                break;

            case QueryBuilderAuthorizationMode.Role:
                if (auth.RoleNames.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"{nameof(QueryBuilderAuthorizationOptions)}.{nameof(QueryBuilderAuthorizationOptions.RoleNames)} must contain at least one role when Mode is Role.");
                }
                group.RequireAuthorization(new AuthorizeAttribute { Roles = string.Join(',', auth.RoleNames) });
                break;

            case QueryBuilderAuthorizationMode.Anonymous:
            default:
                break;
        }
    }
}
