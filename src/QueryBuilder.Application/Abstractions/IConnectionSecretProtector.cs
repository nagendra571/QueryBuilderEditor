namespace QueryBuilder.Application.Abstractions;

/// <summary>Encrypts a managed data source's connection details. The data source's Id is bound into the
/// ciphertext, so a blob copied to another row won't decrypt there.</summary>
public interface IConnectionSecretProtector
{
    /// <exception cref="Exceptions.ConnectionSecretException">No encryption key is configured.</exception>
    string Protect(Guid dataSourceId, string plaintext);

    /// <exception cref="Exceptions.ConnectionSecretException">No key, the wrong key, a changed blob, or another row's blob.</exception>
    string Unprotect(Guid dataSourceId, string ciphertext);
}
