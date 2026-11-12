using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Catalog;

/// <summary>
/// Reads the optional <c>dbo.AppUsers</c> convention view (Id, DisplayName, Email) from a data
/// source's own database. Any failure to read it — missing view, wrong shape, connection issue —
/// is treated as "sharing isn't configured here" (<see cref="AppUsersResult.Available"/> false)
/// rather than an error, since the view is opt-in.
/// </summary>
public sealed class AppUsersDirectory(
    IDataSourceRepository dataSourceRepository,
    IConnectionStringResolver connectionStringResolver,
    ILogger<AppUsersDirectory> logger) : IAppUsersDirectory
{
    private const int MaxUsers = 500;

    public async Task<AppUsersResult> GetUsersAsync(Guid dataSourceId, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(dataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), dataSourceId);

        // Turned off means no connection at all, not even to list users.
        if (!dataSource.IsActive)
        {
            return new AppUsersResult(Available: false, []);
        }

        try
        {
            var connectionString = connectionStringResolver.Resolve(dataSource);
            if (dataSource.Provider == DataSourceProvider.PostgreSql)
            {
                return await GetPostgresUsersAsync(connectionString, cancellationToken);
            }

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"SELECT TOP ({MaxUsers}) Id, DisplayName, Email FROM dbo.AppUsers ORDER BY DisplayName";
            await using var cmd = new SqlCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var users = new List<AppUserDto>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var displayName = reader.GetString(1);
                var email = reader.IsDBNull(2) ? null : reader.GetString(2);
                users.Add(new AppUserDto(id, displayName, email));
            }

            return new AppUsersResult(Available: true, users);
        }
        catch (Exception ex)
        {
            // Expected in the common case — most data sources never define this view.
            logger.LogDebug(ex, "The AppUsers view is not usable for data source {DataSourceId}; sharing unavailable there.", dataSourceId);
            return new AppUsersResult(Available: false, []);
        }
    }

    private static async Task<AppUsersResult> GetPostgresUsersAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Same read-only session as query execution, so a hostile view can't write through this path either.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var session = new NpgsqlCommand(PostgresQueryExecutionService.SessionStatement, connection, transaction))
        {
            await session.ExecuteNonQueryAsync(cancellationToken);
        }

        var sql = $"SELECT id::text, display_name, email FROM public.app_users ORDER BY display_name LIMIT {MaxUsers}";
        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var users = new List<AppUserDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new AppUserDto(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return new AppUsersResult(Available: true, users);
    }
}
