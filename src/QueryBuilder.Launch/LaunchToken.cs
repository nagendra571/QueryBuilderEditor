using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace QueryBuilder.Launch
{
    public static class LaunchToken
    {
        public const int MaxLifetimeSeconds = 120;
        public const int DefaultLifetimeSeconds = 60;

        /// <summary>
        /// Signs a launch token. <paramref name="appKey"/> is the key the hub registered your app under and
        /// <paramref name="audience"/> the hub's configured audience.
        /// </summary>
        public static string Create(
            string appKey,
            string audience,
            LaunchSigningKey key,
            LaunchUser user,
            LaunchRole role,
            LaunchScope scope,
            string? returnUrl = null,
            TimeSpan? lifetime = null,
            DateTimeOffset? now = null)
        {
            if (string.IsNullOrWhiteSpace(appKey))
            {
                throw new ArgumentException("The app key is required.", nameof(appKey));
            }

            if (string.IsNullOrWhiteSpace(audience))
            {
                throw new ArgumentException("The audience is required.", nameof(audience));
            }

            CheckAudience(audience);

            if (key == null) throw new ArgumentNullException(nameof(key));
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            if (role != LaunchRole.Admin && role != LaunchRole.Author && role != LaunchRole.Viewer)
            {
                throw new ArgumentOutOfRangeException(nameof(role), "The role must be Admin, Author or Viewer.");
            }

            if (returnUrl != null)
            {
                CheckReturnUrl(returnUrl);
            }

            var seconds = (long)(lifetime ?? TimeSpan.FromSeconds(DefaultLifetimeSeconds)).TotalSeconds;
            if (seconds < 1 || seconds > MaxLifetimeSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime), "The lifetime must be 1 to " + MaxLifetimeSeconds + " seconds; the hub refuses longer.");
            }

            // Whole seconds: the hub compares integers and refuses fractions.
            var iat = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

            var header = new JsonWriter().StartObject()
                .String("alg", key.Algorithm).String("typ", "JWT").String("kid", key.Kid).EndObject();

            var payload = new JsonWriter().StartObject()
                .String("iss", appKey).String("aud", audience).String("sub", user.Id);
            if (user.Name != null) payload.String("name", user.Name);
            if (user.Email != null) payload.String("email", user.Email);
            payload.String("role", role.ToString());
            scope.Write(payload);
            if (returnUrl != null) payload.String("returnUrl", returnUrl);
            payload.Long("iat", iat).Long("exp", iat + seconds).String("jti", NewJti()).EndObject();

            var signingInput = Base64Url.Encode(Utf8(header.ToString())) + "." + Base64Url.Encode(Utf8(payload.ToString()));
            return signingInput + "." + Base64Url.Encode(key.Sign(Encoding.ASCII.GetBytes(signingInput)));
        }

        // The hub compares aud with its Hub:Audience character for character, and that setting is only ever scheme, host and
        // port. A trailing slash or a path (often copied from the hub URL setting, which LaunchForm.Html accepts with either) would
        // otherwise surface only as a WrongAudience refusal in the hub's audit log. Not normalised: the result could still differ
        // from the hub's setting (case, say), and an exact error here beats a silent rewrite.
        private static void CheckAudience(string audience)
        {
            const string shape = "The audience must be the hub's address exactly as the hub has it in Hub:Audience, for example 'https://hub.example.com': scheme, host and optional port only";
            if (audience != audience.Trim() || audience.Any(char.IsControl))
            {
                throw new ArgumentException(shape + ", with no spaces around it.", nameof(audience));
            }

            if (!Uri.TryCreate(audience, UriKind.Absolute, out var uri) || audience.IndexOf("//", StringComparison.Ordinal) < 0)
            {
                throw new ArgumentException(shape + ".", nameof(audience));
            }

            var local = uri.Scheme == Uri.UriSchemeHttp && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
            if (uri.Scheme != Uri.UriSchemeHttps && !local)
            {
                throw new ArgumentException(shape + ", using https (http is allowed only for localhost).", nameof(audience));
            }

            if (audience.EndsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException(shape + ". Remove the trailing '/': use '" + audience.TrimEnd('/') + "'.", nameof(audience));
            }

            if (uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/"
                || audience.IndexOf('?') >= 0 || audience.IndexOf('#') >= 0)
            {
                throw new ArgumentException(shape + ", with no path (not even /querybuilder), query or fragment.", nameof(audience));
            }
        }

        // Same shape rule as the hub's return-URL registry, so a URL it could never have registered fails here with a reason
        // instead of as a refused launch.
        private static void CheckReturnUrl(string url)
        {
            var bad = url.Length == 0 || url.Length > 800 || url != url.Trim() || url.Any(char.IsControl) || url.Contains('#')
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || url.IndexOf("//", StringComparison.Ordinal) < 0;
            if (bad)
            {
                throw new ArgumentException("The return URL must be an absolute https URL of at most 800 characters, without a fragment or credentials.", nameof(url));
            }

            uri = new Uri(url);
            if (!string.Equals(uri.AbsoluteUri, url, StringComparison.Ordinal))
            {
                throw new ArgumentException("Write the return URL exactly as '" + uri.AbsoluteUri + "' (lowercase host, '/' after the host, no default port).", nameof(url));
            }

            var local = uri.Scheme == Uri.UriSchemeHttp && uri.Host == "localhost";
            if (uri.Scheme != Uri.UriSchemeHttps && !local)
            {
                throw new ArgumentException("The return URL must use https (http is allowed only for localhost).", nameof(url));
            }
        }

        private static byte[] Utf8(string json) => new UTF8Encoding(false, true).GetBytes(json);

        private static string NewJti()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var sb = new StringBuilder(32);
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
