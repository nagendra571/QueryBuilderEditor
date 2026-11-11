using System.Security.Cryptography;

namespace QueryBuilder.Application.Hub;

/// <summary>The one definition of an acceptable launch-signing key, used when a key is registered and when a token is verified.</summary>
public static class SigningKeyStrength
{
    public const int MinRsaBits = 2048;
    public const int MaxRsaBits = 8192;
    public const string P256Oid = "1.2.840.10045.3.1.7";

    public static bool IsAcceptable(RSA rsa) => rsa.KeySize is >= MinRsaBits and <= MaxRsaBits;

    // KeySize alone also admits secp256k1 and brainpoolP256r1, which ES256 does not allow.
    public static bool IsAcceptable(ECDsa ecdsa) => ecdsa.ExportParameters(false).Curve.Oid?.Value == P256Oid;
}
