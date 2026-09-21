using Microsoft.AspNetCore.Hosting;

namespace QueryBuilder.Editor.Diagnostics;

/// <summary>
/// Intercepts <see cref="SetupDiagnosticsPage.Path"/> at the very front of the pipeline — before
/// exception handling, CORS, static files, or routing — so the diagnostics page still works even
/// when one of those is misconfigured or <c>MapQueryBuilderEditor()</c> was never called. This is
/// the whole point of the page: it must be able to tell you what's wrong even when a lot is wrong.
/// </summary>
internal sealed class SetupDiagnosticsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (HttpMethods.IsGet(context.Request.Method) &&
                    string.Equals(context.Request.Path.Value, SetupDiagnosticsPage.Path, StringComparison.OrdinalIgnoreCase))
                {
                    await SetupDiagnosticsPage.HandleAsync(context);
                    return;
                }

                await nextMiddleware();
            });

            next(app);
        };
    }
}
