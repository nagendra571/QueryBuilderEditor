using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QueryBuilder.Editor.Hub;

public sealed record IssuedCode(string Code, string Jti, DateTimeOffset ExpiresUtc);

public enum CodeRefusal { Malformed, BadSignature, WrongPurpose, WrongAudience, Expired, NotYetValid, LifetimeTooLong, NotListed }

public sealed record CodeCheck(string? Name, string? Jti, DateTimeOffset? ExpiresUtc, CodeRefusal? Refusal);

/// <summary>
/// One-time super admin sign-in codes: <c>base64url(payload).base64url(HMAC-SHA256)</c>. The key is derived from the
/// data-source encryption key with a fixed label and the hub's Audience, so it is never the key itself, a code can't be
/// a launch token, and a code from another deployment fails its signature. Whoever holds the encryption key can still
/// mint a code: with codes on, that key is a super admin credential.
/// Single use is enforced by the caller (the replay store); this class only issues and checks.
/// </summary>
public sealed class SuperAdminSignInCodes
{
    public const string Purpose = "qb-super-signin";
    public const int MaxMinutes = 60;
    public const int SkewSeconds = 30;
    public const int MaxCodeLength = 1024;

    private readonly QueryBuilderEditorOptions _options;
    private readonly TimeProvider _clock;
    private readonly byte[] _key;

    public SuperAdminSignInCodes(QueryBuilderEditorOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
        var ikm = Convert.FromBase64String(options.DataSourceManagement.EncryptionKey ?? throw new InvalidOperationException("Sign-in codes need the data source encryption key."));
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt: [], info: Encoding.UTF8.GetBytes("qb-super-signin-code-v1|" + options.Hub.Audience));
    }

    public IssuedCode Issue(string name, int? minutes)
    {
        var length = minutes ?? _options.Hub.SignInCodes.DefaultMinutes;
        if (length is < 1 or > MaxMinutes)
        {
            throw new ArgumentException($"Minutes must be between 1 and {MaxMinutes}.", nameof(minutes));
        }

        if (string.IsNullOrEmpty(name) || !_options.Hub.SuperAdmins.CodeNames.Any(n => string.Equals(n, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("That name is not in SuperAdmins.CodeNames.");
        }

        // Whole seconds on purpose: the check compares whole seconds and a fraction would only be ambiguous.
        var iat = _clock.GetUtcNow().ToUnixTimeSeconds();
        var exp = iat + length * 60L;
        var jti = Base64Url(RandomNumberGenerator.GetBytes(16));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["purpose"] = Purpose, ["aud"] = _options.Hub.Audience, ["name"] = name, ["jti"] = jti, ["iat"] = iat, ["exp"] = exp,
        });
        var body = Base64Url(payload);
        var code = body + "." + Base64Url(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body)));
        return new IssuedCode(code, jti, DateTimeOffset.FromUnixTimeSeconds(exp));
    }

    public CodeCheck Verify(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength)
        {
            return Refuse(CodeRefusal.Malformed);
        }

        var dot = code.IndexOf('.');
        if (dot <= 0 || dot != code.LastIndexOf('.') || dot == code.Length - 1)
        {
            return Refuse(CodeRefusal.Malformed);
        }

        var body = code[..dot];
        if (!TryDecode(body, out var payloadBytes) || !TryDecode(code[(dot + 1)..], out var signature))
        {
            return Refuse(CodeRefusal.Malformed);
        }

        var expected = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body));
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
        {
            return Refuse(CodeRefusal.BadSignature);
        }

        string? purpose, aud, name, jti;
        long iat, exp;
        try
        {
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Refuse(CodeRefusal.Malformed);
            }

            purpose = Str(root, "purpose");
            aud = Str(root, "aud");
            name = Str(root, "name");
            jti = Str(root, "jti");
            if (!Num(root, "iat", out iat) || !Num(root, "exp", out exp) || name is null || jti is null || jti.Length > 100)
            {
                return Refuse(CodeRefusal.Malformed);
            }
        }
        catch (JsonException)
        {
            return Refuse(CodeRefusal.Malformed);
        }

        // DateTimeOffset.FromUnixTimeSeconds throws outside year 1..9999, and the sums below could overflow.
        const long MaxUnix = 253402300799;
        if (iat is < 0 or > MaxUnix || exp is < 0 or > MaxUnix)
        {
            return Refuse(CodeRefusal.Malformed);
        }

        if (!string.Equals(purpose, Purpose, StringComparison.Ordinal))
        {
            return Refuse(CodeRefusal.WrongPurpose);
        }

        if (!string.Equals(aud, _options.Hub.Audience, StringComparison.Ordinal))
        {
            return Refuse(CodeRefusal.WrongAudience);
        }

        if (exp <= iat || exp - iat > MaxMinutes * 60L)
        {
            return Refuse(CodeRefusal.LifetimeTooLong);
        }

        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        if (iat > now + SkewSeconds)
        {
            return Refuse(CodeRefusal.NotYetValid);
        }

        if (now > exp + SkewSeconds)
        {
            return Refuse(CodeRefusal.Expired);
        }

        if (!_options.Hub.SuperAdmins.CodeNames.Any(n => string.Equals(n, name, StringComparison.Ordinal)))
        {
            return Refuse(CodeRefusal.NotListed);
        }

        return new CodeCheck(name, jti, DateTimeOffset.FromUnixTimeSeconds(exp), null);
    }

    private static CodeCheck Refuse(CodeRefusal reason) => new(null, null, null, reason);

    private static string? Str(JsonElement root, string property) =>
        root.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Num(JsonElement root, string property, out long value)
    {
        value = 0;
        return root.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out value);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecode(string text, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var s = text.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            bytes = Convert.FromBase64String(s);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
