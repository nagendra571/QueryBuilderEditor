using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence;

/// <summary>Whether this installation runs as a hub (many apps) or embedded (the Default app only).</summary>
public sealed record MetadataMode(bool Hub)
{
    public string Name => Hub ? "Hub" : "Embedded";
}

/// <summary>
/// Resolves the app a scope runs as. Without an explicit choice: embedded is the Default app; hub is
/// <see cref="App.NoneId"/> (sees nothing) so a missing app can never fall through to another app's data.
/// </summary>
public sealed class AppContextAccessor(MetadataMode mode, IAppSessionSource? session = null) : IAppContext, IAppContextScope
{
    private Guid? _appId;
    private bool _hubWide;

    public Guid AppId
    {
        get
        {
            if (_appId is null && mode.Hub && session is not null)
            {
                // Read the session once and keep the answer: the rest of the scope must not see a different app
                // if the cookie changes mid-request, and a later explicit choice of another app is refused.
                var current = session.Current;
                _appId = current is { AppId: { } id } ? id : App.NoneId;
            }

            return _appId ?? (mode.Hub ? App.NoneId : App.DefaultId);
        }
    }

    public bool IsHubWide => _hubWide;

    // Set once per scope: switching apps mid-scope could let a context already in use read another app's rows.
    public void UseApp(Guid appId)
    {
        if (_appId is not null && (_hubWide || _appId != appId))
        {
            throw new InvalidOperationException("This scope already chose its app; it cannot be changed.");
        }

        _appId = appId;
    }

    // The factory is the only way to a hub-wide scope in hub mode: request code (which can reach the public
    // method through IAppContextScope) must never be able to widen what a request sees.
    internal void UseHubWideForFactory() => SetHubWide();

    public void UseHubWide()
    {
        if (mode.Hub)
        {
            throw new InvalidOperationException("A hub-wide scope is created only through IHubWideScopeFactory.");
        }

        SetHubWide();
    }

    private void SetHubWide()
    {
        if (_appId is not null && !_hubWide)
        {
            throw new InvalidOperationException("This scope already chose its app; it cannot be changed.");
        }

        _hubWide = true;
        _appId = App.NoneId;
    }
}
