using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using QueryBuilder.Infrastructure.Security;

namespace QueryBuilder.Editor.Hub;

/// <summary>
/// Encrypts the session key ring's secrets in the metadata database with AES-256-GCM, using the same key material as
/// the data source secrets (<see cref="ConnectionSecretKey"/>). Anyone who can read that table could otherwise forge
/// any hub cookie, super admin included. A distinct associated-data label means a connection blob cannot be swapped in.
/// Layout before base64: version (1) | nonce (12) | tag (16) | ciphertext.
/// </summary>
internal sealed class HubKeyEncryptor(ConnectionSecretKey secret) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var key = secret.Key ?? throw new InvalidOperationException("Hub mode needs the data source encryption key.");
        var encrypted = new XElement("encryptedHubKey", new XAttribute("v", 1), HubKeyCipher.Encrypt(key, plaintextElement.ToString(SaveOptions.DisableFormatting)));
        return new EncryptedXmlInfo(encrypted, typeof(HubKeyDecryptor));
    }
}

/// <summary>Public with an <see cref="IServiceProvider"/> constructor because Data Protection creates it by type name.</summary>
public sealed class HubKeyDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        var secret = (ConnectionSecretKey)services.GetService(typeof(ConnectionSecretKey))!;
        if (secret.Key is not { } current)
        {
            throw new InvalidOperationException("Hub mode needs the data source encryption key.");
        }

        // Current key first, then the previous ones, like data source secrets.
        foreach (var key in new[] { current }.Concat(secret.PreviousKeys))
        {
            if (HubKeyCipher.TryDecrypt(key, encryptedElement.Value) is { } xml)
            {
                return XElement.Parse(xml);
            }
        }

        throw new CryptographicException("The session key ring cannot be read with the configured encryption keys.");
    }
}

internal static class HubKeyCipher
{
    private const byte Version = 1;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("QueryBuilder.Hub.DataProtection");

    public static string Encrypt(byte[] key, string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var blob = new byte[1 + NonceBytes + TagBytes + plain.Length];
        blob[0] = Version;
        RandomNumberGenerator.Fill(blob.AsSpan(1, NonceBytes));
        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(blob.AsSpan(1, NonceBytes), plain, blob.AsSpan(1 + NonceBytes + TagBytes), blob.AsSpan(1 + NonceBytes, TagBytes), AssociatedData);
        CryptographicOperations.ZeroMemory(plain);
        return Convert.ToBase64String(blob);
    }

    public static string? TryDecrypt(byte[] key, string base64)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (blob.Length < 1 + NonceBytes + TagBytes || blob[0] != Version)
        {
            return null;
        }

        var plain = new byte[blob.Length - 1 - NonceBytes - TagBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(blob.AsSpan(1, NonceBytes), blob.AsSpan(1 + NonceBytes + TagBytes), blob.AsSpan(1 + NonceBytes, TagBytes), plain, AssociatedData);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
