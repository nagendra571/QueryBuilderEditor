using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Editor.Authorization;
using QueryBuilder.Editor.DataScoping;
using QueryBuilder.Editor.Diagnostics;
using QueryBuilder.Editor.Identity;
using QueryBuilder.Editor.Middleware;
using QueryBuilder.Infrastructure;
using QueryBuilder.Infrastructure.Security;

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

        byte[]? secretKey = null;
        var management = options.DataSourceManagement;
        if (management.EncryptionKey is { Length: > 0 })
        {
            if (!ConnectionSecretKey.TryParse(management.EncryptionKey, out var parsed, out var keyError))
            {
                throw new InvalidOperationException(
                    $"{nameof(QueryBuilderEditorOptions.DataSourceManagement)}.{nameof(DataSourceManagementOptions.EncryptionKey)} is invalid: {keyError} " +
                    "Use base64 of exactly 32 bytes, for example the output of `openssl rand -base64 32`.");
            }

            secretKey = parsed;
        }

        var previousKeys = new List<byte[]>();
        var previousEntries = management.PreviousEncryptionKeys ?? [];
        const string previousName = nameof(DataSourceManagementOptions.PreviousEncryptionKeys);
        if (previousEntries.Count > 0 && secretKey is null)
        {
            throw new InvalidOperationException(
                $"{nameof(QueryBuilderEditorOptions.DataSourceManagement)}.{previousName} needs " +
                $"{nameof(DataSourceManagementOptions.EncryptionKey)}: previous keys are only for reading, so there has to be a current key to write with.");
        }

        for (var i = 0; i < previousEntries.Count; i++)
        {
            // Errors name the index only: the entry is a secret and must never reach a log.
            if (!ConnectionSecretKey.TryParse(previousEntries[i], out var previous, out var previousError))
            {
                throw new InvalidOperationException(
                    $"{nameof(QueryBuilderEditorOptions.DataSourceManagement)}.{previousName}[{i}] is invalid: {previousError} " +
                    "Use base64 of exactly 32 bytes.");
            }

            if (previous.AsSpan().SequenceEqual(secretKey))
            {
                throw new InvalidOperationException(
                    $"{nameof(QueryBuilderEditorOptions.DataSourceManagement)}.{previousName}[{i}] is the same as {nameof(DataSourceManagementOptions.EncryptionKey)}. " +
                    "Remove it: the current key is always tried first.");
            }

            var duplicate = previousKeys.FindIndex(k => k.AsSpan().SequenceEqual(previous));
            if (duplicate >= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(QueryBuilderEditorOptions.DataSourceManagement)}.{previousName}[{i}] repeats entry {duplicate}. Remove one of them.");
            }

            previousKeys.Add(previous);
        }

        if (management.Enabled)
        {
            if (secretKey is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(QueryBuilderEditorOptions.DataSourceManagement)}.{nameof(DataSourceManagementOptions.Enabled)} needs " +
                    $"{nameof(DataSourceManagementOptions.EncryptionKey)}: base64 of 32 random bytes, for example `openssl rand -base64 32`.");
            }

            if (options.AdminAuthorization.Mode == QueryBuilderAuthorizationMode.Anonymous
                && string.IsNullOrWhiteSpace(options.AdminAuthorization.PolicyName)
                && !management.AllowAnonymousAdmins)
            {
                throw new InvalidOperationException(
                    $"{nameof(QueryBuilderEditorOptions.DataSourceManagement)}.{nameof(DataSourceManagementOptions.Enabled)} lets admins point this app at any server, " +
                    $"so {nameof(QueryBuilderEditorOptions.AdminAuthorization)} can't be Anonymous. Restrict it, or set " +
                    $"{nameof(DataSourceManagementOptions.AllowAnonymousAdmins)} = true for local development only.");
            }
        }

        // Registered as the resolved instance (not IOptions<T>) — there is exactly one QueryBuilder
        // configuration per host, set once here at startup.
        services.AddSingleton(options);
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICurrentDataScope, CurrentDataScopeService>();
        services.AddSingleton(new RecordLimitSettings(options.DefaultMaxRecords));
        // Re-encrypting connects nowhere, so it doesn't need Enabled, but it follows the same admin rule: with apps
        // sharing one database mid-rotation, an early run makes sources unreadable to an app still on the old key.
        var anonymousAdmins = options.AdminAuthorization.Mode == QueryBuilderAuthorizationMode.Anonymous
            && string.IsNullOrWhiteSpace(options.AdminAuthorization.PolicyName);
        var reencryptAllowed = secretKey is not null && (!anonymousAdmins || management.AllowAnonymousAdmins);
        services.AddSingleton(new DataSourceManagementSettings(management.Enabled, reencryptAllowed));

        services.AddApplication();
        services.AddInfrastructure(options.ConnectionString, options.ApplyMigrations, secretKey, previousKeys, options.Hub.Enabled);

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        // Setup diagnostics: GET /_setup in Development, regardless of how much of the rest of the
        // pipeline is wired up correctly. See Diagnostics/SetupDiagnosticsStartupFilter.
        services.AddSingleton<QueryBuilderSetupState>();
        services.AddSingleton<IStartupFilter, SetupDiagnosticsStartupFilter>();

        return services;
    }
}
