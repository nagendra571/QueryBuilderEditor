using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class HubStartupOrderTests
{
    private sealed class Logs : ILoggerProvider
    {
        public List<(string Category, LogLevel Level, string Text)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }

        private sealed class Logger(Logs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Entries)
                {
                    owner.Entries.Add((category, logLevel, formatter(state, exception) + (exception?.ToString() ?? "")));
                }
            }
        }
    }

    // The host registers authentication before the package does, as most real hosts do.
    private static IHost HubHost(string connectionString, bool applyMigrations, Logs logs) =>
        new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging(l => l.AddProvider(logs));
                services.AddAuthentication();
                services.AddQueryBuilderEditor(o =>
                {
                    o.ConnectionString = connectionString;
                    o.ApplyMigrations = applyMigrations;
                    o.Hub.Enabled = true;
                    o.Hub.Audience = "https://hub.test";
                    o.Hub.SuperAdminSignInScheme = "host";
                    o.Hub.SuperAdmins.ObjectIds = ["admin-oid"];
                    o.Hub.SuperAdmins.TenantIds = ["tenant-1"];
                    o.DataSourceManagement.EncryptionKey = HubApiHost.Key;
                });
            });
            web.Configure(app => { });
        }).Build();

    [MetadataSqlFact]
    public Task A_fresh_hub_database_starts_without_data_protection_errors() => MetadataDb.WithDatabaseAsync(async (_, cs) =>
    {
        var logs = new Logs();
        using (var host = HubHost(cs, applyMigrations: true, logs))
        {
            await host.StartAsync();
            await host.StopAsync();
        }

        lock (logs.Entries)
        {
            Assert.DoesNotContain(logs.Entries, e => e.Category.StartsWith("Microsoft.AspNetCore.DataProtection", StringComparison.Ordinal) && e.Level >= LogLevel.Error);
            Assert.DoesNotContain(logs.Entries, e => e.Text.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase));
        }
    }, migrate: false);

    [MetadataSqlFact]
    public Task A_database_owned_by_embedded_mode_is_refused_before_data_protection_writes_a_key() => MetadataDb.WithDatabaseAsync(async (options, cs) =>
    {
        await using (var db = new AppDbContext(options))
        {
            db.MetadataSettings.Add(new MetadataSetting { Key = "Mode", Value = "Embedded" });
            await db.SaveChangesAsync();
        }

        using (var host = HubHost(cs, applyMigrations: true, new Logs()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        }

        await using var check = new AppDbContext(options);
        Assert.Empty(await check.DataProtectionKeys.ToListAsync());
    });

    [MetadataSqlFact]
    public Task Saving_a_query_without_a_definition_is_a_400_in_embedded_mode() => HubApiHost.RunAsync(async env =>
    {
        var browser = env.NewBrowser();
        await AssertBadRequestsAsync(browser);
    }, hub: false);

    [MetadataSqlFact]
    public Task Saving_a_query_without_a_definition_is_a_400_in_hub_mode() => HubApiHost.RunAsync(async env =>
    {
        var browser = env.NewBrowser();
        await browser.SignInAppAsync(env.AppA, HubRole.Author);
        await AssertBadRequestsAsync(browser);
    });

    private static async Task AssertBadRequestsAsync(HubApiHost.Browser browser)
    {
        var ds = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/querybuilder/api/queries", new { name = "x", dataSourceId = ds })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/querybuilder/api/queries", new { name = "x", dataSourceId = ds, definition = (object?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/querybuilder/api/queries", new { name = "x", dataSourceId = ds, definition = new { source = (object?)null, columns = new[] { "a" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/querybuilder/api/queries", new { name = "x", dataSourceId = ds, definition = new { } })).StatusCode);
    }
}
