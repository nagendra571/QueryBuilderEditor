namespace QueryBuilder.Hub;

internal static class SecurityHeaders
{
    // OnStarting, not a plain assignment: the exception handler clears the response, and redirects and
    // errors must carry the headers too.
    public static IApplicationBuilder UseHubSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var headers = ((HttpContext)state).Response.Headers;
                headers["Content-Security-Policy"] = "frame-ancestors 'self'";
                headers["X-Frame-Options"] = "SAMEORIGIN";
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "same-origin";
                return Task.CompletedTask;
            }, context);
            return next();
        });
}
