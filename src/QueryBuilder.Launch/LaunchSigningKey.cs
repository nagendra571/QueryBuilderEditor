using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace QueryBuilder.Launch
{
    /// <summary>
    /// Your app's private signing key plus the id (<c>kid</c>) the hub registered its public half under. The key is
    /// the caller's: this class never exports it and never disposes it.
    /// </summary>
    public sealed class LaunchSigningKey : IDisposable
    {
        private const string P256Oid = "1.2.840.10045.3.1.7";
        private static readonly Regex KidPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

        private readonly RSA? _rsa;
        private readonly ECDsa? _ecdsa;

        private LaunchSigningKey(string kid, RSA? rsa, ECDsa? ecdsa)
        {
            Kid = kid;
            _rsa = rsa;
            _ecdsa = ecdsa;
            Algorithm = rsa != null ? "RS256" : "ES256";
        }

        public string Kid { get; }

        public string Algorithm { get; }

        public static LaunchSigningKey Rsa(string kid, RSA key)
        {
            CheckKid(kid);
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (key.KeySize < 2048 || key.KeySize > 8192)
            {
                throw new ArgumentException("RSA keys must be 2048 to 8192 bits; this one is " + key.KeySize + ".", nameof(key));
            }

            return new LaunchSigningKey(kid, key, null);
        }

        public static LaunchSigningKey EcdsaP256(string kid, ECDsa key)
        {
            CheckKid(kid);
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            // KeySize alone also admits other 256-bit curves, which ES256 does not allow.
            var isP256 = false;
            if (key.KeySize == 256)
            {
                try
                {
                    var curve = key.ExportParameters(false).Curve;
                    isP256 = curve.Oid?.Value == P256Oid || curve.Oid?.FriendlyName == "nistP256" || curve.Oid?.FriendlyName == "ECDSA_P256";
                }
                catch (CryptographicException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }

            if (!isP256)
            {
                throw new ArgumentException("ECDSA keys must use the NIST P-256 curve.", nameof(key));
            }

            return new LaunchSigningKey(kid, null, key);
        }

        internal byte[] Sign(byte[] data)
        {
            if (_rsa != null)
            {
                return _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            var signature = _ecdsa!.SignData(data, HashAlgorithmName.SHA256);
            // JWS wants IEEE P1363 (r then s, 32 bytes each); a DER-encoded signature would be refused by the hub.
            if (signature.Length != 64)
            {
                throw new CryptographicException("The ECDSA key produced a " + signature.Length + "-byte signature; ES256 needs 64.");
            }

            return signature;
        }

        /// <summary>Nothing to release: the key belongs to the caller.</summary>
        public void Dispose()
        {
        }

        private static void CheckKid(string kid)
        {
            if (kid == null || !KidPattern.IsMatch(kid))
            {
                throw new ArgumentException("The key id must start with a letter or digit and use only letters, digits, '.', '_' and '-' (up to 64).", nameof(kid));
            }
        }
    }
}
