using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QueryBuilder.Editor.Diagnostics;

internal static class SetupDiagnosticsPage
{
    public const string Path = "/_setup";

    public static async Task HandleAsync(HttpContext context)
    {
        var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment())
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var checks = await SetupDiagnosticsRunner.RunAsync(context.RequestServices, context.RequestAborted);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(Render(checks));
    }

    private static string Render(List<SetupCheck> checks)
    {
        var passCount = checks.Count(c => c.Passed);
        var allPass = passCount == checks.Count;

        var rows = new StringBuilder();
        foreach (var check in checks)
        {
            var badge = check.Passed
                ? "<span class=\"badge pass\">PASS</span>"
                : "<span class=\"badge fail\">FAIL</span>";
            var fixHtml = check.Fix is null
                ? ""
                : $"<div class=\"fix\"><strong>Fix:</strong> {Encode(check.Fix)}</div>";

            rows.Append($"""
                <div class="row {(check.Passed ? "row-pass" : "row-fail")}">
                    <div class="row-head">{badge}<span class="name">{Encode(check.Name)}</span></div>
                    <div class="detail">{Encode(check.Detail)}</div>
                    {fixHtml}
                </div>
                """);
        }

        var summaryClass = allPass ? "summary-pass" : "summary-fail";
        var summaryText = allPass ? "All checks passed" : $"{checks.Count - passCount} of {checks.Count} check(s) failing";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8" />
            <title>QueryBuilder setup diagnostics</title>
            <style>
                :root { color-scheme: light dark; }
                body { font-family: -apple-system, Segoe UI, Roboto, sans-serif; margin: 0; padding: 2rem;
                       background: #f8fafc; color: #0f172a; }
                @media (prefers-color-scheme: dark) { body { background: #16171d; color: #f1f5f9; } }
                h1 { font-size: 1.25rem; margin: 0 0 0.25rem; }
                .subtitle { color: #64748b; font-size: 0.875rem; margin: 0 0 1.5rem; }
                .summary { display: inline-block; padding: 0.375rem 0.75rem; border-radius: 999px;
                           font-size: 0.8125rem; font-weight: 600; margin-bottom: 1.5rem; }
                .summary-pass { background: rgba(16,185,129,0.15); color: #10b981; }
                .summary-fail { background: rgba(239,68,68,0.15); color: #ef4444; }
                .row { border: 1px solid rgba(100,116,139,0.25); border-radius: 8px; padding: 0.875rem 1rem;
                       margin-bottom: 0.625rem; background: rgba(255,255,255,0.4); }
                @media (prefers-color-scheme: dark) { .row { background: rgba(255,255,255,0.03); } }
                .row-fail { border-color: rgba(239,68,68,0.4); }
                .row-head { display: flex; align-items: center; gap: 0.625rem; }
                .name { font-weight: 600; font-size: 0.9375rem; }
                .badge { font-size: 0.6875rem; font-weight: 700; padding: 0.125rem 0.5rem; border-radius: 4px; letter-spacing: 0.02em; }
                .badge.pass { background: rgba(16,185,129,0.15); color: #10b981; }
                .badge.fail { background: rgba(239,68,68,0.15); color: #ef4444; }
                .detail { margin: 0.375rem 0 0 0; font-size: 0.8125rem; color: #64748b; }
                .fix { margin-top: 0.5rem; font-size: 0.8125rem; padding: 0.5rem 0.625rem; border-radius: 6px;
                       background: rgba(245,158,11,0.12); color: #b45309; }
                @media (prefers-color-scheme: dark) { .fix { color: #fbbf24; } }
                code { font-family: ui-monospace, Consolas, monospace; }
            </style>
            </head>
            <body>
                <h1>QueryBuilder setup diagnostics</h1>
                <p class="subtitle">Development-only. Returns 404 in every other environment.</p>
                <div class="summary {{summaryClass}}">{{summaryText}}</div>
                {{rows}}
            </body>
            </html>
            """;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
