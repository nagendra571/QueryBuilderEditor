using System.Data.Common;
using Microsoft.Data.SqlClient;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

public sealed class MetadataDatabaseIdentity(MetadataConnectionStringAccessor metadata, MetadataMode mode) : IMetadataDatabaseIdentity
{
    private const string IdentityQuery = "SELECT @@SERVERNAME, DB_NAME(), CAST(SERVERPROPERTY('MachineName') AS nvarchar(256))";
    private const int LimitSeconds = 10;

    private readonly SemaphoreSlim gate = new(1, 1);
    private (string?, string?, string?)? own;

    public async Task<bool> IsSameAsync(DbConnection openCandidate, CancellationToken ct)
    {
        if (!mode.Hub || openCandidate is not SqlConnection candidate)
        {
            return false;
        }

        var theirs = await ReadAsync(candidate, ct);
        return theirs.Item2 is not null && theirs.Equals(await OwnAsync(ct));
    }

    public async Task<bool> IsSameAsync(DataSourceProvider provider, string connectionString, CancellationToken ct)
    {
        if (!mode.Hub || provider != DataSourceProvider.SqlServer)
        {
            return false;
        }

        // Before touching the candidate, so a failure reading our own identity is never mistaken for "unreachable".
        await OwnAsync(ct);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(LimitSeconds));
        try
        {
            var built = new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
            await using var connection = new SqlConnection(built);
            await connection.OpenAsync(limit.Token);
            return await IsSameAsync(connection, limit.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // What can't be reached can't be QueryBuilder's database; the catalog load checks again later.
            return false;
        }
    }

    public Task<bool> IsSameAsync(DataSourceProvider provider, ManagedConnectionDetails details, CancellationToken ct)
    {
        if (!mode.Hub || provider != DataSourceProvider.SqlServer)
        {
            return Task.FromResult(false);
        }

        string built;
        try
        {
            built = ManagedConnectionStringBuilder.Build(provider, details);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Task.FromResult(false);
        }

        return IsSameAsync(provider, built, ct);
    }

    private async Task<(string?, string?, string?)> OwnAsync(CancellationToken ct)
    {
        if (own is { } known)
        {
            return known;
        }

        await gate.WaitAsync(ct);
        try
        {
            if (own is { } again)
            {
                return again;
            }

            try
            {
                await using var connection = new SqlConnection(metadata.ConnectionString);
                await connection.OpenAsync(ct);
                var read = await ReadAsync(connection, ct);
                own = read;
                return read;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The driver text would name the hub's own login and server; fail closed with a generic 400.
                throw new QueryBuilder.Application.Exceptions.CatalogValidationException("Couldn't check the connection.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<(string?, string?, string?)> ReadAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand(IdentityQuery, connection) { CommandTimeout = LimitSeconds };
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return default;
        }

        string? Cell(int i) => reader.IsDBNull(i) ? null : reader.GetString(i).ToUpperInvariant();
        return (Cell(0), Cell(1), Cell(2));
    }
}
