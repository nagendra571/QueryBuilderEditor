using System;
using System.Text;

namespace QueryBuilder.Launch
{
    public static class LaunchForm
    {
        /// <summary>
        /// A complete HTML page that posts the token to the hub as a form field (never in the URL, so it stays out of
        /// logs and Referer headers) and submits itself; the button is for browsers with scripting off.
        /// </summary>
        public static string Html(string hubBaseUrl, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("The token is required.", nameof(token));
            }

            var action = HubBase(hubBaseUrl) + "/querybuilder/launch";
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"referrer\" content=\"no-referrer\">")
              .Append("<title>Opening QueryBuilder</title></head><body>")
              .Append("<form id=\"qb-launch\" method=\"post\" action=\"").Append(Encode(action)).Append("\">")
              .Append("<input type=\"hidden\" name=\"token\" value=\"").Append(Encode(token)).Append("\">")
              .Append("<noscript><button type=\"submit\">Continue to QueryBuilder</button></noscript>")
              .Append("</form>")
              .Append("<script>document.getElementById('qb-launch').submit();</script>")
              .Append("</body></html>");
            return sb.ToString();
        }

        private static string HubBase(string hubBaseUrl)
        {
            if (!Uri.TryCreate(hubBaseUrl, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("The hub address must be an absolute URL such as https://hub.example.com.", nameof(hubBaseUrl));
            }

            var local = uri.Scheme == Uri.UriSchemeHttp && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
            if (uri.Scheme != Uri.UriSchemeHttps && !local)
            {
                throw new ArgumentException("The hub address must use https (http is allowed only for localhost).", nameof(hubBaseUrl));
            }

            if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
            {
                throw new ArgumentException("The hub address must be just the scheme and host (and port): the hub is not served under a path.", nameof(hubBaseUrl));
            }

            return uri.GetLeftPart(UriPartial.Authority);
        }

        private static string Encode(string value) =>
            value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("'", "&#39;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
