namespace QueryBuilder.Application.Abstractions;

/// <summary>Lets background work (seeders, diagnostics) choose the app its scope runs as. Call before resolving
/// the data context; once a choice is made it stays for that scope.</summary>
public interface IAppContextScope
{
    void UseApp(Guid appId);
    void UseHubWide();
}
