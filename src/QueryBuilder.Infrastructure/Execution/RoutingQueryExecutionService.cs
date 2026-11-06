using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Execution;

public sealed class RoutingQueryExecutionService(
    SqlServerQueryExecutionService sqlServer,
    PostgresQueryExecutionService postgres) : IQueryExecutionService
{
    public Task<QueryResultSet> ExecuteAsync(
        DataSource dataSource,
        GeneratedQuery generatedQuery,
        IReadOnlyDictionary<string, string?> runtimeParameterValues,
        int? maxRows,
        CancellationToken cancellationToken) =>
        dataSource.Provider switch
        {
            DataSourceProvider.SqlServer => sqlServer.ExecuteAsync(dataSource, generatedQuery, runtimeParameterValues, maxRows, cancellationToken),
            DataSourceProvider.PostgreSql => postgres.ExecuteAsync(dataSource, generatedQuery, runtimeParameterValues, maxRows, cancellationToken),
            _ => throw new CatalogValidationException($"This data source's provider '{dataSource.Provider}' isn't supported.")
        };
}
