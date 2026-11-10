using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace QueryBuilder.Editor.Middleware;

/// <summary>
/// Hub mode only. SameSite=Lax already stops cross-site POSTs from carrying the session cookie; this also demands a
/// header a cross-site form or simple fetch cannot add, so a state-changing API call must have come from our own page.
/// </summary>
internal sealed class HubRequestHeaderStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-QueryBuilder-Request";
    private static readonly PathString ApiPath = new(QueryBuilderRoutes.BasePath + "/api");

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (IsStateChanging(context.Request.Method)
                && context.Request.Path.StartsWithSegments(ApiPath)
                && context.Request.Headers[HeaderName] != "1")
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid request",
                    Detail = $"The {HeaderName} header is required.",
                    Instance = context.Request.Path,
                }, QueryBuilderJson.Options, "application/problem+json", context.RequestAborted);
                return;
            }

            await nextMiddleware();
        });
        next(app);
    };

    private static bool IsStateChanging(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
