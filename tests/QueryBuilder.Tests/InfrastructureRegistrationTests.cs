using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Tests;

public class InfrastructureRegistrationTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // Nothing is opened at registration, so these never need to be reachable.
        services.AddInfrastructure("Server=never.invalid;Database=x;Integrated Security=true", applyMigrations: false);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    [Fact]
    public void Query_execution_resolves_to_the_provider_router()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.IsType<RoutingQueryExecutionService>(scope.ServiceProvider.GetRequiredService<IQueryExecutionService>());
    }

    [Fact]
    public void Builder_factory_returns_the_right_builder_for_each_provider()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IQuerySqlBuilderFactory>();

        Assert.IsType<SqlServerQuerySqlBuilder>(factory.GetBuilder(DataSourceProvider.SqlServer));
        Assert.IsType<PostgresQuerySqlBuilder>(factory.GetBuilder(DataSourceProvider.PostgreSql));
    }
}
