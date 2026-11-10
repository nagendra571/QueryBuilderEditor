using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Infrastructure.Audit;
using QueryBuilder.Infrastructure.Catalog;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Hub;
using QueryBuilder.Application.Hub;
using QueryBuilder.Infrastructure.Export;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Repositories;
using QueryBuilder.Infrastructure.Security;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Pure plumbing — takes primitive config values rather than the public options object, so this
    /// project has no knowledge of "the editor" concept. <see cref="QueryBuilder.Editor.ServiceCollectionExtensions.AddQueryBuilderEditor"/>
    /// is the composition root that translates the host-facing options into this call.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, bool applyMigrations, byte[]? connectionSecretKey = null, IReadOnlyList<byte[]>? previousConnectionSecretKeys = null, bool hubMode = false)
    {
        services.AddSingleton(new MetadataMode(hubMode));
        services.AddScoped<AppContextAccessor>();
        services.AddScoped<IAppContext>(sp => sp.GetRequiredService<AppContextAccessor>());
        services.AddScoped<IAppContextScope>(sp => sp.GetRequiredService<AppContextAccessor>());

        services.AddSingleton<IHubWideScopeFactory, HubWideScopeFactory>();
        if (hubMode)
        {
            // Needs LaunchValidationSettings, which only hub mode registers; build-time validation would fail embedded hosts otherwise.
            services.AddSingleton<ILaunchTokenValidator, LaunchTokenValidator>();
            services.AddSingleton<LaunchReplayStore>();
        }

        services.AddDbContext<AppDbContext>(db =>
            db.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(new MetadataConnectionStringAccessor(connectionString));

        if (applyMigrations)
        {
            services.AddHostedService<DatabaseMigrationHostedService>();
        }

        // Registered after the migration hosted service (registration order = startup order) so
        // that when migrations are enabled, the schema is guaranteed to exist by the time these run.
        services.AddHostedService<MetadataModeGuard>();
        services.AddHostedService<DefaultDataSourceSeeder>();

        services.AddScoped<ISavedQueryRepository, SavedQueryRepository>();
        services.AddScoped<IDataSourceRepository, DataSourceRepository>();
        services.AddScoped<IDataScopeRuleRepository, DataScopeRuleRepository>();
        services.AddScoped<IVisualizationRepository, VisualizationRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();

        // Resolves connection strings for the business data sources users query (by DataSource.ConnectionStringName)
        // via the host's own IConfiguration — already registered by the host, no wiring needed here.
        services.AddSingleton(new ConnectionSecretKey(connectionSecretKey, previousConnectionSecretKeys));
        services.AddSingleton<IConnectionSecretProtector, AesGcmConnectionSecretProtector>();
        services.AddSingleton<IConnectionStringResolver, ConnectionStringResolver>();
        services.AddSingleton<IConnectionTester, ConnectionTester>();
        services.AddSingleton<IConnectionPoolCleaner, ConnectionPoolCleaner>();
        services.AddSingleton<ICatalogReader, SqlServerCatalogReader>();
        services.AddSingleton<ICatalogReader, PostgresCatalogReader>();
        services.AddScoped<IDataCatalogService, DataCatalogService>();
        services.AddScoped<IAppUsersDirectory, AppUsersDirectory>();

        services.AddSingleton<IQuerySqlBuilder, SqlServerQuerySqlBuilder>();
        services.AddSingleton<IQuerySqlBuilder, PostgresQuerySqlBuilder>();
        services.AddSingleton<IQuerySqlBuilderFactory, QuerySqlBuilderFactory>();

        services.AddScoped<SqlServerQueryExecutionService>();
        services.AddScoped<PostgresQueryExecutionService>();
        services.AddScoped<IQueryExecutionService, RoutingQueryExecutionService>();
        services.AddSingleton<IExportService, ExportService>();

        return services;
    }
}
