using QueryBuilder.Application.Abstractions;

namespace QueryBuilder.Editor.DataScoping;

/// <summary>
/// Resolves the current request's row-level data scope via the host's resolver, once per request
/// (cached on this scoped instance, like <see cref="Identity.CurrentUserService"/>). Unlike the
/// actor resolver, exceptions ARE caught here: the safe failure for a scope resolver is "denied",
/// and a resolver that throws for one user shouldn't take the whole page down for them either.
/// </summary>
public sealed class CurrentDataScopeService(
    IHttpContextAccessor httpContextAccessor,
    QueryBuilderEditorOptions editorOptions,
    ILogger<CurrentDataScopeService> logger)
    : ICurrentDataScope
{
    private ResolvedDataScope? _resolved;

    public bool IsEnabled => editorOptions.DataScope.IsEnabled;

    public IReadOnlyList<string> DeclaredKeys => editorOptions.DataScope.Keys.ToList();

    public async Task<ResolvedDataScope> GetAsync(CancellationToken cancellationToken)
    {
        return _resolved ??= await ResolveAsync();
    }

    private async Task<ResolvedDataScope> ResolveAsync()
    {
        var options = editorOptions.DataScope;
        if (!options.IsEnabled)
        {
            return ResolvedDataScope.Unrestricted;
        }

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return ResolvedDataScope.Denied;
        }

        DataScope? scope;
        try
        {
            scope = options.ResolverAsync is not null
                ? await options.ResolverAsync(httpContext)
                : options.Resolver!(httpContext);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "QueryBuilder data-scope resolver threw; treating the request as denied (only 'not scoped' objects visible).");
            return ResolvedDataScope.Denied;
        }

        if (scope is null)
        {
            return ResolvedDataScope.Denied;
        }

        return scope.IsUnrestricted
            ? ResolvedDataScope.Unrestricted
            : new ResolvedDataScope(false, new Dictionary<string, IReadOnlyList<string>>(scope.Values, StringComparer.OrdinalIgnoreCase));
    }
}
