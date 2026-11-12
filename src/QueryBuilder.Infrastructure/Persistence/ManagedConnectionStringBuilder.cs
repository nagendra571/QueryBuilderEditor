using Microsoft.Data.SqlClient;
using Npgsql;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Persistence;

/// <summary>
/// Turns typed-in details into a connection string using only the providers' own builders, never string
/// concatenation, so a host such as <c>x;Password=y</c> stays one (invalid) value instead of adding a keyword.
/// </summary>
public static class ManagedConnectionStringBuilder
{
    public const int ConnectTimeoutSeconds = 10;

    // Npgsql reads PGPASSWORD (and .pgpass) when the password is null, and "Password=" parses back as null, which
    // would hand the host's own secret to whatever server an admin typed. A filler is never null, so trust
    // authentication ignores it and password authentication fails closed. One per process, so every build of the same
    // details is the same string and the pool isn't split.
    private static readonly string BlankPasswordFiller = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    public static string Build(DataSourceProvider provider, ManagedConnectionDetails details) => provider switch
    {
        DataSourceProvider.SqlServer => BuildSqlServer(details),
        DataSourceProvider.PostgreSql => BuildPostgres(details),
        _ => throw new NotSupportedException($"Data source provider '{provider}' can't be managed here.")
    };

    // Always tcp:. Without a prefix SqlClient may pick shared memory for localhost, and a SQL Browser reply for an
    // instance name could steer it to a named pipe, which can start an outbound SMB login with this server's identity.
    // A named instance therefore needs its port.
    public static string SqlServerDataSource(ManagedConnectionDetails details) => $"tcp:{details.Host},{details.Port}";

    private static string BuildSqlServer(ManagedConnectionDetails details)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = SqlServerDataSource(details),
            InitialCatalog = details.Database,
            UserID = details.Username,
            Password = details.Password,
            ConnectTimeout = ConnectTimeoutSeconds
        };

        switch (details.SslMode)
        {
            case ConnectionSslMode.Disable:
                builder.Encrypt = SqlConnectionEncryptOption.Optional;
                break;
            case ConnectionSslMode.Require:
                builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
                builder.TrustServerCertificate = true;
                break;
            case ConnectionSslMode.VerifyFull:
                builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
                builder.TrustServerCertificate = false;
                break;
        }

        return builder.ConnectionString;
    }

    private static string BuildPostgres(ManagedConnectionDetails details)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = details.Host,
            Port = details.Port,
            Database = details.Database,
            Username = details.Username,
            Password = details.Password.Length == 0 ? BlankPasswordFiller : details.Password,
            Timeout = ConnectTimeoutSeconds,
            SslMode = details.SslMode switch
            {
                ConnectionSslMode.Disable => SslMode.Disable,
                ConnectionSslMode.Require => SslMode.Require,
                _ => SslMode.VerifyFull
            }
        };

        return builder.ConnectionString;
    }
}
