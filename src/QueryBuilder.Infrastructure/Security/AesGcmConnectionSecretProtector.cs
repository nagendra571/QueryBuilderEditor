using System.Security.Cryptography;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;

namespace QueryBuilder.Infrastructure.Security;

/// <summary>The host's encryption key, or none. Wrapped so the raw bytes aren't a loose service in the container.</summary>
public sealed class ConnectionSecretKey(byte[]? key)
{
    public const int KeyBytes = 32;

    public byte[]? Key { get; } = key;

    /// <summary>Parses a base64 key of exactly 32 bytes. The error never repeats the value it was given.</summary>
    public static bool TryParse(string? base64, out byte[] key, out string? error)
    {
        key = [];
        error = null;
        if (string.IsNullOrWhiteSpace(base64))
        {
            error = "The key is empty.";
            return false;
        }

        try
        {
            key = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            error = "The key isn't valid base64.";
            return false;
        }

        if (key.Length != KeyBytes)
        {
            error = $"The key must decode to exactly {KeyBytes} bytes, but decodes to {key.Length}.";
            key = [];
            return false;
        }

        return true;
    }
}

/// <summary>
/// AES-256-GCM with a random nonce per value. The data source's Id is authenticated as associated data, so a blob
/// copied onto another row fails to decrypt. Layout before base64: version (1) | nonce (12) | tag (16) | ciphertext.
/// </summary>
public sealed class AesGcmConnectionSecretProtector(ConnectionSecretKey secretKey) : IConnectionSecretProtector
{
    private const byte Version = 1;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    public string Protect(Guid dataSourceId, string plaintext)
    {
        var key = RequireKey();
        var plain = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var blob = new byte[1 + NonceBytes + TagBytes + plain.Length];
        blob[0] = Version;
        var nonce = blob.AsSpan(1, NonceBytes);
        var tag = blob.AsSpan(1 + NonceBytes, TagBytes);
        var cipher = blob.AsSpan(1 + NonceBytes + TagBytes);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(dataSourceId));
        CryptographicOperations.ZeroMemory(plain);
        return Convert.ToBase64String(blob);
    }

    public string Unprotect(Guid dataSourceId, string ciphertext)
    {
        var key = RequireKey();
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException)
        {
            throw Unreadable();
        }

        if (blob.Length < 1 + NonceBytes + TagBytes || blob[0] != Version)
        {
            throw Unreadable();
        }

        var plain = new byte[blob.Length - 1 - NonceBytes - TagBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(
                blob.AsSpan(1, NonceBytes),
                blob.AsSpan(1 + NonceBytes + TagBytes),
                blob.AsSpan(1 + NonceBytes, TagBytes),
                plain,
                AssociatedData(dataSourceId));
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            throw Unreadable();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    // The version byte is authenticated too, so a blob can't be relabelled to another format.
    private static byte[] AssociatedData(Guid dataSourceId) => [Version, .. dataSourceId.ToByteArray()];

    private byte[] RequireKey() =>
        secretKey.Key ?? throw new ConnectionSecretException(
            "No encryption key is configured. Set DataSourceManagement.EncryptionKey to use managed data sources.");

    private static ConnectionSecretException Unreadable() =>
        new("The saved connection details can't be decrypted: the key is different, or the stored value was changed or belongs to another data source.");
}
