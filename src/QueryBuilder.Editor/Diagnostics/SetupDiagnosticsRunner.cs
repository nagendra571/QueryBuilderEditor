using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using QueryBuilder.Editor.Authorization;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Editor.Diagnostics;

internal static class SetupDiagnosticsRunner
{
    public static async Task<List<SetupCheck>> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var checks = new List<SetupCheck>();
        var options = services.GetRequiredService<QueryBuilderEditorOptions>();

        checks.Add(await CheckDatabaseConnectionAsync(services, cancellationToken));
        checks.Add(await CheckMigrationsAsync(services, options, cancellationToken));
        checks.Add(CheckApiMapped(services));
        checks.Add(CheckJsonOptions(services));
        checks.Add(CheckUiWiring(services));
        checks.Add(await CheckAuthorizationPipelineAsync(services, options));

        return checks;
    }

    private static async Task<SetupCheck> CheckDatabaseConnectionAsync(IServiceProvider services, CancellationToken ct)
    {
        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            var canConnect = await db.Database.CanConnectAsync(ct);
            return canConnect
                ? new SetupCheck("Database connection", true, "The configured connection string is reachable.")
                : new SetupCheck("Database connection", false, "Could not connect to the database.",
                    "Verify ConnectionString in AddQueryBuilderEditor and that the server is reachable from this host.");
        }
        catch (Exception ex)
        {
            return new SetupCheck("Database connection", false, $"Connection attempt threw: {ex.Message}",
                "Verify ConnectionString in AddQueryBuilderEditor and that the server is reachable from this host.");
        }
    }

    private static async Task<SetupCheck> CheckMigrationsAsync(IServiceProvider services, QueryBuilderEditorOptions options, CancellationToken ct)
    {
        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            if (pending.Count == 0)
            {
                return new SetupCheck("Migrations applied", true, "All EF Core schema migrations are current.");
            }

            var fix = options.ApplyMigrations
                ? "Pending migrations exist even though ApplyMigrations is true — check startup logs for errors from QueryBuilder's migration hosted service."
                : "ApplyMigrations is false. Have your DBA run the current scripts/QueryBuilder.schema.<version>.sql against this database (see Scripts/README.md).";

            return new SetupCheck("Migrations applied", false, $"{pending.Count} pending migration(s): {string.Join(", ", pending)}", fix);
        }
        catch (Exception ex)
        {
            return new SetupCheck("Migrations applied", false, $"Could not check migration state: {ex.Message}",
                "Resolve the database connection issue above first.");
        }
    }

    private static SetupCheck CheckApiMapped(IServiceProvider services)
    {
        var hasRoute = services.GetServices<EndpointDataSource>()
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Any(e => (e.RoutePattern.RawText ?? string.Empty).Contains($"{QueryBuilderRoutes.BasePath}/api/data-sources", StringComparison.OrdinalIgnoreCase));

        return hasRoute
            ? new SetupCheck("API endpoints mapped", true, "QueryBuilder's API routes are registered.")
            : new SetupCheck("API endpoints mapped", false, "No QueryBuilder API routes were found in the endpoint registry.",
                "Call app.MapQueryBuilderEditor() in Program.cs, after routing is set up.");
    }

    private static SetupCheck CheckJsonOptions(IServiceProvider services)
    {
        var jsonOptions = services.GetService<IOptions<JsonOptions>>()?.Value;
        var policy = jsonOptions?.SerializerOptions.PropertyNamingPolicy;
        var hasEnumConverter = jsonOptions?.SerializerOptions.Converters
            .Any(c => c.GetType().Name.Contains("EnumConverter", StringComparison.Ordinal)) ?? false;

        if (policy == System.Text.Json.JsonNamingPolicy.CamelCase && hasEnumConverter)
        {
            return new SetupCheck("JSON options configured", true, "camelCase naming and string enum conversion are active.");
        }

        return new SetupCheck("JSON options configured", false,
            "HTTP JSON options are missing camelCase naming and/or a string enum converter — the frontend expects both.",
            """Call builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)); }) in Program.cs.""");
    }

    private static SetupCheck CheckUiWiring(IServiceProvider services)
    {
        var state = services.GetRequiredService<QueryBuilderSetupState>();
        if (!state.UiMiddlewareRegistered)
        {
            return new SetupCheck("UI middleware registered", false, "app.UseQueryBuilderEditorUI() was never called for this request pipeline.",
                "Call app.UseQueryBuilderEditorUI() in Program.cs, before app.MapQueryBuilderEditor(). Skip this check if you're intentionally running headless (API only).");
        }

        try
        {
            var fileProvider = new ManifestEmbeddedFileProvider(typeof(ApplicationBuilderExtensions).Assembly, "wwwroot");
            var indexFile = fileProvider.GetFileInfo("index.html");
            if (!indexFile.Exists)
            {
                return new SetupCheck("UI middleware registered", false, "index.html was not found in the embedded SPA assets.",
                    "Rebuild the package with the client app's production output present under QueryBuilder.Editor/wwwroot.");
            }

            using var stream = indexFile.CreateReadStream();
            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();
            var hasScript = html.Contains("<script", StringComparison.OrdinalIgnoreCase);
            var hasStylesheet = html.Contains("stylesheet", StringComparison.OrdinalIgnoreCase);

            return hasScript && hasStylesheet
                ? new SetupCheck("UI middleware registered", true, "The embedded SPA is wired up and its assets are present.")
                : new SetupCheck("UI middleware registered", false, "index.html is present but doesn't reference a script and stylesheet as expected.",
                    "Rebuild the frontend (-p:BuildClientApp=true) and confirm client/dist looks correct before repackaging.");
        }
        catch (Exception ex)
        {
            return new SetupCheck("UI middleware registered", false, $"Could not read embedded SPA assets: {ex.Message}", null);
        }
    }

    private static async Task<SetupCheck> CheckAuthorizationPipelineAsync(IServiceProvider services, QueryBuilderEditorOptions options)
    {
        var auth = options.Authorization;
        var needsAuth = !string.IsNullOrWhiteSpace(auth.PolicyName) || auth.Mode != QueryBuilderAuthorizationMode.Anonymous;

        if (!needsAuth)
        {
            return new SetupCheck("Authorization pipeline", true, "Authorization.Mode is Anonymous — no authentication middleware required.");
        }

        var schemeProvider = services.GetService<IAuthenticationSchemeProvider>();
        var schemeCount = schemeProvider is null ? 0 : (await schemeProvider.GetAllSchemesAsync()).Count();

        return schemeCount > 0
            ? new SetupCheck("Authorization pipeline", true, $"Authorization requires access control and {schemeCount} authentication scheme(s) are registered.")
            : new SetupCheck("Authorization pipeline", false,
                "Authorization.Mode/PolicyName requires restricting access, but no authentication scheme is registered.",
                "Register an authentication scheme (e.g. builder.Services.AddAuthentication().AddCookie()/.AddJwtBearer()) and call app.UseAuthentication() before app.UseAuthorization().");
    }
}
