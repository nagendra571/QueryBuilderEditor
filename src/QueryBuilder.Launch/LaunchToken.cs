using System;
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

            if (key == null) throw new ArgumentNullException(nameof(key));
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            if (role != LaunchRole.Admin && role != LaunchRole.Author && role != LaunchRole.Viewer)
            {
                throw new ArgumentOutOfRangeException(nameof(role), "The role must be Admin, Author or Viewer.");
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
