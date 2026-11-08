namespace QueryBuilder.Application.Abstractions;

/// <summary>Encrypts a managed data source's connection details. The data source's Id is bound into the
/// ciphertext, so a blob copied to another row won't decrypt there.</summary>
public interface IConnectionSecretProtector
{
    /// <exception cref="Exceptions.ConnectionSecretException">No encryption key is configured.</exception>
    string Protect(Guid dataSourceId, string plaintext);

    /// <exception cref="Exceptions.ConnectionSecretException">No key, a key that opens it neither now nor among the previous ones, a changed blob, or another row's blob.</exception>
    string Unprotect(Guid dataSourceId, string ciphertext);

    /// <summary>Which key opens a stored blob, without returning what is inside. No key configured is Unreadable.</summary>
    SecretKeyMatch Identify(Guid dataSourceId, string ciphertext);

    /// <summary>Null when no key is configured. A fingerprint is the only thing about a key that leaves the server.</summary>
    string? CurrentFingerprint { get; }

    IReadOnlyList<string> PreviousFingerprints { get; }
}

public enum SecretKeyMatch
{
    Current = 0,
    Previous = 1,
    Unreadable = 2
}
