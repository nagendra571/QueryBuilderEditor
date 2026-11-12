using Microsoft.Data.SqlClient;
using QueryBuilder.Domain.Entities;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class HubRolesSchemaTests
{
    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    [MetadataSqlFact]
    public Task New_columns_exist_with_defaults() => MetadataDb.WithDatabaseAsync(async (_, cs) =>
    {
        var parents = new Dictionary<string, Guid>();
        var dataSource = await AddAppsMigrationTests.InsertSampleRowAsync(cs, "DataSources", parents);
        var dashboard = await AddAppsMigrationTests.InsertSampleRowAsync(cs, "Dashboards", parents);
        var app = await AddAppsMigrationTests.InsertSampleRowAsync(cs, "Apps", parents);

        Assert.False((bool)(await ScalarAsync(cs, $"SELECT LockedBySuperAdmin FROM DataSources WHERE Id = '{dataSource}'"))!);
        Assert.False((bool)(await ScalarAsync(cs, $"SELECT PublishedToApp FROM Dashboards WHERE Id = '{dashboard}'"))!);
        Assert.Null(await ScalarAsync(cs, $"SELECT PublishedBy FROM Dashboards WHERE Id = '{dashboard}'"));
        Assert.Null(await ScalarAsync(cs, $"SELECT PublishedAtUtc FROM Dashboards WHERE Id = '{dashboard}'"));
        Assert.Null(await ScalarAsync(cs, $"SELECT MaxRecords FROM Apps WHERE Id = '{app}'"));
        Assert.False((bool)(await ScalarAsync(cs, $"SELECT AppAdminsCanManageConnections FROM Apps WHERE Id = '{app}'"))!);
        Assert.Equal(1, (int)(await ScalarAsync(cs, $"SELECT Version FROM Apps WHERE Id = '{app}'"))!);
        Assert.Equal(1, (int)(await ScalarAsync(cs, $"SELECT Version FROM Apps WHERE Id = '{App.DefaultId}'"))!);
    });
}
