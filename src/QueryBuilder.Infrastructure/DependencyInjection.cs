using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Infrastructure.Audit;
using QueryBuilder.Infrastructure.Catalog;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Export;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Repositories;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Pure plumbing — takes primitive config values rather than the public options object, so this
    /// project has no knowledge of "the editor" concept. <see cref="QueryBuilder.Editor.ServiceCollectionExtensions.AddQueryBuilderEditor"/>
    /// is the composition root that translates the host-facing options into this call.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, bool applyMigrations)
    {
        services.AddDbContext<AppDbContext>(db =>
            db.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        if (applyMigrations)
        {
            services.AddHostedService<DatabaseMigrationHostedService>();
        }

        services.AddScoped<ISavedQueryRepository, SavedQueryRepository>();
        services.AddScoped<IDataSourceRepository, DataSourceRepository>();

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();

        // Resolves connection strings for the business data sources users query (by DataSource.ConnectionStringName)
        // via the host's own IConfiguration — already registered by the host, no wiring needed here.
        services.AddSingleton<IConnectionStringResolver, ConnectionStringResolver>();
        services.AddScoped<IDataCatalogService, SqlServerDataCatalogService>();

        services.AddSingleton<IQuerySqlBuilder, SqlServerQuerySqlBuilder>();
        services.AddSingleton<IQuerySqlBuilderFactory, QuerySqlBuilderFactory>();

        services.AddScoped<IQueryExecutionService, SqlServerQueryExecutionService>();
        services.AddSingleton<IExportService, ExportService>();

        return services;
    }
}
