using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using QueryBuilder.Editor.Diagnostics;

namespace QueryBuilder.Editor;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Serves the built QueryBuilder React app (embedded in this assembly) and adds a SPA fallback
    /// route so client-side routes like <c>/queries/{id}</c> resolve correctly on refresh or direct
    /// navigation instead of 404ing. Call after <c>app.UseRouting()</c> (if used) and before
    /// <c>app.MapQueryBuilderEditor()</c>; requires no other static file middleware for the same path.
    /// Skip this call entirely if you're running QueryBuilder headless (API only) — the setup
    /// diagnostics page at <c>/_setup</c> treats that as an expected configuration, not an error.
    /// </summary>
    /// <example>
    /// <code>
    /// app.UseQueryBuilderEditorUI();
    /// app.MapQueryBuilderEditor();
    /// </code>
    /// </example>
    public static WebApplication UseQueryBuilderEditorUI(this WebApplication app)
    {
        app.Services.GetRequiredService<QueryBuilderSetupState>().UiMiddlewareRegistered = true;

        var fileProvider = new ManifestEmbeddedFileProvider(typeof(ApplicationBuilderExtensions).Assembly, "wwwroot");

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

        // Anything not matched by an API route or a real static file falls back to index.html,
        // letting the SPA's own client-side router take over.
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fileProvider });

        return app;
    }
}
