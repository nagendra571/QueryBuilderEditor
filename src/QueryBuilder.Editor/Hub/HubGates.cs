using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Editor.Hub;

/// <summary>
/// Hub-mode access rules, as endpoint filters on the route groups. They replace the host's own Authorization and
/// AdminAuthorization options in hub mode (those describe the host's users, not hub sessions). They read the session the
/// startup filter already resolved (<see cref="IAppSessionSource"/>) and never authenticate again: a second attempt could
/// rethrow the very session-read failure the startup filter swallowed, turning "no session" into a 500.
/// Filters, not authorization policies, so a host that never calls UseAuthorization still gets them.
/// </summary>
internal static class HubGates
{
    private static HubSessionInfo? Session(EndpointFilterInvocationContext context) =>
        context.HttpContext.RequestServices.GetRequiredService<IAppSessionSource>().Current;

    private static IResult Unauthorized() => Results.StatusCode(StatusCodes.Status401Unauthorized);
    private static IResult Forbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);

    /// <summary>Any valid hub session, even a super admin who has not picked an app yet.</summary>
    public static TBuilder RequireHubSession<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) => Session(context) is null ? Unauthorized() : await next(context));

    /// <summary>A hub session that resolves to an app: an app session, or a super admin viewing one.</summary>
    public static TBuilder RequireHubApp<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
            Session(context) is { AppId: not null } ? await next(context) : Unauthorized());

    /// <summary>An app Admin, or a super admin (who always acts as one inside the app they view).</summary>
    public static TBuilder RequireHubAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) => Session(context) switch
        {
            null => Unauthorized(),
            { IsSuperAdmin: true } or { Role: HubRole.Admin } => await next(context),
            _ => Forbidden(),
        });

    /// <summary>A hub Viewer reaches only endpoints marked <see cref="ViewerAllowedMetadata"/>; the refusal comes before any
    /// handler runs. A super admin acts as Admin and is never a Viewer.</summary>
    public static TBuilder RequireNotViewerUnlessAllowed<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
            Session(context) is { IsSuperAdmin: false, Role: HubRole.Viewer }
            && context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ViewerAllowedMetadata>() is null
                ? Forbidden()
                : await next(context));

    public static TBuilder RequireSuperAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) => Session(context) switch
        {
            null => Unauthorized(),
            { IsSuperAdmin: true } => await next(context),
            _ => Forbidden(),
        });
}
