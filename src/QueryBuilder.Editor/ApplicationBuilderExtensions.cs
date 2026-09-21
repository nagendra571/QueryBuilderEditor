using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using QueryBuilder.Editor.Diagnostics;

namespace QueryBuilder.Editor;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Serves the built QueryBuilder React app (embedded in this assembly) under
    /// <c>/querybuilder</c> — never your app's root — with a SPA fallback so client-side routes
    /// like <c>/querybuilder/queries/{id}</c> resolve correctly on refresh or direct navigation.
    /// Call after <c>app.UseRouting()</c> (if used) and before <c>app.MapQueryBuilderEditor()</c>.
    /// Skip this call entirely if you're running QueryBuilder headless (API only) — the setup
    /// diagnostics page at <c>/_setup</c> treats that as an expected configuration, not an error.
    /// </summary>
    /// <example>
    /// <code>
    /// app.UseQueryBuilderEditorUI();
    /// app.MapQueryBuilderEditor();
    /// // Add a nav link in your own layout: &lt;a href="/querybuilder"&gt;Queries&lt;/a&gt;
    /// </code>
    /// </example>
    public static WebApplication UseQueryBuilderEditorUI(this WebApplication app)
    {
        app.Services.GetRequiredService<QueryBuilderSetupState>().UiMiddlewareRegistered = true;

        var fileProvider = new ManifestEmbeddedFileProvider(typeof(ApplicationBuilderExtensions).Assembly, "wwwroot");
        var basePath = QueryBuilderRoutes.BasePath;

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider, RequestPath = basePath });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider, RequestPath = basePath });

        // Anything under /querybuilder not matched by a real static file falls back to index.html,
        // letting the SPA's own client-side router take over. Two patterns: the bare base path
        // (no trailing segment) and everything nested under it. Deliberately no RequestPath here —
        // MapFallbackToFile always serves the fixed "index.html" regardless of the matched path, so
        // path-stripping (RequestPath's job for UseStaticFiles) doesn't apply and actively breaks it.
        var fallbackOptions = new StaticFileOptions { FileProvider = fileProvider };
        app.MapFallbackToFile(basePath, "index.html", fallbackOptions);
        app.MapFallbackToFile(basePath + "/{*path:nonfile}", "index.html", fallbackOptions);

        return app;
    }
}
