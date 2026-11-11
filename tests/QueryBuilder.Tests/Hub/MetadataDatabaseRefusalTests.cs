using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Catalog;
using QueryBuilder.Infrastructure.Hub;
using QueryBuilder.Infrastructure.Persistence;
using System.Net;
using QueryBuilder.Domain.Model;
using QueryBuilder.Application.Common;
using Xunit;

namespace QueryBuilder.Tests.Hub;

/// <summary>Hub Part 3, task 7: the app's record ceiling and refusing a data source that points at the hub's own database.</summary>
[Collection(MetadataDatabaseCollection.Name)]
public class MetadataDatabaseRefusalTests
{
    private const string Refusal = "A data source can't point at QueryBuilder's own database.";

    private static ConnectionInput Input() => new("h", 1433, "d", "u", "p", null);

    [MetadataSqlFact]
    public async Task Data_source_pointing_at_the_metadata_db_is_refused()
    {
        await MetadataDb.WithDatabaseAsync(async (_, connectionString) =>
        {
            var identity = new MetadataDatabaseIdentity(new MetadataConnectionStringAccessor(connectionString), new MetadataMode(true));
            var builder = new SqlConnectionStringBuilder(connectionString);

            // The same database reached under another spelling of the server and another case of its name.
            var alias = new SqlConnectionStringBuilder(connectionString)
            {
                DataSource = builder.DataSource.Replace("(localdb)", "(LOCALDB)", StringComparison.OrdinalIgnoreCase).Replace("MSSQLLocalDB", "MSSQLLOCALDB"),
                InitialCatalog = builder.InitialCatalog.ToUpperInvariant()
            }.ConnectionString;

            Assert.True(await identity.IsSameAsync(DataSourceProvider.SqlServer, connectionString, default));
            Assert.True(await identity.IsSameAsync(DataSourceProvider.SqlServer, alias, default));

            await using var open = new SqlConnection(alias);
            await open.OpenAsync();
            Assert.True(await identity.IsSameAsync(open, default));
            Assert.False(await identity.IsSameAsync(DataSourceProvider.PostgreSql, connectionString, default));
        });
    }

    [MetadataSqlFact]
    public async Task Other_database_on_same_server_is_allowed()
    {
        await MetadataDb.WithDatabaseAsync(async (_, connectionString) =>
        {
            var identity = new MetadataDatabaseIdentity(new MetadataConnectionStringAccessor(connectionString), new MetadataMode(true));
            var other = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;

            Assert.False(await identity.IsSameAsync(DataSourceProvider.SqlServer, other, default));
        });
    }

    [MetadataSqlFact]
    public async Task Embedded_mode_never_refuses()
    {
        await MetadataDb.WithDatabaseAsync(async (_, connectionString) =>
        {
            var identity = new MetadataDatabaseIdentity(new MetadataConnectionStringAccessor(connectionString), new MetadataMode(false));

            Assert.False(await identity.IsSameAsync(DataSourceProvider.SqlServer, connectionString, default));
        });
    }

