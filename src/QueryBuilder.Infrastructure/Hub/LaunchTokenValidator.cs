using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataScoping;
using QueryBuilder.Application.Hub;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

/// <summary>
/// Turns an app's signed launch token into trusted claims or a refusal reason. Checks run in an order that
/// reveals nothing about other apps: the signing key is looked up only among the app named by <c>iss</c>,
/// and nothing from the payload is trusted before that key has verified the signature.
/// </summary>
public sealed class LaunchTokenValidator : ILaunchTokenValidator
{
    public const int MaxTokenLength = 512 * 1024;
    public const int MaxLifetimeSeconds = 120;
    public const int SkewSeconds = 30;
    public const int MaxScopeKeys = 20;
    public const int MaxValuesPerKey = 1000;
    public const int MaxValueLength = 200;
    /// <summary>The serialised scope must also fit <see cref="ResolvedDataScope.MaxSessionBytes"/> (6 KB, about 150 to 300 values).</summary>
    public const int MaxScopeBytes = ResolvedDataScope.MaxSessionBytes;
    private const int MaxIdentityLength = 256;
    private const int MaxJtiLength = 200;
    private const int MaxNameLength = 200;
    private const int MaxEmailLength = 320;

    // The handler's own size limit (250 KB by default) must equal ours, or an oversized token would surface as BadSignature.
    private static readonly JsonWebTokenHandler Handler = new() { MaximumTokenSizeInBytes = MaxTokenLength };


    private readonly IHubWideScopeFactory _hubWide;
    private readonly LaunchReplayStore _replays;
    private readonly LaunchValidationSettings _settings;
    private readonly TimeProvider _clock;

    public LaunchTokenValidator(IHubWideScopeFactory hubWide, LaunchReplayStore replays, LaunchValidationSettings settings)
        : this(hubWide, replays, settings, TimeProvider.System)
    {
    }

    internal LaunchTokenValidator(IHubWideScopeFactory hubWide, LaunchReplayStore replays, LaunchValidationSettings settings, TimeProvider clock)
    {
        _hubWide = hubWide;
        _replays = replays;
        _settings = settings;
        _clock = clock;
    }

    public async Task<LaunchResult> ValidateAsync(string token, CancellationToken ct)
    {
        if (!TryParse(token, out var headerDoc, out var payloadDoc))
        {
            return LaunchResult.Refused(LaunchRefusalReason.Malformed);
        }

        using (headerDoc)
        using (payloadDoc)
        {
            return await ValidateParsedAsync(token, headerDoc.RootElement, payloadDoc.RootElement, ct);
        }
    }

