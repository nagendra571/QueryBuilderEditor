using Microsoft.Data.SqlClient;
using Npgsql;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Persistence;

public sealed class ConnectionPoolCleaner(IConnectionStringResolver resolver) : IConnectionPoolCleaner
{
    public void Clear(DataSource dataSource)
    {
        try
        {
            var connectionString = resolver.Resolve(dataSource);
            if (dataSource.Provider == DataSourceProvider.PostgreSql)
            {
                using var connection = new NpgsqlConnection(connectionString);
                NpgsqlConnection.ClearPool(connection);
            }
            else
            {
                using var connection = new SqlConnection(connectionString);
                SqlConnection.ClearPool(connection);
            }
        }
        catch (Exception)
        {
            // Unreadable or missing details mean nothing was pooled under them.
        }
    }
}
