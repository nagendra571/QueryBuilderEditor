using System.Security.Cryptography;
using System.Text.RegularExpressions;
using QueryBuilder.Application.DataScoping;
using QueryBuilder.Application.Exceptions;

namespace QueryBuilder.Application.Hub.Registry;

/// <summary>What the registry accepts. Pure, so every rule can be tested without a database.</summary>
public static partial class RegistryRules
{
    public const int MaxNameLength = 200;
    public const int MaxKidLength = 64;
    public const int MaxPemLength = 4000;
    public const int MaxReturnUrls = 20;
    public const int MaxReturnUrlLength = 800;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex KidPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$")]
    private static partial Regex AppKeyPattern();

    public static string AppKey(string? key)
    {
        if (key is null || !AppKeyPattern().IsMatch(key))
        {
            throw new CatalogValidationException("An app key is 2 to 63 characters: lowercase letters, digits and hyphens, starting with a letter or digit.");
        }

        return key;
    }

    public static string AppName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength)
        {
            throw new CatalogValidationException($"A name is required and at most {MaxNameLength} characters.");
        }

        return trimmed;
    }

    /// <summary>The same name rule and limits as the scope keys a host declares or an admin adds to a data source.</summary>
    public static List<string> ScopeKeys(IReadOnlyList<string>? keys)
    {
        if (keys is null)
        {
            throw new CatalogValidationException("A list of scope keys is required.");
        }

        if (keys.Count > DataScopeKeys.MaxPerDataSource)
        {
            throw new CatalogValidationException($"An app can have at most {DataScopeKeys.MaxPerDataSource} scope keys.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!DataScopeKeys.IsValidName(key))
            {
                throw new CatalogValidationException("A scope key uses letters, digits and underscores only (max 128 characters).");
            }

            if (!seen.Add(key))
            {
                throw new CatalogValidationException($"'{key}' is listed more than once.");
            }
        }

        return keys.ToList();
    }

    public static string Kid(string? kid)
    {
        // Letters, digits, dot, underscore, hyphen only: anything else (a slash, %, ?) cannot be reliably addressed in
        // the revoke URL, and revoking is the incident-response path.
        if (kid is null || !KidPattern().IsMatch(kid))
        {
            throw new CatalogValidationException($"A key id is 1 to {MaxKidLength} characters: letters, digits, '.', '_' and '-' only, starting with a letter or digit.");
        }

        return kid;
    }

    /// <summary>
    /// Accepts only a single public key of the kind the launch validator will use: RSA of 2048 bits or more for RS256,
    /// P-256 for ES256. A private key, a certificate or a key of another kind is refused here, not at the first launch.
    /// Returns the SHA-256 of the key's SubjectPublicKeyInfo, which is what an operator compares against their own copy.
    /// </summary>
    public static string ValidatePublicKey(string? algorithm, string? pem)
    {
        if (algorithm is not ("RS256" or "ES256"))
        {
            throw new CatalogValidationException("The algorithm must be RS256 or ES256.");
        }

        if (string.IsNullOrWhiteSpace(pem) || pem.Length > MaxPemLength)
        {
            throw new CatalogValidationException($"A public key in PEM form is required, at most {MaxPemLength} characters.");
        }

        return TryFingerprint(algorithm, pem, out var problem) ?? throw new CatalogValidationException(problem!);
    }

    /// <summary>Null (and no throw) when a stored key no longer parses, so listing never fails on one bad row.</summary>
    public static string? FingerprintOrNull(string algorithm, string pem) => TryFingerprint(algorithm, pem, out _);

    private static string? TryFingerprint(string algorithm, string pem, out string? problem)
    {
        problem = "The key must be one public key in PEM form (-----BEGIN PUBLIC KEY-----).";
        try
        {
            var span = pem.AsSpan();
            if (!PemEncoding.TryFind(span, out var fields) || !span[fields.Label].SequenceEqual("PUBLIC KEY"))
            {
                return null;
            }

            // Anything beside the one block (a second key, a private key after it) is refused rather than ignored.
            if (!span[..fields.Location.Start].IsWhiteSpace() || !span[fields.Location.End..].IsWhiteSpace())
            {
                return null;
            }

            var der = Convert.FromBase64String(span[fields.Base64Data].ToString());
            byte[] spki;
            if (algorithm == "RS256")
            {
                using var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(der, out var read);
                if (read != der.Length)
                {
                    return null;
                }

                if (!SigningKeyStrength.IsAcceptable(rsa))
                {
                    problem = "An RS256 key must be RSA of 2048 to 8192 bits.";
                    return null;
                }

                spki = rsa.ExportSubjectPublicKeyInfo();
            }
            else
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(der, out var read);
                if (read != der.Length)
                {
                    return null;
                }

                if (!SigningKeyStrength.IsAcceptable(ecdsa))
                {
                    problem = "An ES256 key must be on the P-256 curve.";
                    return null;
                }

                spki = ecdsa.ExportSubjectPublicKeyInfo();
            }

            return Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            problem = algorithm == "RS256"
                ? "That is not an RSA public key in PEM form."
                : "That is not an EC public key in PEM form.";
            return null;
        }
    }

    /// <summary>Absolute https, or http only for localhost; no fragment, no credentials. Duplicates (exact) collapse; strings are stored as given.</summary>
    public static List<string> ReturnUrls(IReadOnlyList<string>? urls)
    {
        if (urls is null)
        {
            throw new CatalogValidationException("A list of return URLs is required.");
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var url in urls)
        {
            ValidateReturnUrl(url);
            if (seen.Add(url))
            {
                result.Add(url);
                if (result.Count > MaxReturnUrls)
                {
                    throw new CatalogValidationException($"An app can have at most {MaxReturnUrls} return URLs.");
                }
            }
        }

        // The stored set is unique ignoring case on typical collations, so two spellings could not both be saved.
        if (result.Select(u => u.ToLowerInvariant()).Distinct().Count() != result.Count)
        {
            throw new CatalogValidationException("Two return URLs differ only by letter case; list one.");
        }

        return result;
    }

    private static void ValidateReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > MaxReturnUrlLength || url != url.Trim() || url.Any(char.IsControl)
            || url.Contains('#') || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || !url.Contains("//", StringComparison.Ordinal))
        {
            throw new CatalogValidationException($"A return URL must be an absolute https URL of at most {MaxReturnUrlLength} characters, without a fragment or credentials.");
        }

        // Must already be in the form a browser will follow ("https:evil.com" parses but is not "https://evil.com/"),
        // so what is validated is what a launch can redirect to. Say what to type instead.
        if (!string.Equals(uri.AbsoluteUri, url, StringComparison.Ordinal))
        {
            throw new CatalogValidationException($"Write the return URL exactly as `{uri.AbsoluteUri}` (lowercase host, `/` after the host, no default port).");
        }

        var local = uri.Scheme == Uri.UriSchemeHttp && uri.Host == "localhost";
        if (uri.Scheme != Uri.UriSchemeHttps && !local)
        {
            throw new CatalogValidationException("A return URL must use https (http is allowed only for localhost).");
        }
    }
}
