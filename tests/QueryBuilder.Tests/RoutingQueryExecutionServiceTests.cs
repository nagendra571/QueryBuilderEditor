using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Tests;

public class RoutingQueryExecutionServiceTests
{
    private sealed class NoConnections : IConnectionStringResolver
    {
        public string Resolve(DataSource dataSource) => throw new InvalidOperationException("must not be reached");
    }

    private static Task<QueryResultSet> Run(DataSourceProvider provider)
    {
        var router = new RoutingQueryExecutionService(
            new SqlServerQueryExecutionService(new NoConnections()),
            new PostgresQueryExecutionService(new NoConnections(), NullLogger<PostgresQueryExecutionService>.Instance));

        return router.ExecuteAsync(
            new DataSource { Name = "x", Provider = provider }, new GeneratedQuery("SELECT 1", []),
            new Dictionary<string, string?>(), null, default);
    }

    [Fact]
    public async Task Routing_refuses_mysql_with_clear_message()
    {
        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Run(DataSourceProvider.MySql));

        Assert.Equal("This data source's provider 'MySql' isn't supported.", ex.Message);
    }

    [Fact]
    public async Task Routing_refuses_sqlite_with_clear_message()
    {
        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Run(DataSourceProvider.Sqlite));

        Assert.Equal("This data source's provider 'Sqlite' isn't supported.", ex.Message);
    }
}
