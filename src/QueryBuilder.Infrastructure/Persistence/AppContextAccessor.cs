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
public sealed class AppContextAccessor(MetadataMode mode) : IAppContext, IAppContextScope
{
    private Guid? _appId;
    private bool _hubWide;

    public Guid AppId => _appId ?? (mode.Hub ? App.NoneId : App.DefaultId);
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

    // Task 6 will make this the only caller in hub mode; the public UseHubWide stays until then.
    internal void UseHubWideForFactory() => UseHubWide();

    public void UseHubWide()
    {
        if (_appId is not null && !_hubWide)
        {
            throw new InvalidOperationException("This scope already chose its app; it cannot be changed.");
        }

        _hubWide = true;
        _appId = App.NoneId;
    }
}
