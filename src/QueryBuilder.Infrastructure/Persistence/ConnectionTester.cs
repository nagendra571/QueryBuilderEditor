using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Persistence;

public sealed class ConnectionTester(IConnectionStringResolver resolver, ILogger<ConnectionTester> logger, IMetadataDatabaseIdentity? metadataIdentity = null) : IConnectionTester
{
    private const int LimitSeconds = 10;
    public const string Unreachable = "Couldn't reach the server.";
    public const string Succeeded = "Connected.";

    public Task<ConnectionTestResult> TestAsync(DataSourceProvider provider, ManagedConnectionDetails details, CancellationToken cancellationToken) =>
        RunAsync(provider, () => ManagedConnectionStringBuilder.Build(provider, details), cancellationToken, refuseMetadataDatabase: false);

    public async Task<ConnectionTestResult> TestStoredAsync(DataSource dataSource, CancellationToken cancellationToken)
    {
        return await RunAsync(dataSource.Provider, () =>
        {
            try
            {
                return resolver.Resolve(dataSource);
            }
            catch (QueryExecutionException)
            {
                throw new ConnectionSecretException(ManagedConnections.SecretUnavailableMessage);
            }
            catch (InvalidOperationException)
            {
                // Only resolving the configured name can mean "no such connection string".
                throw new ConnectionSecretException("No connection string with the configured name was found in the host's configuration.");
            }
        }, cancellationToken, refuseMetadataDatabase: true);
    }

    // Unsaved details are checked by the handler before any connection; a stored row only has its string here.
    private async Task<ConnectionTestResult> RunAsync(DataSourceProvider provider, Func<string> buildConnectionString, CancellationToken cancellationToken, bool refuseMetadataDatabase)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(LimitSeconds));

        try
        {
            var built = buildConnectionString();
            if (provider == DataSourceProvider.PostgreSql)
            {
                // No pooling: a test has to prove the password it was given, not reuse a connection opened with an older one.
                var connectionString = new NpgsqlConnectionStringBuilder(built) { Pooling = false }.ConnectionString;
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(limit.Token);
                await using var command = new NpgsqlCommand("SELECT 1", connection) { CommandTimeout = LimitSeconds };
                await command.ExecuteScalarAsync(limit.Token);
            }
            else
            {
                var connectionString = new SqlConnectionStringBuilder(built) { Pooling = false }.ConnectionString;
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(limit.Token);
                await using var command = new SqlCommand("SELECT 1", connection) { CommandTimeout = LimitSeconds };
                await command.ExecuteScalarAsync(limit.Token);
                if (refuseMetadataDatabase && metadataIdentity is not null && await metadataIdentity.IsSameAsync(connection, limit.Token))
                {
                    throw new CatalogValidationException(MetadataDatabaseMessages.Refused);
                }
            }

            return new ConnectionTestResult(true, Succeeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestResult(false, Unreachable);
        }
        catch (CatalogValidationException)
        {
            throw;
        }
        catch (ConnectionSecretException ex)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Failed(ex, "The connection details aren't valid.");
        }
        catch (PostgresException ex)
        {
            return Failed(ex, ex.MessageText);
        }
        catch (SqlException ex)
        {
            return Failed(ex, IsNetworkFailure(ex) ? Unreachable : FirstLine(ex.Message));
        }
        catch (Exception ex)
        {
            return Failed(ex, Unreachable);
        }
    }

    private ConnectionTestResult Failed(Exception ex, string message)
    {
        // The exception can echo the host or login, but never the password; still, keep it out of the response.
        logger.LogInformation(ex, "A data source connection test failed.");
        return new ConnectionTestResult(false, message);
    }

    // Numbers for a server that never answered: timeouts, name resolution, refused connections, no instance.
    private static bool IsNetworkFailure(SqlException ex) =>
        ex.Number is -2 or -1 or 2 or 20 or 53 or 64 or 121 or 258 or 1231 or 10051 or 10053 or 10054 or 10060 or 10061 or 11001 or 40 or 26 or 25;

    private static string FirstLine(string message) =>
        message.Split('\n', '\r')[0].Trim();
}
