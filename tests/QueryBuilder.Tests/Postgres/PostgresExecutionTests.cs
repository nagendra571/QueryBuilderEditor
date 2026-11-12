using Xunit.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataScoping;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Catalog;
using QueryBuilder.Infrastructure.Execution;
using QueryBuilder.Infrastructure.Export;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Tests.Postgres;

public class PostgresExecutionTests(PostgresTestDatabase database, ITestOutputHelper output) : IClassFixture<PostgresTestDatabase>
{
    private static readonly Dictionary<string, string?> NoRuntime = [];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static DataSource Source() => new()
    {
        Id = TestData.DataSourceId, Name = "pg", Provider = DataSourceProvider.PostgreSql, CatalogScope = CatalogScope.TablesAndViews
    };

    private PostgresQueryExecutionService Service(string? connectionString = null, ILogger<PostgresQueryExecutionService>? logger = null) =>
        new(new Resolver(connectionString ?? database.ReaderConnectionString), logger ?? NullLogger<PostgresQueryExecutionService>.Instance);

    private Task<QueryResultSet> Raw(string sql, int? maxRows = null, CancellationToken ct = default) =>
        Service().ExecuteAsync(Source(), new GeneratedQuery(sql, []), NoRuntime, maxRows, ct);

    private async Task<long> AdminScalar(string sql)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task AdminExecute(string sql)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static QueryDefinition OpsDefinition(string column, FilterOperator op, string? value, ColumnDataType type)
    {
        var definition = new QueryDefinition
        {
            Source = new QuerySource { SchemaName = "ex", ObjectName = "ops", Alias = "t0", Kind = SchemaObjectKind.Table },
            Columns = [new QueryColumn { TableAlias = "t0", ColumnName = "id" }]
        };
        definition.Filters.Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = column, Operator = op, Value = value, DataType = type
        });
        return definition;
    }

    private async Task<long[]> Ids(QueryDefinition definition, IReadOnlyList<ScopePredicate>? scope = null)
    {
        var generated = new PostgresQuerySqlBuilder().Build(definition, scope ?? []);
        var result = await Service().ExecuteAsync(Source(), generated, NoRuntime, null, default);
        return result.Rows.Select(r => (long)r["id"]!).Order().ToArray();
    }

    private Task<long[]> Ids(string column, FilterOperator op, string? value, ColumnDataType type = ColumnDataType.Text) =>
        Ids(OpsDefinition(column, op, value, type));

    // ---- AC-5 -------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task AC5_view_whose_function_writes_is_refused_and_adds_no_row()
    {
        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() => Raw("SELECT * FROM ex.writer"));

        Assert.Equal("This query tried to change data, which isn't allowed.", ex.Message);
        Assert.Equal(0, await AdminScalar("SELECT count(*) FROM ex.counter"));
    }

    [PostgresFact]
    public async Task Other_database_errors_carry_the_server_message_without_the_sql_state()
    {
        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() => Raw("SELECT * FROM ex.no_such_table"));

        Assert.StartsWith("The database rejected the query: ", ex.Message);
        Assert.Contains("no_such_table", ex.Message);
        Assert.DoesNotContain("42P01", ex.Message);
    }

    // ---- AC-11 ------------------------------------------------------------------------------------------

    private const string EveryType = """
        SELECT 1::smallint AS c_int2, 2::int AS c_int4, 3::bigint AS c_int8, 4::oid AS c_oid,
               'NaN'::numeric AS n_nan, 'Infinity'::numeric AS n_inf, '-Infinity'::numeric AS n_ninf,
               1234567890123456789012345678901234567890::numeric AS n_big, 12.50::numeric AS n_trail,
               0.1234567890123456789012345678901::numeric AS n_fraction,
               'NaN'::float8 AS f_nan, 'Infinity'::float8 AS f_inf, '-Infinity'::float8 AS f_ninf, 1.5::float4 AS f_real,
               true AS b,
               '2024-03-05'::date AS d, 'infinity'::date AS d_inf, '-infinity'::date AS d_ninf,
               '0044-03-15 BC'::date AS d_bc, '12000-01-01'::date AS d_far,
               '2024-03-05 10:11:12.5'::timestamp AS ts, 'infinity'::timestamp AS ts_inf, '0044-03-15 10:00:00 BC'::timestamp AS ts_bc,
               '2024-03-05 10:11:12+02'::timestamptz AS tz, 'infinity'::timestamptz AS tz_inf,
               '33333333-3333-3333-3333-333333333333'::uuid AS u,
               'ab'::char(5) AS ch, '$12.50'::money AS m, '1 year 2 mons 3 days'::interval AS iv,
               '10:30:00'::time AS tm, '192.168.0.1/24'::inet AS ip, ARRAY[1,2,3] AS arr, '{"k":[1,2]}'::json AS js,
               'calm'::ex.feeling AS en, 'Mixed'::citext AS ci, NULL::int AS nothing
        """;

    [PostgresFact]
    public async Task AC11_every_type_reads_serializes_and_exports_to_csv_and_excel()
    {
        var result = await Raw(EveryType);
        var row = result.Rows.Single();

        Assert.Equal(1L, row["c_int2"]);
        Assert.Equal(4L, row["c_oid"]);
        Assert.Null(row["n_nan"]);
        Assert.Null(row["n_inf"]);
        Assert.Null(row["n_ninf"]);
        Assert.Equal("1234567890123456789012345678901234567890", row["n_big"]);
        Assert.Equal(12.50m, row["n_trail"]);
        Assert.Equal("0.1234567890123456789012345678901", row["n_fraction"]);
        Assert.Null(row["f_nan"]);
        Assert.Null(row["f_inf"]);
        Assert.Null(row["f_ninf"]);
        Assert.Equal(1.5, row["f_real"]);
        Assert.Equal(true, row["b"]);
        Assert.Equal(new DateTime(2024, 3, 5), row["d"]);
        Assert.Equal("infinity", row["d_inf"]);
        Assert.Equal("-infinity", row["d_ninf"]);
        Assert.Equal("0044-03-15 BC", row["d_bc"]);
        Assert.Equal("12000-01-01", row["d_far"]);
        Assert.Equal(new DateTime(2024, 3, 5, 10, 11, 12, 500), row["ts"]);
        Assert.Equal("infinity", row["ts_inf"]);
        Assert.Equal("0044-03-15 10:00:00 BC", row["ts_bc"]);
        var tz = Assert.IsType<DateTime>(row["tz"]);
        Assert.Equal(DateTimeKind.Utc, tz.Kind);
        Assert.Equal(new DateTime(2024, 3, 5, 8, 11, 12, DateTimeKind.Utc), tz);
        Assert.Equal("infinity", row["tz_inf"]);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), row["u"]);
        Assert.Equal("ab", row["ch"]);
        Assert.Contains("12", Assert.IsType<string>(row["m"]));
        Assert.Equal("1 year 2 mons 3 days", row["iv"]);
        Assert.Equal("10:30:00", row["tm"]);
        Assert.Equal("192.168.0.1/24", row["ip"]);
        Assert.Equal("{1,2,3}", row["arr"]);
        Assert.Equal("{\"k\":[1,2]}", row["js"]);
        Assert.Equal("calm", row["en"]);
        Assert.Equal("Mixed", row["ci"]);
        Assert.Null(row["nothing"]);

        var json = JsonSerializer.Serialize(result, Json);
        Assert.Contains("\"infinity\"", json);
        Assert.DoesNotContain("9999-12-31", json);

        var export = new ExportService();
        var csv = Encoding.UTF8.GetString(await export.ExportAsync(result, ExportFormat.Csv, "r", default));
        Assert.Contains("infinity", csv);
        Assert.DoesNotContain("9999-12-31", csv);

        var xlsx = await export.ExportAsync(result, ExportFormat.Xlsx, "r", default);
        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var sheet = workbook.Worksheet(1);
        Assert.Equal(result.Columns.Count, sheet.LastColumnUsed()!.ColumnNumber());
        Assert.DoesNotContain("9999-12-31", sheet.CellsUsed().Select(c => c.GetFormattedString()));
        Assert.Contains("infinity", sheet.CellsUsed().Select(c => c.GetString()));
    }

    [PostgresFact]
    public async Task Column_data_types_follow_the_reported_types()
    {
        var result = await Raw(EveryType);
        var types = result.Columns.ToDictionary(c => c.Name, c => c.DataType);

        Assert.Equal(ColumnDataType.Number, types["c_int8"]);
        Assert.Equal(ColumnDataType.Number, types["n_big"]);
        Assert.Equal(ColumnDataType.Number, types["f_real"]);
        Assert.Equal(ColumnDataType.Boolean, types["b"]);
        Assert.Equal(ColumnDataType.Date, types["d"]);
        Assert.Equal(ColumnDataType.DateTime, types["ts"]);
        Assert.Equal(ColumnDataType.DateTime, types["tz"]);
        Assert.Equal(ColumnDataType.Guid, types["u"]);
        Assert.Equal(ColumnDataType.Text, types["ch"]);
        Assert.Equal(ColumnDataType.Text, types["iv"]);
        Assert.Equal(ColumnDataType.Text, types["en"]);
        Assert.Equal(ColumnDataType.Text, types["arr"]);
    }

    [PostgresFact]
    public async Task Same_date_type_on_both_targets()
    {
        var result = await Raw("SELECT '2024-03-05'::date AS d");

        var cell = Assert.IsType<DateTime>(result.Rows.Single()["d"]);
        Assert.Equal(new DateTime(2024, 3, 5), cell);
        Assert.Equal(ColumnDataType.Date, result.Columns.Single().DataType);
    }

    [PostgresFact]
    public async Task Session_pins_the_settings_the_parser_relies_on()
    {
        var result = await Raw("""
            SELECT current_setting('transaction_read_only') AS ro, current_setting('DateStyle') AS ds,
                   current_setting('TimeZone') AS tz, current_setting('IntervalStyle') AS iv,
                   current_setting('extra_float_digits') AS fd, current_setting('statement_timeout') AS st
            """);
        var row = result.Rows.Single();

        Assert.Equal("on", row["ro"]);
        Assert.Equal("ISO, YMD", row["ds"]);
        Assert.Equal("UTC", row["tz"]);
        Assert.Equal("postgres", row["iv"]);
        Assert.Equal("3", row["fd"]);
        Assert.Equal("1min", row["st"]);
    }

    [PostgresFact]
    public async Task Session_settings_do_not_leak_to_the_next_user_of_a_pooled_connection()
    {
        await Raw("SELECT 1");
        await Raw("SELECT 1");

        await using var connection = new NpgsqlConnection(database.ReaderConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_setting('transaction_read_only'), current_setting('DateStyle')", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.Equal("off", reader.GetString(0));
        Assert.NotEqual("ISO, YMD", reader.GetString(1));
    }

    // ---- AC-12 ------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task AC12_enum_created_after_the_pool_opened_reads_as_text()
    {
        await Raw("SELECT 1");

        await AdminExecute("""
            CREATE TYPE ex.late_mood AS ENUM ('glad', 'sorry');
            CREATE TABLE ex.late_t (m ex.late_mood);
            INSERT INTO ex.late_t VALUES ('glad');
            GRANT SELECT ON ex.late_t TO qb_it_reader;
            """);

        var result = await Raw("SELECT m FROM ex.late_t");

        Assert.Equal("glad", result.Rows.Single()["m"]);
        Assert.Equal(ColumnDataType.Text, result.Columns.Single().DataType);
    }

    // ---- AC-17 ------------------------------------------------------------------------------------------

    private async Task AssertQueryRunning(string marker)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var running = await AdminScalar(
                $"SELECT count(*) FROM pg_stat_activity WHERE datname = '{database.DatabaseName}' AND pid <> pg_backend_pid() " +
                $"AND state = 'active' AND query LIKE '%{marker}%' AND query NOT LIKE '%pg_stat_activity%'");
            if (running > 0)
            {
                return;
            }
            await Task.Delay(50);
        }
        Assert.Fail("The query never started running on the server.");
    }

    private async Task AssertQueryGone(string marker)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var running = await AdminScalar(
                $"SELECT count(*) FROM pg_stat_activity WHERE datname = '{database.DatabaseName}' AND pid <> pg_backend_pid() " +
                $"AND state = 'active' AND query LIKE '%{marker}%' AND query NOT LIKE '%pg_stat_activity%'");
            if (running == 0)
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail("The server-side query was still running.");
    }

    [PostgresFact]
    public async Task AC17_statement_timeout_gives_the_timeout_message_and_the_query_is_gone()
    {
        var marker = $"qbtimeout{Guid.NewGuid():N}";

        // Lowers the timeout inside the same transaction; the production value (60 s) would make the test minutes long.
        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            Raw($"SET LOCAL statement_timeout = 400; SELECT pg_sleep(30) /* {marker} */"));

        Assert.Equal("The query took too long and was stopped.", ex.Message);
        await AssertQueryGone(marker);
    }

    [PostgresFact]
    public async Task AC17_cancelled_token_throws_cancellation_and_the_query_is_gone()
    {
        var marker = $"qbcancel{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource();

        var task = Raw($"SELECT pg_sleep(30) /* {marker} */", ct: cts.Token);
        await AssertQueryRunning(marker);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await AssertQueryGone(marker);
    }

    // ---- AC-20 ------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task AC20_wrong_connection_string_gives_the_message_and_hides_host_and_password()
    {
        var logger = new CapturingLogger();
        var connectionString = "Host=127.0.0.1;Port=1;Username=nobody;Password=hunter2-secret;Timeout=3;Pooling=false";

        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            Service(connectionString, logger).ExecuteAsync(Source(), new GeneratedQuery("SELECT 1", []), NoRuntime, null, default));

        Assert.Equal("Couldn't connect to this data source.", ex.Message);
        Assert.DoesNotContain("127.0.0.1", ex.Message);
        Assert.DoesNotContain("hunter2", ex.Message);
        Assert.Single(logger.Warnings);
    }

    [PostgresFact]
    public async Task AC20_malformed_connection_string_gives_the_same_message()
    {
        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            Service("this is not a connection string; Password=hunter2-secret").ExecuteAsync(
                Source(), new GeneratedQuery("SELECT 1", []), NoRuntime, null, default));

        Assert.Equal("Couldn't connect to this data source.", ex.Message);
        Assert.DoesNotContain("hunter2", ex.Message);
    }

    [PostgresFact]
    public async Task Authentication_failure_is_a_connect_failure_not_a_rejected_query()
    {
        var builder = new NpgsqlConnectionStringBuilder(database.ReaderConnectionString)
        {
            Username = "qb_it_no_such_role_xyz", Password = "wrong-password-xyz", Pooling = false
        };

        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            Service(builder.ConnectionString).ExecuteAsync(Source(), new GeneratedQuery("SELECT 1", []), NoRuntime, null, default));

        Assert.Equal("Couldn't connect to this data source.", ex.Message);
        Assert.DoesNotContain("wrong-password-xyz", ex.Message);
    }

    // ---- truncation -------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task Row_limit_returns_N_rows_and_flags_truncation_from_the_extra_row()
    {
        var result = await Raw("SELECT g FROM generate_series(1, 10) g ORDER BY g", maxRows: 3);

        Assert.Equal(3, result.Rows.Count);
        Assert.True(result.Truncated);
        Assert.Equal([1L, 2L, 3L], result.Rows.Select(r => (long)r["g"]!));
    }

    [PostgresFact]
    public async Task Exactly_the_limit_is_not_truncated()
    {
        var result = await Raw("SELECT g FROM generate_series(1, 3) g", maxRows: 3);

        Assert.Equal(3, result.Rows.Count);
        Assert.False(result.Truncated);
    }

    // ---- AC-3b ------------------------------------------------------------------------------------------

    private sealed class FakeScope(ResolvedDataScope scope) : ICurrentDataScope
    {
        public bool IsEnabled => true;
        public IReadOnlyList<string> HostKeys => ["Tenant"];
        public Task<ResolvedDataScope> GetAsync(CancellationToken cancellationToken) => Task.FromResult(scope);
    }

    private sealed class FakeRules(IReadOnlyList<DataScopeRule> rules) : IDataScopeRuleRepository
    {
        public Task<IReadOnlyList<DataScopeRule>> GetForDataSourceAsync(Guid dataSourceId, CancellationToken cancellationToken) => Task.FromResult(rules);
        public Task ReplaceForDataSourceAsync(Guid dataSourceId, IReadOnlyList<DataScopeRule> newRules, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private DataScopeGuard Guard(string tenantColumn, string value)
    {
        var catalog = new DataCatalogService(
            new Repo(Source()), new Resolver(database.ReaderConnectionString),
            [new SqlServerCatalogReader(), new PostgresCatalogReader(NullLogger<PostgresCatalogReader>.Instance)],
            new MemoryCache(new MemoryCacheOptions()), NullLogger<DataCatalogService>.Instance);

        return new DataScopeGuard(
            new FakeScope(TestData.Scoped(("Tenant", [value]))),
            new Repo(Source()),
            new FakeRules([TestData.Rule("ex.ops", "Tenant", tenantColumn)]),
            catalog);
    }

    [PostgresFact]
    public async Task AC3b_scope_on_a_nondeterministic_column_never_reaches_sql()
    {
        var guard = Guard("c_ci", "acme");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            guard.AuthorizeAsync(TestData.DataSourceId, OpsDefinition("id", FilterOperator.IsNotNull, null, ColumnDataType.Number), default));
    }

    [PostgresFact]
    public async Task AC3b_scope_on_a_plain_text_column_is_exact()
    {
        var guard = Guard("c_tenant", "acme");
        var definition = OpsDefinition("id", FilterOperator.IsNotNull, null, ColumnDataType.Number);

        var predicates = await guard.AuthorizeAsync(TestData.DataSourceId, definition, default);

        var actual = await Ids(definition, predicates);

        Assert.Equal<long>([2L], actual);
    }

    // ---- AC-6 -------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task AC6_values_with_quotes_and_sql_fragments_stay_bound()
    {
        var ids = await Ids("c_text", FilterOperator.Equals, "x'; DROP TABLE ex.ops;--");

        Assert.Empty(ids);
        Assert.Equal(4, await AdminScalar("SELECT count(*) FROM ex.ops"));
    }

    [PostgresFact]
    public async Task AC6_drop_table_value_in_contains_and_in_lists_stays_bound()
    {
        Assert.Empty(await Ids("c_text", FilterOperator.Contains, "'; DROP TABLE ex.ops;--"));
        Assert.Empty(await Ids("c_text", FilterOperator.In, JsonSerializer.Serialize(new[] { "x'; DROP TABLE ex.ops;--", "y" })));
        Assert.Equal(4, await AdminScalar("SELECT count(*) FROM ex.ops"));
    }

    // ---- AC-10 / AC-10a ---------------------------------------------------------------------------------

    public static IEnumerable<object?[]> Operators() =>
    [
        // Number
        ["c_int", FilterOperator.Equals, "10", ColumnDataType.Number, new long[] { 1 }],
        ["c_int", FilterOperator.NotEquals, "10", ColumnDataType.Number, new long[] { 2 }],
        ["c_int", FilterOperator.GreaterThan, "10", ColumnDataType.Number, new long[] { 2 }],
        ["c_int", FilterOperator.GreaterOrEqual, "10", ColumnDataType.Number, new long[] { 1, 2 }],
        ["c_int", FilterOperator.LessThan, "20", ColumnDataType.Number, new long[] { 1 }],
        ["c_int", FilterOperator.LessOrEqual, "20", ColumnDataType.Number, new long[] { 1, 2 }],
        ["c_int", FilterOperator.Between, "[\"5\",\"15\"]", ColumnDataType.Number, new long[] { 1 }],
        ["c_int", FilterOperator.In, "[\"10\",\"99\"]", ColumnDataType.Number, new long[] { 1 }],
        ["c_int", FilterOperator.NotIn, "[\"10\"]", ColumnDataType.Number, new long[] { 2 }],
        ["c_int", FilterOperator.IsNull, null, ColumnDataType.Number, new long[] { 3, 4 }],
        ["c_int", FilterOperator.IsNotNull, null, ColumnDataType.Number, new long[] { 1, 2 }],
        ["c_numeric", FilterOperator.Equals, "1.5", ColumnDataType.Number, new long[] { 1 }],
        ["c_numeric", FilterOperator.GreaterThan, "2", ColumnDataType.Number, new long[] { 2 }],
        ["c_float", FilterOperator.LessThan, "3", ColumnDataType.Number, new long[] { 1 }],
        ["c_float", FilterOperator.Equals, "3.5", ColumnDataType.Number, new long[] { 2 }],
        // Boolean
        ["c_bool", FilterOperator.IsTrue, null, ColumnDataType.Boolean, new long[] { 1 }],
        ["c_bool", FilterOperator.IsFalse, null, ColumnDataType.Boolean, new long[] { 2 }],
        ["c_bool", FilterOperator.Equals, "true", ColumnDataType.Boolean, new long[] { 1 }],
        ["c_bool", FilterOperator.NotEquals, "true", ColumnDataType.Boolean, new long[] { 2 }],
        // Date and DateTime
        ["c_date", FilterOperator.Equals, "2024-01-15", ColumnDataType.Date, new long[] { 1 }],
        ["c_date", FilterOperator.GreaterThan, "2024-01-31", ColumnDataType.Date, new long[] { 2 }],
        ["c_date", FilterOperator.LessOrEqual, "2024-01-15", ColumnDataType.Date, new long[] { 1 }],
        ["c_date", FilterOperator.Between, "[\"2024-01-01\",\"2024-01-31\"]", ColumnDataType.Date, new long[] { 1 }],
        ["c_ts", FilterOperator.GreaterThan, "2024-02-01 00:00:00", ColumnDataType.DateTime, new long[] { 2 }],
        ["c_ts", FilterOperator.LessThan, "2024-02-01 00:00:00", ColumnDataType.DateTime, new long[] { 1 }],
        ["c_ts", FilterOperator.Equals, "2024-01-15 10:30:00", ColumnDataType.DateTime, new long[] { 1 }],
        ["c_tstz", FilterOperator.GreaterThan, "2024-02-01 00:00:00", ColumnDataType.DateTime, new long[] { 2 }],
        ["c_tstz", FilterOperator.Equals, "2024-01-15 10:30:00", ColumnDataType.DateTime, new long[] { 1 }],
        // Guid
        ["c_uuid", FilterOperator.Equals, "11111111-1111-1111-1111-111111111111", ColumnDataType.Guid, new long[] { 1 }],
        ["c_uuid", FilterOperator.NotEquals, "11111111-1111-1111-1111-111111111111", ColumnDataType.Guid, new long[] { 2 }],
        // Text
        ["c_text", FilterOperator.Equals, "gamma", ColumnDataType.Text, new long[] { 2 }],
        ["c_text", FilterOperator.Equals, "GAMMA", ColumnDataType.Text, new long[] { }],
        ["c_text", FilterOperator.NotEquals, "gamma", ColumnDataType.Text, new long[] { 1, 4 }],
        ["c_text", FilterOperator.Contains, "pha", ColumnDataType.Text, new long[] { 1 }],
        ["c_text", FilterOperator.Contains, "ALPHA", ColumnDataType.Text, new long[] { 1 }],
        ["c_text", FilterOperator.NotContains, "pha", ColumnDataType.Text, new long[] { 2, 4 }],
        ["c_text", FilterOperator.StartsWith, "Alp", ColumnDataType.Text, new long[] { 1 }],
        ["c_text", FilterOperator.EndsWith, "beta", ColumnDataType.Text, new long[] { 1 }],
        ["c_text", FilterOperator.In, "[\"gamma\",\"nope\"]", ColumnDataType.Text, new long[] { 2 }],
        ["c_text", FilterOperator.NotIn, "[\"gamma\"]", ColumnDataType.Text, new long[] { 1, 4 }],
        ["c_text", FilterOperator.IsNull, null, ColumnDataType.Text, new long[] { 3 }],
        ["c_text", FilterOperator.IsNotNull, null, ColumnDataType.Text, new long[] { 1, 2, 4 }],
        // Backslash and percent: the value is literal text except that % still acts as a wildcard.
        ["c_text", FilterOperator.Contains, @"C:\temp", ColumnDataType.Text, new long[] { 4 }],
        ["c_text", FilterOperator.EndsWith, @"\", ColumnDataType.Text, new long[] { 4 }],
        ["c_text", FilterOperator.StartsWith, @"C:\", ColumnDataType.Text, new long[] { 4 }],
        ["c_text", FilterOperator.Contains, "Al%ta", ColumnDataType.Text, new long[] { 1 }],
        // Enum, citext, char(n), json, time through the text operators
        ["c_enum", FilterOperator.Equals, "calm", ColumnDataType.Text, new long[] { 1 }],
        ["c_enum", FilterOperator.NotEquals, "calm", ColumnDataType.Text, new long[] { 2 }],
        ["c_enum", FilterOperator.Contains, "ens", ColumnDataType.Text, new long[] { 2 }],
        ["c_enum", FilterOperator.StartsWith, "ca", ColumnDataType.Text, new long[] { 1 }],
        ["c_enum", FilterOperator.In, "[\"calm\",\"tense\"]", ColumnDataType.Text, new long[] { 1, 2 }],
        ["c_citext", FilterOperator.Equals, "Alpha", ColumnDataType.Text, new long[] { 1 }],
        ["c_citext", FilterOperator.Equals, "alpha", ColumnDataType.Text, new long[] { }],
        ["c_citext", FilterOperator.Contains, "LPH", ColumnDataType.Text, new long[] { 1 }],
        ["c_citext", FilterOperator.EndsWith, "ta", ColumnDataType.Text, new long[] { 2 }],
        ["c_citext", FilterOperator.In, "[\"beta\"]", ColumnDataType.Text, new long[] { 2 }],
        ["c_char", FilterOperator.Equals, "ab", ColumnDataType.Text, new long[] { 1 }],
        ["c_char", FilterOperator.Contains, "c", ColumnDataType.Text, new long[] { 2 }],
        ["c_char", FilterOperator.StartsWith, "a", ColumnDataType.Text, new long[] { 1 }],
        ["c_char", FilterOperator.In, "[\"cd\"]", ColumnDataType.Text, new long[] { 2 }],
        ["c_json", FilterOperator.Contains, "\"k\":1", ColumnDataType.Text, new long[] { 1 }],
        ["c_json", FilterOperator.Equals, "{\"k\":2}", ColumnDataType.Text, new long[] { 2 }],
        ["c_json", FilterOperator.StartsWith, "{", ColumnDataType.Text, new long[] { 1, 2 }],
        ["c_time", FilterOperator.Equals, "10:30:00", ColumnDataType.Text, new long[] { 1 }],
        ["c_time", FilterOperator.StartsWith, "08", ColumnDataType.Text, new long[] { 2 }],
        ["c_time", FilterOperator.Contains, ":30:", ColumnDataType.Text, new long[] { 1 }],
        // Every operator offered per type (filter-operators.ts)
        ["c_date", FilterOperator.NotEquals, "2024-01-15", ColumnDataType.Date, new long[] { 2 }],
        ["c_date", FilterOperator.GreaterOrEqual, "2024-02-20", ColumnDataType.Date, new long[] { 2 }],
        ["c_date", FilterOperator.LessThan, "2024-02-01", ColumnDataType.Date, new long[] { 1 }],
        ["c_date", FilterOperator.IsNull, null, ColumnDataType.Date, new long[] { 3, 4 }],
        ["c_date", FilterOperator.IsNotNull, null, ColumnDataType.Date, new long[] { 1, 2 }],
        ["c_ts", FilterOperator.NotEquals, "2024-01-15 10:30:00", ColumnDataType.DateTime, new long[] { 2 }],
        ["c_ts", FilterOperator.GreaterOrEqual, "2024-02-20 08:00:00", ColumnDataType.DateTime, new long[] { 2 }],
        ["c_ts", FilterOperator.LessOrEqual, "2024-01-15 10:30:00", ColumnDataType.DateTime, new long[] { 1 }],
        ["c_ts", FilterOperator.Between, "[\"2024-01-01 00:00:00\",\"2024-01-31 00:00:00\"]", ColumnDataType.DateTime, new long[] { 1 }],
        ["c_ts", FilterOperator.IsNull, null, ColumnDataType.DateTime, new long[] { 3, 4 }],
        ["c_ts", FilterOperator.IsNotNull, null, ColumnDataType.DateTime, new long[] { 1, 2 }],
        ["c_tstz", FilterOperator.NotEquals, "2024-01-15 10:30:00", ColumnDataType.DateTime, new long[] { 2 }],
        ["c_tstz", FilterOperator.GreaterOrEqual, "2024-02-20 08:00:00", ColumnDataType.DateTime, new long[] { 2 }],
        ["c_tstz", FilterOperator.LessOrEqual, "2024-01-15 10:30:00", ColumnDataType.DateTime, new long[] { 1 }],
        ["c_tstz", FilterOperator.Between, "[\"2024-01-01 00:00:00\",\"2024-01-31 00:00:00\"]", ColumnDataType.DateTime, new long[] { 1 }],
        ["c_tstz", FilterOperator.IsNull, null, ColumnDataType.DateTime, new long[] { 3, 4 }],
        ["c_tstz", FilterOperator.IsNotNull, null, ColumnDataType.DateTime, new long[] { 1, 2 }],
        ["c_tstz", FilterOperator.LessThan, "2024-02-01 00:00:00", ColumnDataType.DateTime, new long[] { 1 }],
        ["c_uuid", FilterOperator.In, "[\"11111111-1111-1111-1111-111111111111\"]", ColumnDataType.Guid, new long[] { 1 }],
        ["c_uuid", FilterOperator.NotIn, "[\"11111111-1111-1111-1111-111111111111\"]", ColumnDataType.Guid, new long[] { 2 }],
        ["c_uuid", FilterOperator.IsNull, null, ColumnDataType.Guid, new long[] { 3, 4 }],
        ["c_uuid", FilterOperator.IsNotNull, null, ColumnDataType.Guid, new long[] { 1, 2 }],
        ["c_enum", FilterOperator.NotContains, "ens", ColumnDataType.Text, new long[] { 1 }],
        ["c_enum", FilterOperator.StartsWith, "te", ColumnDataType.Text, new long[] { 2 }],
        ["c_enum", FilterOperator.EndsWith, "se", ColumnDataType.Text, new long[] { 2 }],
        ["c_enum", FilterOperator.In, "[\"tense\"]", ColumnDataType.Text, new long[] { 2 }],
        ["c_enum", FilterOperator.NotIn, "[\"calm\"]", ColumnDataType.Text, new long[] { 2 }],
        ["c_enum", FilterOperator.IsNull, null, ColumnDataType.Text, new long[] { 3, 4 }],
        ["c_enum", FilterOperator.IsNotNull, null, ColumnDataType.Text, new long[] { 1, 2 }],
        ["c_citext", FilterOperator.NotEquals, "Alpha", ColumnDataType.Text, new long[] { 2 }],
        ["c_citext", FilterOperator.NotContains, "LPH", ColumnDataType.Text, new long[] { 2 }],
        ["c_citext", FilterOperator.StartsWith, "al", ColumnDataType.Text, new long[] { 1 }],
        ["c_citext", FilterOperator.NotIn, "[\"beta\"]", ColumnDataType.Text, new long[] { 1 }],
        ["c_citext", FilterOperator.IsNull, null, ColumnDataType.Text, new long[] { 3, 4 }],
        ["c_citext", FilterOperator.IsNotNull, null, ColumnDataType.Text, new long[] { 1, 2 }],
        ["c_char", FilterOperator.NotEquals, "ab", ColumnDataType.Text, new long[] { 2 }],
        ["c_char", FilterOperator.NotContains, "c", ColumnDataType.Text, new long[] { 1 }],
        ["c_char", FilterOperator.StartsWith, "c", ColumnDataType.Text, new long[] { 2 }],
        ["c_char", FilterOperator.EndsWith, "d", ColumnDataType.Text, new long[] { 2 }],
        ["c_char", FilterOperator.NotIn, "[\"cd\"]", ColumnDataType.Text, new long[] { 1 }],
        ["c_char", FilterOperator.IsNull, null, ColumnDataType.Text, new long[] { 3, 4 }],
        ["c_char", FilterOperator.IsNotNull, null, ColumnDataType.Text, new long[] { 1, 2 }],
        ["c_json", FilterOperator.Equals, "{\"k\":1}", ColumnDataType.Text, new long[] { 1 }],
        ["c_json", FilterOperator.NotEquals, "{\"k\":2}", ColumnDataType.Text, new long[] { 1 }],
        ["c_json", FilterOperator.NotContains, "\"k\":1", ColumnDataType.Text, new long[] { 2 }],
        ["c_json", FilterOperator.StartsWith, "{\"k\":2", ColumnDataType.Text, new long[] { 2 }],
        ["c_json", FilterOperator.EndsWith, "2}", ColumnDataType.Text, new long[] { 2 }],
        ["c_json", FilterOperator.In, "[\"{\\\"k\\\":1}\"]", ColumnDataType.Text, new long[] { 1 }],
        ["c_json", FilterOperator.NotIn, "[\"{\\\"k\\\":1}\"]", ColumnDataType.Text, new long[] { 2 }],
        ["c_json", FilterOperator.IsNull, null, ColumnDataType.Text, new long[] { 3, 4 }],
        ["c_json", FilterOperator.IsNotNull, null, ColumnDataType.Text, new long[] { 1, 2 }],
        ["c_time", FilterOperator.NotEquals, "10:30:00", ColumnDataType.Text, new long[] { 2 }],
        ["c_time", FilterOperator.NotContains, ":30:", ColumnDataType.Text, new long[] { 2 }],
        ["c_time", FilterOperator.EndsWith, "00:00", ColumnDataType.Text, new long[] { 2 }],
        ["c_time", FilterOperator.In, "[\"10:30:00\"]", ColumnDataType.Text, new long[] { 1 }],
        ["c_time", FilterOperator.NotIn, "[\"10:30:00\"]", ColumnDataType.Text, new long[] { 2 }],
        ["c_time", FilterOperator.IsNull, null, ColumnDataType.Text, new long[] { 3, 4 }],
        ["c_time", FilterOperator.IsNotNull, null, ColumnDataType.Text, new long[] { 1, 2 }],
        ["c_int", FilterOperator.GreaterThan, "5.5", ColumnDataType.Number, new long[] { 1, 2 }],
        ["c_int", FilterOperator.LessThan, "10.5", ColumnDataType.Number, new long[] { 1 }],
        ["c_int", FilterOperator.Equals, "10.0", ColumnDataType.Number, new long[] { 1 }],
        ["c_numeric", FilterOperator.GreaterOrEqual, "2", ColumnDataType.Number, new long[] { 2 }],
        ["c_numeric", FilterOperator.Equals, "2", ColumnDataType.Number, new long[] {  }],
        ["c_numeric", FilterOperator.Equals, "2.5", ColumnDataType.Number, new long[] { 2 }],
        ["c_float", FilterOperator.GreaterThan, "2", ColumnDataType.Number, new long[] { 1, 2 }],
        ["c_float", FilterOperator.Equals, "3", ColumnDataType.Number, new long[] {  }],
        // AC-10a: nondeterministic column
        ["c_ci", FilterOperator.Contains, "lph", ColumnDataType.Text, new long[] { 1 }],
        ["c_ci", FilterOperator.Contains, "acme", ColumnDataType.Text, new long[] { 2 }],
        ["c_ci", FilterOperator.StartsWith, "AC", ColumnDataType.Text, new long[] { 2 }],
        ["c_ci", FilterOperator.EndsWith, "pha", ColumnDataType.Text, new long[] { 1 }],
        ["c_ci", FilterOperator.NotContains, "lph", ColumnDataType.Text, new long[] { 2 }],
    ];

    [PostgresTheory]
    [MemberData(nameof(Operators))]
    public async Task AC10_operator_matches_the_expected_rows(
        string column, FilterOperator op, string? value, ColumnDataType type, long[] expected)
    {
        var actual = await Ids(column, op, value, type);

        Assert.Equal(expected, actual);
    }

    // ---- numeric binding and index use -----------------------------------------------------------------

    private async Task<string> Plan(string whereClause, ColumnDataType type, params string[] literals)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using (var set = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await set.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand($"EXPLAIN SELECT 1 FROM ex.big WHERE {whereClause}", connection);
        for (var i = 0; i < literals.Length; i++)
        {
            var name = literals.Length == 1 ? "@p0" : $"@__scope{i}";
            var parameter = new GeneratedQueryParameter(name, type, literals[i], false, null);
            command.Parameters.Add(new NpgsqlParameter(name, QueryParameterValues.ResolveForPostgres(parameter, NoRuntime)));
        }
        var plan = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.AppendLine(reader.GetString(0));
        }
        var text = plan.ToString();
        output.WriteLine($"PLAN {whereClause} [{type} {string.Join(",", literals)}] => " + string.Join(" | ", text.Split((char)10, StringSplitOptions.RemoveEmptyEntries)));
        return text;
    }

    // A cast wrapped around the column, such as (c_int)::numeric, is what stops an index being used as a condition.
    private static void AssertIndexConditionWithoutColumnCast(string plan)
    {
        Assert.Contains("Index Cond", plan);
        Assert.DoesNotContain("Seq Scan", plan);
        Assert.DoesNotMatch(@"\(c_\w+\)::", plan);
    }

    [PostgresTheory]
    [InlineData("c_int = @p0", ColumnDataType.Number, "5")]
    [InlineData("c_int > @p0", ColumnDataType.Number, "5")]
    [InlineData("c_int BETWEEN @p0 AND @p0", ColumnDataType.Number, "5")]
    [InlineData("c_bigint = @p0", ColumnDataType.Number, "5")]
    [InlineData("c_bigint = @p0", ColumnDataType.Number, "5000000000")]
    [InlineData("c_numeric = @p0", ColumnDataType.Number, "5")]
    [InlineData("c_numeric = @p0", ColumnDataType.Number, "5.5")]
    [InlineData("c_float = @p0", ColumnDataType.Number, "5")]
    [InlineData("c_float = @p0", ColumnDataType.Number, "5.5")]
    [InlineData("c_varchar::text = @p0", ColumnDataType.Text, "v5")]
    public async Task Bound_parameters_let_the_planner_use_an_index(string where, ColumnDataType type, string literal)
    {
        var plan = await Plan(where, type, literal);

        AssertIndexConditionWithoutColumnCast(plan);
    }

    [PostgresTheory]
    [InlineData("c_date = @p0", ColumnDataType.Date, "2020-02-01")]
    [InlineData("c_ts = @p0", ColumnDataType.DateTime, "2020-01-05 10:00:00")]
    [InlineData("c_tstz = @p0", ColumnDataType.DateTime, "2020-01-05 10:00:00")]
    public async Task Date_and_time_parameters_plan_is_measured(string where, ColumnDataType type, string literal)
    {
        var plan = await Plan(where, type, literal);

        AssertIndexConditionWithoutColumnCast(plan);
    }

    [PostgresFact]
    public async Task Scope_shaped_in_list_on_an_integer_column_returns_the_rows_and_uses_the_index()
    {
        var plan = await Plan("c_int IN (@__scope0, @__scope1)", ColumnDataType.Number, "5", "7");
        AssertIndexConditionWithoutColumnCast(plan);

        var definition = OpsDefinition("id", FilterOperator.IsNotNull, null, ColumnDataType.Number);
        var scope = new ScopePredicate("t0", "c_int", ColumnDataType.Number, ["10", "20"]);
        var ids = await Ids(definition, [scope]);

        Assert.Equal<long>([1, 2], ids);
    }

    [PostgresFact]
    public async Task Integer_column_compared_with_a_fraction_returns_the_right_rows()
    {
        var all = await Ids("c_int", FilterOperator.GreaterThan, "5.5", ColumnDataType.Number);
        var none = await Ids("c_int", FilterOperator.Equals, "10.5", ColumnDataType.Number);

        Assert.Equal<long>([1, 2], all);
        Assert.Empty(none);
    }

    [PostgresFact]
    public async Task Number_beyond_decimal_is_refused_with_the_not_valid_message_on_the_postgres_path()
    {
        var huge = new string('9', 40) + "0000";
        var ex = await Assert.ThrowsAsync<QueryExecutionException>(() =>
            Ids("c_int", FilterOperator.Equals, huge, ColumnDataType.Number));

        Assert.Equal($"Value '{huge}' is not a valid Number.", ex.Message);
    }

    [PostgresFact]
    public async Task Whole_numbers_bind_as_long_and_everything_else_as_decimal()
    {
        object? Bound(string v) => QueryParameterValues.ResolveForPostgres(
            new GeneratedQueryParameter("@p0", ColumnDataType.Number, v, false, null), NoRuntime);

        Assert.Equal(5L, Bound("5"));
        Assert.Equal(5L, Bound("5.0"));
        Assert.Equal(-7L, Bound("-7"));
        Assert.Equal(long.MaxValue, Bound("9223372036854775807"));
        Assert.Equal(5.5m, Bound("5.5"));
        Assert.Equal(1.50m, Bound("1.50"));
        Assert.Equal(7L, Bound("007"));
        Assert.Equal(5L, Bound(" 5 "));
        Assert.Equal(long.MinValue, Bound("-9223372036854775808"));
        Assert.Equal(9223372036854775808m, Bound("9223372036854775808"));
        Assert.IsType<decimal>(QueryParameterValues.Resolve(
            new GeneratedQueryParameter("@p0", ColumnDataType.Number, "5", false, null), NoRuntime));
    }

    // ---- AC-19 ------------------------------------------------------------------------------------------

    private AppUsersDirectory Directory(string? connectionString = null) =>
        new(new Repo(Source()), new Resolver(connectionString ?? database.ReaderConnectionString), NullLogger<AppUsersDirectory>.Instance);

    [PostgresFact]
    public async Task AC19_missing_app_users_view_means_sharing_unavailable()
    {
        var result = await Directory().GetUsersAsync(TestData.DataSourceId, default);

        Assert.False(result.Available);
        Assert.Empty(result.Users);
    }

    [PostgresFact]
    public async Task AC19_app_users_with_an_integer_id_returns_the_user()
    {
        await AdminExecute("""
            CREATE TABLE public.app_users (id int, display_name text, email text);
            INSERT INTO public.app_users VALUES (7, 'Zed Last', 'zed@example.com'), (3, 'Amy First', NULL);
            GRANT SELECT ON public.app_users TO qb_it_reader;
            """);

        try
        {
            var result = await Directory().GetUsersAsync(TestData.DataSourceId, default);

            Assert.True(result.Available);
            Assert.Equal(["Amy First", "Zed Last"], result.Users.Select(u => u.DisplayName));
            Assert.Equal("7", result.Users.Last().Id);
            Assert.Equal("zed@example.com", result.Users.Last().Email);
            Assert.Null(result.Users.First().Email);
        }
        finally
        {
            // The database is shared by the whole class, so leaving the table would break the "missing" test by order.
            await AdminExecute("DROP TABLE IF EXISTS public.app_users");
        }
    }

    private sealed class Resolver(string connectionString) : IConnectionStringResolver
    {
        public string Resolve(DataSource dataSource) => connectionString;
    }

    private sealed class Repo(DataSource dataSource) : IDataSourceRepository
    {
        public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<DataSource?>(dataSource);
        public Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(DataSource dataSource, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void UpdateOnly(DataSource dataSource, params string[] propertyNames) { }
        public void Update(DataSource ds) { }
        public Task<bool> TryReplaceEncryptedConnectionAsync(Guid id, string expected, string replacement, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class CapturingLogger : ILogger<PostgresQueryExecutionService>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
