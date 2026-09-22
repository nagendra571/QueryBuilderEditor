using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
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

        try
        {
            var connectionString = connectionStringResolver.Resolve(dataSource);
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
            logger.LogDebug(ex, "dbo.AppUsers not usable for data source {DataSourceId}; sharing unavailable there.", dataSourceId);
            return new AppUsersResult(Available: false, []);
        }
    }
}