    private async Task<LaunchResult> ValidateParsedAsync(string token, JsonElement h, JsonElement p, CancellationToken ct)
    {
        // Only RS256/ES256: refuses "none" and every HMAC, which would let a public key act as a shared secret.
        if (!TryString(h, "alg", out var alg) || alg is not ("RS256" or "ES256"))
        {
            return LaunchResult.Refused(LaunchRefusalReason.AlgorithmNotAllowed);
        }

        if (!p.TryGetProperty("iss", out var issElement) || issElement.ValueKind == JsonValueKind.Null)
        {
            return LaunchResult.Refused(LaunchRefusalReason.MissingClaim);
        }

        if (issElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(issElement.GetString()))
        {
            return LaunchResult.Refused(LaunchRefusalReason.Malformed);
        }

        var iss = issElement.GetString()!;

        await using var scope = _hubWide.Create();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var app = (await db.Apps.AsNoTracking().Where(a => a.Key == iss).ToListAsync(ct))
            .FirstOrDefault(a => string.Equals(a.Key, iss, StringComparison.Ordinal));
        if (app is null || app.Id == QueryBuilder.Domain.Entities.App.DefaultId)
        {
            return LaunchResult.Refused(LaunchRefusalReason.UnknownApp);
        }

        LaunchResult Refuse(LaunchRefusalReason reason) => LaunchResult.Refused(reason, app.Id);

        if (!app.IsEnabled)
        {
            return Refuse(LaunchRefusalReason.AppDisabled);
        }

        if (!TryString(h, "kid", out var kid) || kid.Length == 0)
        {
            return Refuse(LaunchRefusalReason.UnknownKey);
        }

        var key = (await db.AppSigningKeys.AsNoTracking().Where(k => k.AppId == app.Id && k.Kid == kid).ToListAsync(ct))
            .FirstOrDefault(k => string.Equals(k.Kid, kid, StringComparison.Ordinal));
        if (key is null)
        {
            return Refuse(LaunchRefusalReason.UnknownKey);
        }

        if (key.RevokedAtUtc is not null)
        {
            return Refuse(LaunchRefusalReason.RevokedKey);
        }

        if (!string.Equals(key.Algorithm, alg, StringComparison.Ordinal))
        {
            return Refuse(LaunchRefusalReason.AlgorithmNotAllowed);
        }

        if (!await SignatureIsValidAsync(token, key.PublicKeyPem, alg, kid))
        {
            return Refuse(LaunchRefusalReason.BadSignature);
        }

        // From here the payload is authentic, but it is still only the app's word for who the user is.
        if (!AudienceMatches(p))
        {
            return Refuse(LaunchRefusalReason.WrongAudience);
        }

        if (!TryReadRequired(p, out var sub, out var roleText, out var jti, out var iat, out var exp, out var problem))
        {
            return Refuse(problem);
        }

        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        if (exp + SkewSeconds < now)
        {
            return Refuse(LaunchRefusalReason.Expired);
        }

        if (iat - SkewSeconds > now)
        {
            return Refuse(LaunchRefusalReason.NotYetValid);
        }

        if (p.TryGetProperty("nbf", out var nbfElement) && nbfElement.ValueKind != JsonValueKind.Null)
        {
            if (!nbfElement.TryGetInt64(out var nbf))
            {
                return Refuse(LaunchRefusalReason.Malformed);
            }

            if (nbf - SkewSeconds > now)
            {
                return Refuse(LaunchRefusalReason.NotYetValid);
            }
        }

        if (exp - iat > MaxLifetimeSeconds)
        {
            return Refuse(LaunchRefusalReason.LifetimeTooLong);
        }

        if (exp < iat)
        {
            return Refuse(LaunchRefusalReason.Malformed);
        }

        // Exact names only: Enum.TryParse would also accept "2" or "admin".
        var role = roleText switch
        {
            "Admin" => (HubRole?)HubRole.Admin,
            "Author" => HubRole.Author,
            "Viewer" => HubRole.Viewer,
            _ => null,
        };
        if (role is null)
        {
            return Refuse(LaunchRefusalReason.UnknownRole);
        }

        if (IsTooLong(p, "name", MaxNameLength) || IsTooLong(p, "email", MaxEmailLength))
        {
            return Refuse(LaunchRefusalReason.Malformed);
        }

        var dataSourceKeys = await db.DataSources.AsNoTracking()
            .Where(d => d.AppId == app.Id)
            .Select(d => d.ScopeKeys)
            .ToListAsync(ct);
        var declared = DataScopeKeys.Effective(app.ScopeKeys, dataSourceKeys.SelectMany(k => k));

        var scopeProblem = ReadScope(p, declared, out var dataScope);
        if (scopeProblem is not null)
        {
            return Refuse(scopeProblem.Value);
        }

        // Measured as the session will store it, before anything is written: an oversized scope would make every
        // request carry an oversized cookie.
        if (System.Text.Encoding.UTF8.GetByteCount(dataScope!.SerializeForSession()) > ResolvedDataScope.MaxSessionBytes)
        {
            return Refuse(LaunchRefusalReason.ScopeTooLarge);
        }

        string? returnUrl = null;
        if (p.TryGetProperty("returnUrl", out var returnElement) && returnElement.ValueKind != JsonValueKind.Null)
        {
            if (returnElement.ValueKind != JsonValueKind.String)
            {
                return Refuse(LaunchRefusalReason.Malformed);
            }

            returnUrl = returnElement.GetString()!;

            // SQL Server's default collation ignores case and trailing spaces, so equality is re-checked in code.
            var registered = await db.AppReturnUrls.AsNoTracking()
                .Where(u => u.AppId == app.Id && u.Url == returnUrl)
                .Select(u => u.Url)
                .ToListAsync(ct);
            if (!registered.Any(u => string.Equals(u, returnUrl, StringComparison.Ordinal)))
            {
                return Refuse(LaunchRefusalReason.ReturnUrlNotRegistered);
            }
        }

        var expiresUtc = DateTimeOffset.FromUnixTimeSeconds(exp);

        // Last, so a token refused for any other reason does not burn its id.
        await _replays.CleanUpAsync(db, _clock.GetUtcNow(), ct);
        if (!await _replays.TryRecordAsync(db, app.Id, jti, expiresUtc.AddSeconds(SkewSeconds), ct))
        {
            return Refuse(LaunchRefusalReason.Replayed);
        }

        return LaunchResult.Accepted(new LaunchClaims(
            app.Id, app.Key, sub, OptionalString(p, "name"), OptionalString(p, "email"),
            role.Value, dataScope!, returnUrl, jti, expiresUtc));
    }

