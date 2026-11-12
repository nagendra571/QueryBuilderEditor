using System.Net;
using System.Text;

namespace QueryBuilder.Editor.Hub;

/// <summary>The super admin sign-in page: plain server-rendered HTML, so it needs no script and no single-page app.</summary>
internal static class SignInPage
{
    public static string Render(string? providerDisplayName, bool offerProvider, bool offerCode, string providerPath, string codePath)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta name=\"robots\" content=\"noindex\"><title>QueryBuilder sign in</title>")
            .Append("<style>body{font-family:system-ui,sans-serif;margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#f4f5f7;color:#1b2430}")
            .Append("main{width:100%;max-width:26rem;padding:2rem}h1{font-size:1.25rem;margin:0 0 1.25rem}form{margin:0 0 1.25rem}")
            .Append("label{display:block;font-size:.875rem;margin-bottom:.375rem}")
            .Append("input[type=text],input[type=password]{box-sizing:border-box;width:100%;padding:.5rem;border:1px solid #9aa4b2;border-radius:4px;font:inherit;margin-bottom:.75rem}")
            .Append("button{padding:.5rem 1rem;border:0;border-radius:4px;background:#1b2430;color:#fff;font:inherit;cursor:pointer}</style></head><body><main>")
            .Append("<h1>Sign in to QueryBuilder</h1>");

        if (offerProvider)
        {
            html.Append("<form method=\"post\" action=\"").Append(WebUtility.HtmlEncode(providerPath)).Append("\">")
                .Append("<button type=\"submit\">Sign in with ").Append(WebUtility.HtmlEncode(providerDisplayName ?? "your organisation")).Append("</button></form>");
        }

        if (offerCode)
        {
            html.Append("<form method=\"post\" action=\"").Append(WebUtility.HtmlEncode(codePath)).Append("\" autocomplete=\"off\">")
                .Append("<label for=\"code\">One-time sign-in code</label>")
                .Append("<input id=\"code\" name=\"code\" type=\"password\" autocomplete=\"off\" spellcheck=\"false\" required maxlength=\"1024\">")
                .Append("<button type=\"submit\">Sign in with code</button></form>");
        }

        return html.Append("</main></body></html>").ToString();
    }
}
