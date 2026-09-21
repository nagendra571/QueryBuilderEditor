using Microsoft.Extensions.Configuration;
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
public sealed class ConnectionStringResolver(IConfiguration configuration, MetadataConnectionStringAccessor metadataConnectionString) : IConnectionStringResolver
{
    public string Resolve(DataSource dataSource)
    {
        if (dataSource.ConnectionStringName == DataSource.DefaultConnectionStringSentinel)
        {
            return metadataConnectionString.ConnectionString;
        }

        return configuration.GetConnectionString(dataSource.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"No connection string named '{dataSource.ConnectionStringName}' is configured for data source '{dataSource.Name}'.");
    }
}
