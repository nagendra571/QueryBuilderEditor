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

    private sealed class FixedResolver : IConnectionStringResolver
    {
        public string Resolve(DataSource dataSource) => "Server=x;Database=y";
    }
}
