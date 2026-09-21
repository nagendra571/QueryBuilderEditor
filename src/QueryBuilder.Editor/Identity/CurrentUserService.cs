using QueryBuilder.Application.Abstractions;
using QueryBuilder.Editor;

namespace QueryBuilder.Editor.Identity;

/// <summary>
/// Resolves the acting user for the current request via, in order: the host's configured
/// <see cref="QueryBuilderEditorOptions.ActorResolver"/>, the authenticated principal's name,
/// then "anonymous". Resolved once per request and cached on this scoped instance, matching
/// TemplateBuilder.Editor's ActorResolver contract.
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor, QueryBuilderEditorOptions editorOptions)
    : ICurrentUserService
{
    private const int ActorMaxLength = 450;
    private const string AnonymousActor = "anonymous";

    private string? _resolvedActor;

    public string UserId => ResolveActor();
    public string DisplayName => ResolveActor();
    public string? Email => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true
        ? httpContextAccessor.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        : null;

    private string ResolveActor()
    {
        if (_resolvedActor is not null)
        {
            return _resolvedActor;
        }

        var httpContext = httpContextAccessor.HttpContext;

        // Exceptions from a host-supplied resolver are intentionally not caught here — they should
        // fail the request loudly rather than silently falling back to "anonymous".
        var fromResolver = httpContext is not null ? editorOptions.ActorResolver?.Invoke(httpContext) : null;

        var actor = FirstNonBlank(fromResolver, httpContext?.User?.Identity?.Name) ?? AnonymousActor;
        _resolvedActor = actor.Length > ActorMaxLength ? actor[..ActorMaxLength] : actor;
        return _resolvedActor;
    }

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
