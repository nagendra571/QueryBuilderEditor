using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class AddAppsMigrationTests
{
    private const string PreviousMigration = "20261110023605_AddDataSourceScopeKeys";

    // Parents before children, so foreign keys can point at rows inserted earlier.
    private static readonly string[] AppOwnedTables =
    [
        "DataSources", "SavedQueries", "QueryShares", "QueryVisualizations", "DataScopeRules",
        "Dashboards", "DashboardTiles", "DashboardShares", "AuditLogEntries",
    ];

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    [MetadataSqlFact]
    public Task Fresh_database_has_only_the_default_app() => MetadataDb.WithDatabaseAsync(async (_, connectionString) =>
    {
        Assert.Equal(1, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM Apps"));
        Assert.Equal(1, await ScalarAsync(connectionString,
            $"SELECT COUNT(*) FROM Apps WHERE Id = '{App.DefaultId}' AND [Key] = 'default' AND IsEnabled = 1"));
    });

    [MetadataSqlFact]
    public Task Upgrade_puts_existing_rows_in_the_default_app() => MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
    {
        await using (var db = new AppDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        Assert.Equal(0, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM sys.tables WHERE name = 'Apps'"));

        // Rows are inserted by discovering each table's required columns, so the test doesn't hard-code the old schema.
        var inserted = new Dictionary<string, Guid>();
        foreach (var table in AppOwnedTables)
        {
            inserted[table] = await InsertSampleRowAsync(connectionString, table, inserted, includeAppId: false);
        }

        var before = new Dictionary<string, long>();
        foreach (var table in AppOwnedTables)
        {
            before[table] = await ScalarAsync(connectionString, $"SELECT COUNT(*) FROM [{table}]");
            Assert.Equal(1, before[table]);
        }

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        foreach (var table in AppOwnedTables)
        {
            Assert.Equal(before[table], await ScalarAsync(connectionString, $"SELECT COUNT(*) FROM [{table}]"));
            Assert.Equal(before[table], await ScalarAsync(connectionString, $"SELECT COUNT(*) FROM [{table}] WHERE AppId = '{App.DefaultId}'"));
        }
    }, migrate: false);

    [MetadataSqlFact]
    public Task A_raw_insert_without_an_app_lands_in_default() => MetadataDb.WithDatabaseAsync(async (_, connectionString) =>
    {
        var inserted = new Dictionary<string, Guid>();
        foreach (var table in AppOwnedTables)
        {
            inserted[table] = await InsertSampleRowAsync(connectionString, table, inserted, includeAppId: false);
            Assert.Equal(1, await ScalarAsync(connectionString,
                $"SELECT COUNT(*) FROM [{table}] WHERE Id = '{inserted[table]}' AND AppId = '{App.DefaultId}'"));
        }
    });

    [MetadataSqlFact]
    public Task Every_app_id_column_has_a_named_default_of_the_default_app() => MetadataDb.WithDatabaseAsync(async (_, connectionString) =>
    {
        foreach (var table in AppOwnedTables)
        {
            Assert.Equal(1, await ScalarAsync(connectionString,
                $@"SELECT COUNT(*) FROM sys.default_constraints d
JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
WHERE d.parent_object_id = OBJECT_ID('{table}') AND c.name = 'AppId' AND d.name = 'DF_{table}_AppId'
  AND d.definition LIKE '%00000000-0000-0000-0000-00000000000d%'"));
        }
    });

    [MetadataSqlFact]
    public Task Down_removes_apps_and_every_app_id_column() => MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
    {
        await using (var db = new AppDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        Assert.Equal(0, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM sys.tables WHERE name IN ('Apps','MetadataSettings')"));
        Assert.Equal(0, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM sys.columns WHERE name = 'AppId'"));
    });

    internal static async Task<Guid> InsertSampleRowAsync(string connectionString, string table, Dictionary<string, Guid> parents, bool includeAppId = true)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var foreignKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var fk = connection.CreateCommand())
        {
            fk.CommandText = @"SELECT pc.name, rt.name FROM sys.foreign_key_columns k
JOIN sys.columns pc ON pc.object_id = k.parent_object_id AND pc.column_id = k.parent_column_id
JOIN sys.tables rt ON rt.object_id = k.referenced_object_id
WHERE k.parent_object_id = OBJECT_ID(@t)";
            fk.Parameters.AddWithValue("@t", table);
            await using var reader = await fk.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                foreignKeys[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var columns = new List<string>();
        var values = new List<string>();
        var id = Guid.NewGuid();
        await using (var cols = connection.CreateCommand())
        {
            cols.CommandText = @"SELECT c.name, ty.name, c.is_nullable, c.is_identity, c.is_computed, c.default_object_id
FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(@t)";
            cols.Parameters.AddWithValue("@t", table);
            await using var reader = await cols.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                var type = reader.GetString(1);
                var nullable = reader.GetBoolean(2);
                if (reader.GetBoolean(3) || reader.GetBoolean(4) || type == "timestamp" || type == "rowversion")
                {
                    continue;
                }

                if (name == "AppId")
                {
                    if (includeAppId && !nullable)
                    {
                        columns.Add("[AppId]");
                        values.Add($"'{App.DefaultId}'");
                    }

                    continue;
                }

                if (name == "Id")
                {
                    columns.Add("[Id]");
                    values.Add($"'{id}'");
                    continue;
                }

                if (foreignKeys.TryGetValue(name, out var parentTable))
                {
                    if (parents.TryGetValue(parentTable, out var parentId))
                    {
                        columns.Add($"[{name}]");
                        values.Add($"'{parentId}'");
                    }
                    else if (!nullable)
                    {
                        throw new InvalidOperationException($"{table}.{name} needs a {parentTable} row.");
                    }

                    continue;
                }

                if (nullable || reader.GetInt32(5) != 0)
                {
                    continue;
                }

                columns.Add($"[{name}]");
                values.Add(type switch
                {
                    "uniqueidentifier" => $"'{Guid.NewGuid()}'",
                    "bit" or "int" or "smallint" or "tinyint" or "bigint" => "1",
                    "datetimeoffset" => "SYSDATETIMEOFFSET()",
                    "datetime2" or "datetime" => "SYSUTCDATETIME()",
                    _ => "N'x'",
                });
            }
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO [{table}] ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
        await insert.ExecuteNonQueryAsync();
        return id;
    }
}
