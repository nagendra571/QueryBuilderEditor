using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace QueryBuilder.Tests.Hub;

/// <summary>Builds compact JWTs by hand so tests control every header and claim, including the ones a real library would refuse to write.</summary>
internal static class TestTokens
{
    public static string Rs256(RSA key, Dictionary<string, object?> payload, string? kid = "rsa-a") =>
        Build(Header("RS256", kid), payload, data => key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    public static string Es256(ECDsa key, Dictionary<string, object?> payload, string? kid = "ec-a") =>
        Build(Header("ES256", kid), payload, data => key.SignData(data, HashAlgorithmName.SHA256));

    public static string Hs256(byte[] secret, Dictionary<string, object?> payload, string? kid = "rsa-a") =>
        Build(Header("HS256", kid), payload, data => HMACSHA256.HashData(secret, data));

    public static string Unsigned(Dictionary<string, object?> payload, string? kid = "rsa-a") =>
        Build(Header("none", kid), payload, _ => []);

    /// <summary>Signed as RS256 but the header names another algorithm.</summary>
    public static string Rs256WithHeaderAlg(RSA key, string alg, Dictionary<string, object?> payload, string? kid = "rsa-a") =>
        Build(Header(alg, kid), payload, data => key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    public static string Pem(RSA key) => key.ExportSubjectPublicKeyInfoPem();

    public static string Pem(ECDsa key) => key.ExportSubjectPublicKeyInfoPem();

    private static Dictionary<string, object?> Header(string alg, string? kid)
    {
        var header = new Dictionary<string, object?> { ["alg"] = alg, ["typ"] = "JWT" };
        if (kid is not null)
        {
            header["kid"] = kid;
        }

        return header;
    }

    private static string Build(Dictionary<string, object?> header, Dictionary<string, object?> payload, Func<byte[], byte[]> sign)
    {
        var signingInput = $"{Encode(header)}.{Encode(payload)}";
        return $"{signingInput}.{Base64UrlEncoder.Encode(sign(Encoding.ASCII.GetBytes(signingInput)))}";
    }

    private static string Encode(object value) => Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(value));
}
