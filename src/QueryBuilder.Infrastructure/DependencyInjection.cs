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
        services.AddScoped<ICurrentAppReader, CurrentAppReader>();
        services.AddScoped<ICurrentAppSettings, CurrentAppSettings>();
        services.AddSingleton<IMetadataDatabaseIdentity, MetadataDatabaseIdentity>();
        if (hubMode)
        {
            // Needs LaunchValidationSettings, which only hub mode registers; build-time validation would fail embedded hosts otherwise.
            services.AddSingleton<ILaunchTokenValidator, LaunchTokenValidator>();
            services.AddSingleton<LaunchReplayStore>();
            services.AddScoped<QueryBuilder.Application.Hub.ILaunchRecorder, LaunchRecorder>();
            services.AddScoped<QueryBuilder.Application.Hub.Registry.IAppRegistry, AppRegistry>();
            services.AddScoped<QueryBuilder.Application.Hub.Registry.IHubAuditReader, HubAuditReader>();
            services.AddScoped<QueryBuilder.Application.DataSourceManagement.IDataSourceMover, DataSourceMover>();
        }

        services.AddDbContext<AppDbContext>(db =>
            db.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(new MetadataConnectionStringAccessor(connectionString));

        if (hubMode)
        {
            // Data Protection's own hosted service (registered by the first AddAuthentication/AddDataProtection call, which
            // may be the host's, earlier than this one) reads the key table at startup. Inserting at the front makes the
            // migration and the mode guard run before it whatever the host registered first; migration stays ahead of the guard.
            services.Insert(0, ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, MetadataModeGuard>());
            if (applyMigrations)
            {
                services.Insert(0, ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, DatabaseMigrationHostedService>());
            }
        }
        else
        {
            if (applyMigrations)
            {
                services.AddHostedService<DatabaseMigrationHostedService>();
            }

            // Registered after the migration hosted service (registration order = startup order) so
            // that when migrations are enabled, the schema is guaranteed to exist by the time these run.
            services.AddHostedService<MetadataModeGuard>();
        }


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
        services.AddScoped<ISeenUserDirectory, SeenUserDirectory>();

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