    [Fact]
    public async Task Hub_handlers_refuse_when_identity_matches()
    {
        var rig = new DataSourceManagementTests.Rig();
        var existing = rig.AddManaged("Existing", DataSourceProvider.SqlServer);
        var same = new FakeMetadataIdentity(same: true);
        var user = new DataSourceManagementTests.User();
        var apps = new DataSourceManagementTests.NoApp();

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            new CreateDataSourceCommandHandler(rig.Repo, rig.Protector, rig.Settings, user, apps, rig.Audit, same)
                .Handle(new CreateDataSourceCommand("New", null, DataSourceProvider.SqlServer, Input()), default));
        Assert.Equal(Refusal, ex.Message);

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            new TestConnectionCommandHandler(rig.Repo, rig.Protector, rig.Tester, rig.Settings, user, apps, same)
                .Handle(new TestConnectionCommand(DataSourceProvider.SqlServer, Input(), null), default));

        var before = existing.EncryptedConnection;
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            new UpdateDataSourceCommandHandler(rig.Repo, rig.Protector, rig.Catalog, rig.Pools, rig.Settings, user, apps, rig.Audit, same)
                .Handle(new UpdateDataSourceCommand(existing.Id, "Renamed", null, Input()), default));

        Assert.Null(rig.Tester.Tested);
        Assert.Equal(before, existing.EncryptedConnection);
        Assert.Single(rig.Repo.Items);
    }

    [Fact]
    public async Task Handlers_go_on_when_identity_differs()
    {
        var rig = new DataSourceManagementTests.Rig();

        var result = await new TestConnectionCommandHandler(rig.Repo, rig.Protector, rig.Tester, rig.Settings,
            new DataSourceManagementTests.User(), new DataSourceManagementTests.NoApp(), new FakeMetadataIdentity())
            .Handle(new TestConnectionCommand(DataSourceProvider.SqlServer, Input(), null), default);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Catalog_load_refuses_a_source_that_is_the_metadata_database()
    {
        var service = new DataCatalogService(new ActiveDataSources(), new FixedResolver(), [], new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DataCatalogService>.Instance, new FakeMetadataIdentity(same: true));

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => service.GetCatalogAsync(TestData.DataSourceId, default));
        Assert.Equal(Refusal, ex.Message);
        await Assert.ThrowsAsync<CatalogValidationException>(() => service.GetRawObjectsAsync(TestData.DataSourceId, default));
    }

    [MetadataSqlFact]
    public async Task App_ceiling_is_read_from_the_apps_row_and_embedded_has_none()
    {
        await HubApiHost.RunAsync(async env =>
        {
            await using (var db = env.HubWideDb())
            {
                var app = await db.Apps.FirstAsync(a => a.Id == env.AppA);
                app.MaxRecords = 5;
                await db.SaveChangesAsync();
            }

            using var scope = env.Host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IAppContextScope>().UseApp(env.AppA);
            Assert.Equal(5, await scope.ServiceProvider.GetRequiredService<ICurrentAppSettings>().MaxRecordsAsync(default));

            using var other = env.Host.Services.CreateScope();
            other.ServiceProvider.GetRequiredService<IAppContextScope>().UseApp(env.AppB);
            Assert.Null(await other.ServiceProvider.GetRequiredService<ICurrentAppSettings>().MaxRecordsAsync(default));
        });

        await HubApiHost.RunAsync(async env =>
        {
            using var scope = env.Host.Services.CreateScope();
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ICurrentAppSettings>().MaxRecordsAsync(default));
        }, hub: false);
    }

    [Fact]
    public async Task Failing_to_read_the_hubs_own_identity_fails_closed_without_driver_text()
    {
        var identity = new MetadataDatabaseIdentity(
            new MetadataConnectionStringAccessor("Server=tcp:127.0.0.1,1;Database=secretdb;User Id=hubuser;Password=x;Connect Timeout=1;Encrypt=false"),
            new MetadataMode(true));

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            identity.IsSameAsync(DataSourceProvider.SqlServer, "Server=other;Database=d", default));

        Assert.Equal("Couldn't check the connection.", ex.Message);
    }

    private const string Api = "/querybuilder/api";

    [MetadataSqlFact]
    public Task Real_identity_refuses_a_stored_source_that_points_at_the_metadata_db() => HubApiHost.RunAsync(async env =>
    {
        var source = new DataSource { AppId = env.AppA, Name = "own-db", ConnectionStringName = "biz" };
        await using (var db = env.HubWideDb())
        {
            db.DataSources.Add(source);
            await db.SaveChangesAsync();
        }

        var super = env.NewBrowser();
        await super.SignInSuperAsync(env.AppA);
        var response = await super.PostAsync($"{Api}/admin/data-sources/{source.Id}/test", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(Refusal, await response.Content.ReadAsStringAsync());
    }, realIdentity: true);

    [MetadataSqlFact]
    public Task App_ceiling_caps_run_export_and_render() => HubApiHost.RunAsync(async env =>
    {
        var sourceId = Guid.NewGuid();
        var queryId = Guid.NewGuid();
        var vizId = Guid.NewGuid();
        var definition = new QueryDefinition
        {
            Source = new QuerySource { SchemaName = "dbo", ObjectName = "ceil10", Kind = SchemaObjectKind.View, Alias = "t" },
            Columns =
            [
                new QueryColumn { TableAlias = "t", ColumnName = "label", OrderIndex = 0, DataType = ColumnDataType.Text },
                new QueryColumn { TableAlias = "t", ColumnName = "n", OrderIndex = 1, DataType = ColumnDataType.Number }
            ]
        };

        await using (var db = env.HubWideDb())
        {
            await db.Database.ExecuteSqlRawAsync("CREATE VIEW dbo.ceil10 AS SELECT CAST(v AS nvarchar(5)) AS label, v AS n FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) AS x(v)");
            db.DataSources.Add(new DataSource { Id = sourceId, AppId = env.AppA, Name = "src", ConnectionStringName = "biz", AllowedObjects = ["dbo.ceil10"], MaxRecords = 50 });
            await db.SaveChangesAsync();
            db.SavedQueries.Add(new SavedQuery { Id = queryId, AppId = env.AppA, Name = "q", DataSourceId = sourceId, OwnerId = "boss", OwnerName = "Boss", DefinitionJson = QueryDefinitionSerializer.Serialize(definition) });
            await db.SaveChangesAsync();
            db.QueryVisualizations.Add(new QueryVisualization
            {
                Id = vizId, AppId = env.AppA, SavedQueryId = queryId, Name = "bar", Type = VisualizationType.Bar,
                OptionsJson = VisualizationOptionsSerializer.Serialize(new VisualizationOptions { X = "label", Measures = ["n"] })
            });
            await db.SaveChangesAsync();
        }

        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "boss");
        object body(string? format = null) => new { dataSourceId = sourceId, definition, parameterValues = new { }, maxRows = 1000, format, fileName = "x" };

        // With only the source's own limit (50) all ten rows come back.
        var open = await HubApiHost.Browser.JsonAsync(await admin.PostAsync($"{Api}/queries/run", body()));
        Assert.Equal(10, open.GetProperty("rows").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Api}/queries/export", body("csv"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Api}/queries/{queryId}/visualizations/{vizId}/data", new { parameterValues = new { } })).StatusCode);

        await using (var db = env.HubWideDb())
        {
            var app = await db.Apps.FirstAsync(a => a.Id == env.AppA);
            app.MaxRecords = 5;
            await db.SaveChangesAsync();
        }

        // The ceiling is cached for up to 30 s, so a new host (fresh cache) sees it at once.
        await env.RestartAsync(null);
        admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin, "boss");

        var capped = await HubApiHost.Browser.JsonAsync(await admin.PostAsync($"{Api}/queries/run", body()));
        Assert.Equal(5, capped.GetProperty("rows").GetArrayLength());
        Assert.True(capped.GetProperty("truncated").GetBoolean());
        Assert.Equal(5, capped.GetProperty("rowLimit").GetInt32());

        var export = await admin.PostAsync($"{Api}/queries/export", body("csv"));
        Assert.Equal(HttpStatusCode.Conflict, export.StatusCode);
        Assert.DoesNotContain("data source", await export.Content.ReadAsStringAsync());

        var render = await admin.PostAsync($"{Api}/queries/{queryId}/visualizations/{vizId}/data", new { parameterValues = new { } });
        Assert.Equal(HttpStatusCode.Conflict, render.StatusCode);
        Assert.Contains("more than 5 records", await render.Content.ReadAsStringAsync());
    });

    private sealed class FixedResolver : IConnectionStringResolver
    {
        public string Resolve(DataSource dataSource) => "Server=x;Database=y";
    }
}
