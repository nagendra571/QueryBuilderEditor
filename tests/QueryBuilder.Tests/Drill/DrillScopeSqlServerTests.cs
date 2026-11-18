using System.Data;
using Microsoft.Data.SqlClient;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Tests.Hub;

namespace QueryBuilder.Tests.Drill;

/// <summary>A throwaway business database with 60 enrolments, created and dropped around the tests of one class.</summary>
public sealed class EnrolmentSqlServerDatabase : IAsyncLifetime
{
    private readonly string? _server = Environment.GetEnvironmentVariable(MetadataSqlFactAttribute.EnvironmentVariable) is { Length: > 0 } v ? v : null;

    public string DatabaseName { get; } = $"qb_drill_it_{Guid.NewGuid():N}";
    public string ConnectionString { get; private set; } = string.Empty;
    internal List<EnrolmentRow> Seed { get; } = EnrolmentSeed.Rows(1234567);

    public async Task InitializeAsync()
    {
        if (_server is null) return;

        await using (var master = new SqlConnection(new SqlConnectionStringBuilder(_server) { InitialCatalog = "master", Pooling = false }.ConnectionString))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{DatabaseName}]";
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new SqlConnectionStringBuilder(_server) { InitialCatalog = DatabaseName }.ConnectionString;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using (var table = connection.CreateCommand())
        {
            table.CommandText = """
                CREATE TABLE dbo.Enrolments(
                    Id int NOT NULL PRIMARY KEY, ProgramId int NOT NULL, Status nvarchar(20) NULL, Site nvarchar(20) NOT NULL,
                    Amount decimal(10,2) NOT NULL, Score float NOT NULL, EnrolledOn datetime2 NOT NULL,
                    SeenAt datetimeoffset NOT NULL, Note nvarchar(50) NOT NULL)
                """;
            await table.ExecuteNonQueryAsync();
        }

        foreach (var row in Seed)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT dbo.Enrolments VALUES (@id, @program, @status, @site, @amount, @score, @enrolled, @seen, @note)";
            insert.Parameters.Add(new SqlParameter("@id", row.Id));
            insert.Parameters.Add(new SqlParameter("@program", row.ProgramId));
            insert.Parameters.Add(new SqlParameter("@status", (object?)row.Status ?? DBNull.Value));
            insert.Parameters.Add(new SqlParameter("@site", row.Site));
            insert.Parameters.Add(new SqlParameter("@amount", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = row.Amount });
            insert.Parameters.Add(new SqlParameter("@score", row.Score));
            insert.Parameters.Add(new SqlParameter("@enrolled", SqlDbType.DateTime2) { Value = row.EnrolledOn });
            insert.Parameters.Add(new SqlParameter("@seen", SqlDbType.DateTimeOffset) { Value = row.SeenAt });
            insert.Parameters.Add(new SqlParameter("@note", row.Note));
            await insert.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_server is null) return;

        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(_server) { InitialCatalog = "master", Pooling = false }.ConnectionString);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END";
        await drop.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// The user's top requirement against a real SQL Server: a user zoned to ProgramId 10 sees only ProgramId 10 rows at every
/// drill level, through every path, even when the click trail is forged.
/// </summary>
[Collection(MetadataDatabaseCollection.Name)]
public class DrillScopeSqlServerTests(EnrolmentSqlServerDatabase db) : IClassFixture<EnrolmentSqlServerDatabase>
{
    private sealed class FixedConnection(string connectionString) : IConnectionStringResolver
    {
        public string Resolve(DataSource dataSource) => connectionString;
    }

    private DrillHarness Harness(Persona persona) =>
        new(DataSourceProvider.SqlServer, "dbo", "Enrolments", new SqlServerQueryExecutionService(new FixedConnection(db.ConnectionString)), persona);

    [MetadataSqlFact]
    public async Task Unrestricted_user_matrix() => Assert.True(await DrillScopeChecks.RunMatrixAsync(Harness(Persona.Unrestricted), db.Seed) > 0);

    [MetadataSqlFact]
    public async Task Program_10_user_matrix() => Assert.True(await DrillScopeChecks.RunMatrixAsync(Harness(Persona.P10), db.Seed) > 0);

    [MetadataSqlFact]
    public async Task Program_10_and_30_user_matrix() => Assert.True(await DrillScopeChecks.RunMatrixAsync(Harness(Persona.P10And30), db.Seed) > 0);

    [MetadataSqlFact]
    public async Task A_forged_text_value_matches_nothing_and_cannot_widen_the_zone() =>
        await DrillScopeChecks.ForgedTextValueMatchesNothingAsync(Harness(Persona.P10));

    [MetadataSqlFact]
    public async Task Null_step_returns_exactly_the_rows_without_a_value()
    {
        await DrillScopeChecks.NullStepIsExactAsync(Harness(Persona.Unrestricted), db.Seed);
        await DrillScopeChecks.NullStepIsExactAsync(Harness(Persona.P10), db.Seed);
    }

    [MetadataSqlFact]
    public Task Every_status_group_including_null_and_empty_drills_to_its_own_count() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "Status", ColumnDataType.Text, minimumGroups: 4);

    [MetadataSqlFact]
    public Task Datetime_value_from_chart_data_round_trips_to_the_same_rows() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "EnrolledOn", ColumnDataType.DateTime, minimumGroups: 5);

    [MetadataSqlFact]
    public Task Datetime_value_from_chart_data_round_trips_inside_a_zone() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.P10), "EnrolledOn", ColumnDataType.DateTime, minimumGroups: 3);

    [MetadataSqlFact]
    public Task Datetimeoffset_value_from_chart_data_round_trips_to_the_same_rows() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "SeenAt", ColumnDataType.DateTime, minimumGroups: 4);

    [MetadataSqlFact]
    public Task Float_step_matches_its_row() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "Score", ColumnDataType.Number, minimumGroups: 6);

    [MetadataSqlFact]
    public Task Decimal_step_matches_its_row() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "Amount", ColumnDataType.Number, minimumGroups: 7);
}
