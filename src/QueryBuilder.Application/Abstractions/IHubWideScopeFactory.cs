using Microsoft.Extensions.DependencyInjection;

namespace QueryBuilder.Application.Abstractions;

/// <summary>
/// The single place that creates a hub-wide scope. It makes a NEW DI scope (never the request's, because
/// the request's app is already fixed) whose <see cref="IAppContextScope"/> has had UseHubWide called.
/// Synchronous: creating a scope does no I/O. Dispose the returned scope.
/// </summary>
public interface IHubWideScopeFactory
{
    AsyncServiceScope Create();
}
