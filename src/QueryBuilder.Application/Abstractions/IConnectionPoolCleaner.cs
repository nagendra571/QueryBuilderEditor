using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Abstractions;

/// <summary>Closes the pooled connections opened with a data source's current connection details, so a changed
/// password or a turn-off takes effect now instead of whenever the pool recycles. Best effort: does nothing if the
/// details can't be read.</summary>
public interface IConnectionPoolCleaner
{
    void Clear(DataSource dataSource);
}
