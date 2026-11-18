using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Tests.Postgres;

namespace QueryBuilder.Tests.Drill;

/// <summary>A throwaway PostgreSQL database with the same 60 enrolments as the SQL Server one (quoted mixed-case names, so the
/// saved definitions are identical). PostgreSQL keeps microseconds, so the seeded fractions of a second have six digits.</summary>
public sealed class EnrolmentPostgresDatabase : IAsyncLifetime
{
    private readonly string? _admin =
        Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvironmentVariable) is { Length: > 0 } v ? v : null;

    public string DatabaseName { get; } = $"qb_drill_it_{Guid.NewGuid():N}";
    public string ConnectionString { get; private set; } = string.Empty;
    internal List<EnrolmentRow> Seed { get; } = EnrolmentSeed.Rows(1234560);

    public async Task InitializeAsync()
    {
        if (_admin is null) return;

        var server = new NpgsqlConnectionStringBuilder(_admin) { Pooling = false };
        await using (var connection = new NpgsqlConnection(server.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = DatabaseName }.ConnectionString;
        await using var db = new NpgsqlConnection(ConnectionString);
        await db.OpenAsync();

        await using (var table = new NpgsqlCommand("""
            CREATE TABLE public."Enrolments"(
                "Id" int NOT NULL PRIMARY KEY, "ProgramId" int NOT NULL, "Status" varchar(20) NULL, "Site" varchar(20) NOT NULL,
                "Amount" numeric(10,2) NOT NULL, "Score" double precision NOT NULL, "EnrolledOn" timestamp NOT NULL,
                "SeenAt" timestamptz NOT NULL, "Note" varchar(50) NOT NULL)
            """, db))
        {
            await table.ExecuteNonQueryAsync();
        }

        foreach (var row in Seed)
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO public.\"Enrolments\" VALUES (@id, @program, @status, @site, @amount, @score, @enrolled, @seen, @note)", db);
            insert.Parameters.AddWithValue("id", row.Id);
            insert.Parameters.AddWithValue("program", row.ProgramId);
            insert.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Varchar) { Value = (object?)row.Status ?? DBNull.Value });
            insert.Parameters.AddWithValue("site", row.Site);
            insert.Parameters.Add(new NpgsqlParameter("amount", NpgsqlDbType.Numeric) { Value = row.Amount });
            insert.Parameters.Add(new NpgsqlParameter("score", NpgsqlDbType.Double) { Value = row.Score });
            insert.Parameters.Add(new NpgsqlParameter("enrolled", NpgsqlDbType.Timestamp) { Value = DateTime.SpecifyKind(row.EnrolledOn, DateTimeKind.Unspecified) });
            insert.Parameters.Add(new NpgsqlParameter("seen", NpgsqlDbType.TimestampTz) { Value = row.SeenAt.UtcDateTime });
            insert.Parameters.AddWithValue("note", row.Note);
            await insert.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_admin is null) return;

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_admin) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

/// <summary>The same guarantee as <see cref="DrillScopeSqlServerTests"/> on PostgreSQL, whose text-mode reads, bigint binding of
/// whole numbers and timestamptz handling are all different code. Skipped unless QB_TEST_POSTGRES is set.</summary>
public class DrillScopePostgresTests(EnrolmentPostgresDatabase db) : IClassFixture<EnrolmentPostgresDatabase>
{
    private sealed class FixedConnection(string connectionString) : IConnectionStringResolver
    {
        public string Resolve(DataSource dataSource) => connectionString;
    }

    private DrillHarness Harness(Persona persona) =>
        new(DataSourceProvider.PostgreSql, "public", "Enrolments",
            new PostgresQueryExecutionService(new FixedConnection(db.ConnectionString), NullLogger<PostgresQueryExecutionService>.Instance), persona);

    [PostgresFact]
    public async Task Unrestricted_user_matrix() => Assert.True(await DrillScopeChecks.RunMatrixAsync(Harness(Persona.Unrestricted), db.Seed) > 0);

    [PostgresFact]
    public async Task Program_10_user_matrix() => Assert.True(await DrillScopeChecks.RunMatrixAsync(Harness(Persona.P10), db.Seed) > 0);

    [PostgresFact]
    public async Task Program_10_and_30_user_matrix() => Assert.True(await DrillScopeChecks.RunMatrixAsync(Harness(Persona.P10And30), db.Seed) > 0);

    [PostgresFact]
    public async Task A_forged_text_value_matches_nothing_and_cannot_widen_the_zone() =>
        await DrillScopeChecks.ForgedTextValueMatchesNothingAsync(Harness(Persona.P10));

    [PostgresFact]
    public async Task Null_step_returns_exactly_the_rows_without_a_value()
    {
        await DrillScopeChecks.NullStepIsExactAsync(Harness(Persona.Unrestricted), db.Seed);
        await DrillScopeChecks.NullStepIsExactAsync(Harness(Persona.P10), db.Seed);
    }

    [PostgresFact]
    public Task Every_status_group_including_null_and_empty_drills_to_its_own_count() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "Status", ColumnDataType.Text, minimumGroups: 4);

    [PostgresFact]
    public Task Timestamp_value_from_chart_data_round_trips_to_the_same_rows() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "EnrolledOn", ColumnDataType.DateTime, minimumGroups: 5);

    [PostgresFact]
    public Task Timestamp_value_from_chart_data_round_trips_inside_a_zone() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.P10), "EnrolledOn", ColumnDataType.DateTime, minimumGroups: 3);

    [PostgresFact]
    public Task Timestamptz_value_from_chart_data_round_trips_to_the_same_rows() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "SeenAt", ColumnDataType.DateTime, minimumGroups: 4);

    [PostgresFact]
    public Task Float_step_matches_its_row() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "Score", ColumnDataType.Number, minimumGroups: 6);

    [PostgresFact]
    public Task Decimal_step_matches_its_row() =>
        DrillScopeChecks.ChartValueRoundTripsAsync(Harness(Persona.Unrestricted), "Amount", ColumnDataType.Number, minimumGroups: 7);
}
