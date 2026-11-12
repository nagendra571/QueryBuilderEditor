using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence;

public interface IConnectionStringResolver
{
    string Resolve(DataSource dataSource);
}

/// <summary>
/// Looks up the real connection string for a <see cref="DataSource"/> from configuration by name.
/// Keeping the indirection means the metadata DB never stores a raw connection string/secret.
/// </summary>
public sealed class ConnectionStringResolver(
    IConfiguration configuration,
    MetadataConnectionStringAccessor metadataConnectionString,
    IConnectionSecretProtector protector,
    ILogger<ConnectionStringResolver> logger) : IConnectionStringResolver
{
    public const string ConnectFailed = "Couldn't connect to this data source.";

    public string Resolve(DataSource dataSource)
    {
        if (dataSource.IsManaged)
        {
            return ResolveManaged(dataSource);
        }

        if (dataSource.ConnectionStringName == DataSource.DefaultConnectionStringSentinel)
        {
            return metadataConnectionString.ConnectionString;
        }

        return configuration.GetConnectionString(dataSource.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"No connection string named '{dataSource.ConnectionStringName}' is configured for data source '{dataSource.Name}'.");
    }

    private string ResolveManaged(DataSource dataSource)
    {
        try
        {
            if (string.IsNullOrEmpty(dataSource.EncryptedConnection))
            {
                throw new ConnectionSecretException("The data source has no saved connection details.");
            }

            var details = ManagedConnections.Deserialize(protector.Unprotect(dataSource.Id, dataSource.EncryptedConnection));
            return ManagedConnectionStringBuilder.Build(dataSource.Provider, details);
        }
        catch (Exception ex) when (ex is ConnectionSecretException or JsonException or ArgumentException or NotSupportedException)
        {
            // Only this source fails; the log carries the id and the exception, which never holds a secret.
            logger.LogWarning(ex, "Couldn't read the saved connection of data source {DataSourceId}.", dataSource.Id);
            throw new QueryExecutionException(ConnectFailed);
        }
    }
}