    private static bool TryParse(string? token, out JsonDocument header, out JsonDocument payload)
    {
        header = null!;
        payload = null!;
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        // An empty signature is let through so an unsigned ("alg":"none") token gets its precise reason.
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        JsonDocument? h = null;
        JsonDocument? p = null;
        try
        {
            h = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
            p = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
            if (h.RootElement.ValueKind != JsonValueKind.Object || p.RootElement.ValueKind != JsonValueKind.Object)
            {
                h.Dispose();
                p.Dispose();
                return false;
            }

            header = h;
            payload = p;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            h?.Dispose();
            p?.Dispose();
            return false;
        }
    }

    private static async Task<bool> SignatureIsValidAsync(string token, string pem, string alg, string kid)
    {
        RSA? rsa = null;
        ECDsa? ecdsa = null;
        try
        {
            SecurityKey securityKey;
            if (alg == "RS256")
            {
                rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                if (!SigningKeyStrength.IsAcceptable(rsa))
                {
                    return false;
                }

                securityKey = new RsaSecurityKey(rsa) { KeyId = kid };
            }
            else
            {
                ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(pem);
                if (!SigningKeyStrength.IsAcceptable(ecdsa))
                {
                    return false;
                }

                securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = kid };
            }

            // Only the signature is delegated here; every other rule is checked by the caller so each has its own reason.
            var parameters = new TokenValidationParameters
            {
                IssuerSigningKey = securityKey,
                ValidAlgorithms = [alg],
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = false,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                RequireExpirationTime = false,
                TryAllIssuerSigningKeys = false,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            };

            var result = await Handler.ValidateTokenAsync(token, parameters);
            return result.IsValid;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException or SecurityTokenException)
        {
            return false;
        }
        finally
        {
            rsa?.Dispose();
            ecdsa?.Dispose();
        }
    }

    private bool AudienceMatches(JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(_settings.Audience) || !payload.TryGetProperty("aud", out var aud))
        {
            return false;
        }

        return aud.ValueKind switch
        {
            JsonValueKind.String => string.Equals(aud.GetString(), _settings.Audience, StringComparison.Ordinal),
            JsonValueKind.Array => aud.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.String
                && string.Equals(a.GetString(), _settings.Audience, StringComparison.Ordinal)),
            _ => false,
        };
    }

    private static bool TryReadRequired(JsonElement p, out string sub, out string role, out string jti, out long iat, out long exp, out LaunchRefusalReason problem)
    {
        sub = role = jti = string.Empty;
        iat = exp = 0;
        problem = LaunchRefusalReason.MissingClaim;

        foreach (var name in new[] { "iat", "exp", "jti", "sub", "role" })
        {
            if (!p.TryGetProperty(name, out var e) || e.ValueKind == JsonValueKind.Null)
            {
                return false;
            }
        }

        if (!p.GetProperty("iat").TryGetInt64(out iat) || !p.GetProperty("exp").TryGetInt64(out exp)
            || !TryString(p, "jti", out jti) || !TryString(p, "sub", out sub) || !TryString(p, "role", out role))
        {
            problem = LaunchRefusalReason.Malformed;
            return false;
        }

        if (string.IsNullOrWhiteSpace(jti) || string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        if (jti.Length > MaxJtiLength || sub.Length > MaxIdentityLength)
        {
            problem = LaunchRefusalReason.Malformed;
            return false;
        }

        return true;
    }

    /// <summary>Null when the scope is acceptable; otherwise why not.</summary>
    private static LaunchRefusalReason? ReadScope(JsonElement p, IReadOnlyList<string> declared, out ResolvedDataScope? scope)
    {
        scope = null;
        var hasScope = p.TryGetProperty("scope", out var scopeElement) && scopeElement.ValueKind != JsonValueKind.Null;
        var hasUnrestricted = p.TryGetProperty("unrestricted", out var unrestrictedElement) && unrestrictedElement.ValueKind != JsonValueKind.Null;

        if (hasUnrestricted && unrestrictedElement.ValueKind != JsonValueKind.True)
        {
            return LaunchRefusalReason.Malformed;
        }

        if (hasScope && hasUnrestricted)
        {
            return LaunchRefusalReason.ScopeAndUnrestricted;
        }

        if (hasUnrestricted)
        {
            scope = ResolvedDataScope.Unrestricted;
            return null;
        }

        if (!hasScope)
        {
            return LaunchRefusalReason.ScopeMissing;
        }

        if (scopeElement.ValueKind != JsonValueKind.Object)
        {
            return LaunchRefusalReason.Malformed;
        }

        if (scopeElement.GetPropertyCount() > MaxScopeKeys)
        {
            return LaunchRefusalReason.ScopeTooLarge;
        }

        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in scopeElement.EnumerateObject())
        {

            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                return LaunchRefusalReason.Malformed;
            }

            var list = new List<string>();
            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return LaunchRefusalReason.Malformed;
                }

                var value = item.GetString()!;
                if (value.Length == 0)
                {
                    return LaunchRefusalReason.Malformed;
                }

                if (value.Length > MaxValueLength || list.Count >= MaxValuesPerKey)
                {
                    return LaunchRefusalReason.ScopeTooLarge;
                }

                list.Add(value);
            }

            if (list.Count == 0)
            {
                return LaunchRefusalReason.ScopeTooLarge;
            }

            var canonical = declared.FirstOrDefault(d => string.Equals(d, property.Name, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                return LaunchRefusalReason.UndeclaredScopeKey;
            }

            if (!values.TryAdd(canonical, list.Distinct(StringComparer.Ordinal).ToList()))
            {
                return LaunchRefusalReason.Malformed;
            }
        }

        if (values.Count == 0)
        {
            return LaunchRefusalReason.ScopeMissing;
        }

        scope = new ResolvedDataScope(false, values);
        return null;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (element.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String)
        {
            value = e.GetString() ?? string.Empty;
            return true;
        }

        return false;
    }

    private static bool IsTooLong(JsonElement element, string name, int max) =>
        TryString(element, name, out var value) && value.Length > max;

    private static string? OptionalString(JsonElement element, string name) =>
        TryString(element, name, out var value) && value.Length > 0 ? value : null;
}
