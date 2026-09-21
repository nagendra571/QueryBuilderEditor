namespace QueryBuilder.Editor;

/// <summary>
/// QueryBuilder mounts under a fixed path — <c>/querybuilder</c> — rather than your app's root, so
/// it never hijacks routes your own app owns (the way TemplateBuilder.Editor only ever responds
/// under <c>/Templates</c>). This isn't currently configurable: the embedded SPA's built asset
/// references bake this path in at package-build time, so changing it here alone wouldn't be
/// enough — the frontend would need rebuilding with a matching Vite `base` to match.
/// </summary>
internal static class QueryBuilderRoutes
{
    public const string BasePath = "/querybuilder";
}
