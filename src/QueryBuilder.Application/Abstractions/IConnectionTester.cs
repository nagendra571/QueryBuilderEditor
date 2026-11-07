using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Abstractions;

/// <summary>Opens a connection and runs <c>SELECT 1</c> within ten seconds. Never throws for a failed
/// attempt (that is the result); only cancellation by the caller propagates.</summary>
public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(DataSourceProvider provider, ManagedConnectionDetails details, CancellationToken cancellationToken);

    /// <summary>Tests whatever the stored row connects with, managed or from host configuration.</summary>
    Task<ConnectionTestResult> TestStoredAsync(DataSource dataSource, CancellationToken cancellationToken);
}
