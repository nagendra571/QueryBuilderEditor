using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;

namespace QueryBuilder.Infrastructure.Persistence;

internal sealed class HubWideScopeFactory(IServiceScopeFactory scopes) : IHubWideScopeFactory
{
    public AsyncServiceScope Create()
    {
        // A scope created from the root factory is new even when called inside a request, so it never inherits the request's app.
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<AppContextAccessor>().UseHubWideForFactory();
        return scope;
    }
}
