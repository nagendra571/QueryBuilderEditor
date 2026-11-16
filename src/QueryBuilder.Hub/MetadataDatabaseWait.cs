using Microsoft.Data.SqlClient;

namespace QueryBuilder.Hub;

/// <summary>
/// Waits, before anything else touches the metadata database, until it accepts a connection. An Azure SQL serverless
/// database that auto-paused refuses at once with "is not currently available" while it resumes, and the migration and
/// mode-guard services treat any startup error as fatal: the process then dies and IIS keeps answering 500.30 until the
/// app is stopped and started again. Retrying here turns that into a slower first request.
/// </summary>
internal sealed class MetadataDatabaseWait(string connectionString, TimeSpan budget, ILogger<MetadataDatabaseWait> logger) : IHostedService
{
    // Azure SQL's documented transient connection errors, plus the "cannot open database" a resume can briefly return.
    private static readonly HashSet<int> TransientNumbers =
        [40613, 40197, 40501, 40540, 49918, 49919, 49920, 4060, 4221, 10928, 10929, 233, 64, 10053, 10054, 10060, 11001, -2];

    public Task StartAsync(CancellationToken cancellationToken) =>
        WaitAsync(
            async ct =>
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(ct);
            },
            IsTransient,
            budget,
            Task.Delay,
            logger,
            cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static bool IsTransient(Exception exception) =>
        exception is SqlException sql && (sql.IsTransient || sql.Errors.Cast<SqlError>().Any(e => TransientNumbers.Contains(e.Number)));

    /// <summary>
    /// Probes until it succeeds, a non-transient error occurs, or the budget runs out. It never throws for the database's
    /// sake: on a non-transient error or an exhausted budget it returns, and the normal startup reports the real error.
    /// </summary>
    internal static async Task<bool> WaitAsync(
        Func<CancellationToken, Task> probe,
        Func<Exception, bool> isTransient,
        TimeSpan budget,
        Func<TimeSpan, CancellationToken, Task> delay,
        ILogger logger,
        CancellationToken cancellationToken,
        Func<DateTimeOffset>? clock = null)
    {
        if (budget <= TimeSpan.Zero)
        {
            return false;
        }

        clock ??= () => DateTimeOffset.UtcNow;
        var deadline = clock() + budget;
        var pause = TimeSpan.FromSeconds(2);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await probe(cancellationToken);
                if (attempt > 1)
                {
                    logger.LogInformation("The metadata database is available after {Attempts} attempts.", attempt);
                }

                return true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && isTransient(ex))
            {
                var left = deadline - clock();
                if (left <= TimeSpan.Zero)
                {
                    logger.LogError("The metadata database was still unavailable when the startup wait ran out; starting anyway.");
                    return false;
                }

                // Only the message: it names the database and server, never the connection string or a password.
                logger.LogWarning("The metadata database is not available yet (attempt {Attempt}: {Message}); retrying.", attempt, ex.Message);
                await delay(pause < left ? pause : left, cancellationToken);
                pause = TimeSpan.FromSeconds(Math.Min(pause.TotalSeconds * 2, 10));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Wrong password, unknown server, missing permission: waiting will not fix it, so let startup report it.
                logger.LogDebug(ex, "The metadata database check failed with a non-transient error; not waiting.");
                return false;
            }
        }
    }
}
