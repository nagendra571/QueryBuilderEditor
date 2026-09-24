using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Editor.DataScoping;
using QueryBuilder.Editor.Diagnostics;
using QueryBuilder.Editor.Identity;
using QueryBuilder.Editor.Middleware;
using QueryBuilder.Infrastructure;

namespace QueryBuilder.Editor;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything QueryBuilder needs: application services, EF Core persistence,
    /// catalog/execution/export services, the audit log, and (unless
    /// <see cref="QueryBuilderEditorOptions.ApplyMigrations"/> is false) automatic migrations on
    /// startup. Follow with <c>app.MapQueryBuilderEditor()</c> and <c>app.UseExceptionHandler()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddQueryBuilderEditor(options =>
    /// {
    ///     options.ConnectionString = builder.Configuration.GetConnectionString("QueryBuilderDb")!;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddQueryBuilderEditor(this IServiceCollection services, Action<QueryBuilderEditorOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        var options = new QueryBuilderEditorOptions { ConnectionString = string.Empty };
        configureOptions(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                $"{nameof(QueryBuilderEditorOptions)}.{nameof(QueryBuilderEditorOptions.ConnectionString)} must be set in the AddQueryBuilderEditor callback.");
        }

        options.DataScope.Validate();

        if (options.DefaultMaxRecords is { } defaultMax && !RecordLimits.IsValid(defaultMax))
        {
            throw new InvalidOperationException(
                $"{nameof(QueryBuilderEditorOptions)}.{nameof(QueryBuilderEditorOptions.DefaultMaxRecords)} must be between " +
                $"{RecordLimits.Min} and {RecordLimits.Max}, or left unset for no limit.");
        }

        // Registered as the resolved instance (not IOptions<T>) — there is exactly one QueryBuilder
        // configuration per host, set once here at startup.
        services.AddSingleton(options);
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICurrentDataScope, CurrentDataScopeService>();
        services.AddSingleton(new RecordLimitSettings(options.DefaultMaxRecords));

        services.AddApplication();
        services.AddInfrastructure(options.ConnectionString, options.ApplyMigrations);

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        // Setup diagnostics: GET /_setup in Development, regardless of how much of the rest of the
        // pipeline is wired up correctly. See Diagnostics/SetupDiagnosticsStartupFilter.
        services.AddSingleton<QueryBuilderSetupState>();
        services.AddSingleton<IStartupFilter, SetupDiagnosticsStartupFilter>();

        return services;
    }
}
