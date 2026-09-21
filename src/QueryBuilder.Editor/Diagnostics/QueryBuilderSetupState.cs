namespace QueryBuilder.Editor.Diagnostics;

/// <summary>
/// Tracks host wiring the setup diagnostics page can't otherwise observe — e.g. whether
/// <c>UseQueryBuilderEditorUI()</c> was actually called. Registered as a singleton by
/// <c>AddQueryBuilderEditor</c>; mutated by the extension methods themselves at app startup.
/// </summary>
internal sealed class QueryBuilderSetupState
{
    public bool UiMiddlewareRegistered { get; set; }
}
