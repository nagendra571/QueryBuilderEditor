using System.Text.Json;
using System.Text.Json.Serialization;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.DataSourceManagement;

/// <summary>Whether the admin UI may create and edit data sources. The key alone (without this) is enough
/// to query managed sources that already exist.</summary>
public sealed record DataSourceManagementSettings(bool Enabled, bool ReencryptAllowed = false);

/// <summary>What the admin types. Everything nullable so a missing field becomes a validation message, not a crash.</summary>
public sealed record ConnectionInput(
    string? Host, int? Port, string? Database, string? Username, string? Password, ConnectionSslMode? SslMode);

/// <summary>The whole blob that is encrypted and stored: <c>{host, port, database, username, password, sslMode}</c>.</summary>
public sealed record ManagedConnectionDetails(
    string Host, int Port, string Database, string Username, string Password, ConnectionSslMode SslMode);

public sealed record ConnectionTestResult(bool Ok, string Message);

/// <summary>What an admin may read back. There is deliberately no password member, only whether one is stored.</summary>
public sealed record ConnectionDto(string Host, int Port, string Database, string Username, ConnectionSslMode SslMode, bool HasPassword);

public static class ManagedConnections
{
    public const string RePasswordMessage = "Re-enter the password when changing the host, port, username or lowering encryption.";
    public const string RePasswordUnreadableMessage = "Re-enter the password: the saved details can't be read with the current key.";
    public const string SecretUnavailableMessage = "The saved connection details can't be read with the current key. Re-enter them.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static bool IsSupportedProvider(DataSourceProvider provider) =>
        provider is DataSourceProvider.SqlServer or DataSourceProvider.PostgreSql;

    public static int DefaultPort(DataSourceProvider provider) => provider == DataSourceProvider.PostgreSql ? 5432 : 1433;

    public static string Serialize(ManagedConnectionDetails details) => JsonSerializer.Serialize(details, Json);

    public static ManagedConnectionDetails Deserialize(string json) =>
        JsonSerializer.Deserialize<ManagedConnectionDetails>(json, Json) ?? throw new JsonException("Empty connection details.");

    /// <summary>Null when the source isn't managed or its blob can't be read (no key, wrong key, tampered).</summary>
    public static ManagedConnectionDetails? TryRead(DataSource dataSource, IConnectionSecretProtector protector)
    {
        if (!dataSource.IsManaged || string.IsNullOrEmpty(dataSource.EncryptedConnection))
        {
            return null;
        }

        try
        {
            return Deserialize(protector.Unprotect(dataSource.Id, dataSource.EncryptedConnection));
        }
        catch (Exception ex) when (ex is ConnectionSecretException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fills defaults and decides which password to use. A blank password keeps the stored one only when host, port
    /// and username are unchanged and the encryption setting isn't weakened, so an admin can't make the server send a
    /// saved password to another server, or in the clear. A managed source whose blob can't be read has no password to
    /// keep, and a blank one is refused instead of overwriting a blob the right key might still open.
    /// </summary>
    public static ManagedConnectionDetails Resolve(
        DataSourceProvider provider, ConnectionInput input, ManagedConnectionDetails? stored, bool storedUnreadable = false)
    {
        var host = input.Host?.Trim() ?? string.Empty;
        var port = input.Port ?? DefaultPort(provider);
        var username = input.Username ?? string.Empty;
        var ssl = input.SslMode ?? stored?.SslMode ?? ConnectionSslMode.Require;

        var password = input.Password ?? string.Empty;
        if (password.Length == 0)
        {
            if (storedUnreadable)
            {
                throw new CatalogValidationException(RePasswordUnreadableMessage);
            }

            if (stored is not null)
            {
                var unchanged = string.Equals(stored.Host, host, StringComparison.OrdinalIgnoreCase)
                                && stored.Port == port
                                && string.Equals(stored.Username, username, StringComparison.Ordinal)
                                && ssl >= stored.SslMode;
                if (!unchanged)
                {
                    throw new CatalogValidationException(RePasswordMessage);
                }

                password = stored.Password;
            }
        }

        RequireSupportedHost(provider, host);
        if (provider == DataSourceProvider.SqlServer)
        {
            if (password.Length > 128)
            {
                throw new CatalogValidationException("SQL Server limits the password to 128 characters.");
            }

            // The limit applies to the final Data Source value ("tcp:" + host + "," + port), not to the host alone.
            if (SqlServerDataSourceLength(host, port) > SqlServerDataSourceMax)
            {
                throw new CatalogValidationException(
                    $"The host is too long: with the port, SQL Server limits it to {SqlServerDataSourceMax - SqlServerDataSourceLength("", port)} characters.");
            }
        }

        return new ManagedConnectionDetails(host, port, input.Database?.Trim() ?? string.Empty, username, password, ssl);
    }

    // A named pipe (np:), a local path or (localdb) would reach things on this server, and np: can start an outbound
    // SMB login with the host's identity, so only names, addresses and (SQL Server) host\instance are accepted.
    private static readonly System.Text.RegularExpressions.Regex HostName = new(@"^[A-Za-z0-9._-]+$");
    private static readonly System.Text.RegularExpressions.Regex Ipv6 = new(@"^\[[0-9A-Fa-f:.]+\]$");
    private static readonly System.Text.RegularExpressions.Regex Instance = new(@"^[A-Za-z0-9._-]+\\[A-Za-z0-9_$#-]+$");

    public const int SqlServerDataSourceMax = 128;

    public static int SqlServerDataSourceLength(string host, int port) => $"tcp:{host},{port}".Length;

    public static bool IsAcceptableHost(string host, bool allowInstance) =>
        HostName.IsMatch(host) || Ipv6.IsMatch(host) || (allowInstance && Instance.IsMatch(host));

    public const string HostMessage = "The host must be a host name or IP address (or host\\instance for SQL Server), without prefixes such as np: or tcp:.";

    private static void RequireSupportedHost(DataSourceProvider provider, string host)
    {
        if (!IsAcceptableHost(host, allowInstance: provider == DataSourceProvider.SqlServer))
        {
            throw new CatalogValidationException(HostMessage);
        }

    }

    public static ConnectionDto ToDto(ManagedConnectionDetails details) =>
        new(details.Host, details.Port, details.Database, details.Username, details.SslMode, details.Password.Length > 0);

    public static void RequireEnabled(DataSourceManagementSettings settings)
    {
        if (!settings.Enabled)
        {
            throw new NotFoundException("Data source management isn't enabled.");
        }
    }
}

public static class DataSourceMessages
{
    public const string TurnedOff = "This data source is turned off.";
}
