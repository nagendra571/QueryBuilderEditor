using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Editor.Authorization;
using QueryBuilder.Editor.Endpoints;
using QueryBuilder.Editor.Hub;

namespace QueryBuilder.Editor;

public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps every QueryBuilder API endpoint (data sources, saved queries, audit log) and applies
    /// the access control configured via <c>options.Authorization</c> in <c>AddQueryBuilderEditor</c>
    /// to all of them uniformly — no per-endpoint changes needed. For anything other than
    /// <see cref="QueryBuilderAuthorizationMode.Anonymous"/>, your pipeline must call, in order,
    /// <c>app.UseAuthentication()</c> then <c>app.UseAuthorization()</c> before this is reached.
    /// Every route is mapped under <c>/querybuilder</c> (e.g. <c>/querybuilder/api/data-sources</c>)
    /// so QueryBuilder never collides with routes your own app owns. Once registered, visit
    /// <c>GET /_setup</c> in Development to verify every integration requirement at once — it
    /// works independently of whether this method was even called.
    /// </summary>
    public static IEndpointRouteBuilder MapQueryBuilderEditor(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<QueryBuilderEditorOptions>();

        if (options.Hub.Enabled)
        {
            endpoints.MapHubLaunchEndpoints();
            endpoints.MapHubSuperAdminEndpoints();
            endpoints.MapHubRegistryEndpoints();
            // Any session, including a super admin who has not chosen an app yet: the shell needs this to offer the choice.
            endpoints.MapGroup(QueryBuilderRoutes.BasePath).RequireHubSession().MapMeEndpoints(options.Hub.Enabled);
        }

        var group = endpoints.MapGroup(QueryBuilderRoutes.BasePath);
        // Hub sessions replace the host's own gates; embedded mode keeps them exactly as before.
        if (options.Hub.Enabled)
        {
            group.RequireHubApp();
        }
        else
        {
            ApplyAuthorization(group, options.Authorization);
        }

        group.MapDataSourceEndpoints();
        group.MapSavedQueryEndpoints();
        group.MapParameterEndpoints();
        group.MapVisualizationEndpoints();
        group.MapDashboardEndpoints();
        if (!options.Hub.Enabled)
        {
            group.MapMeEndpoints(false);
        }

        group.MapAuditEndpoints();

        var adminGroup = group.MapAdminEndpoints();
        if (options.Hub.Enabled)
        {
            adminGroup.RequireHubAdmin();
        }
        else
        {
            ApplyAuthorization(adminGroup, options.AdminAuthorization);
        }

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
