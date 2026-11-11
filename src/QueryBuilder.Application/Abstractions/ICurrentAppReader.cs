using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Abstractions;

/// <summary>Reads the row of the app the request works in (null when there is none). Read-only: the registry is written
/// only from hub-wide scopes.</summary>
public interface ICurrentAppReader
{
    Task<App?> GetCurrentAsync(CancellationToken ct);
}
