using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Infrastructure.Persistence;

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
    ILogger<CurrentDataScopeService> logger,
    IAppSessionSource? hubSession = null,
    IServiceProvider? services = null)
    : ICurrentDataScope
{
    private ResolvedDataScope? _resolved;

    private IReadOnlyList<string>? _appKeys;

    public bool IsEnabled => editorOptions.Hub.Enabled || editorOptions.DataScope.IsEnabled;

    // Hub mode has no host options: the keys are the app's own, read once per request (a sync read, because the
    // interface is a property; one tiny indexed row).
    public IReadOnlyList<string> HostKeys => editorOptions.Hub.Enabled ? AppKeys() : editorOptions.DataScope.Keys.ToList();

    private IReadOnlyList<string> AppKeys()
    {
        if (_appKeys is not null)
        {
            return _appKeys;
        }

        if (hubSession?.Current is not { AppId: { } appId } || services?.GetService(typeof(AppDbContext)) is not AppDbContext db)
        {
            return _appKeys = [];
        }

        return _appKeys = db.Apps.AsNoTracking().Where(a => a.Id == appId).Select(a => a.ScopeKeys).FirstOrDefault() ?? [];
    }

    public async Task<ResolvedDataScope> GetAsync(CancellationToken cancellationToken)
    {
        return _resolved ??= await ResolveAsync();
    }

    private async Task<ResolvedDataScope> ResolveAsync()
    {
        if (editorOptions.Hub.Enabled)
        {
            // No session, or a session without a scope: denied, never unrestricted.
            return hubSession?.Current?.Scope ?? ResolvedDataScope.Denied;
        }

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
