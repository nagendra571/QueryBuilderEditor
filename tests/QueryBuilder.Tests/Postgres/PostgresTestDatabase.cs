using Npgsql;

namespace QueryBuilder.Tests.Postgres;

/// <summary>A throwaway database on the server named by QB_TEST_POSTGRES, seeded with objects that exercise the
/// catalog reader and read through a restricted role, as a real data source login would be.</summary>
public sealed class PostgresTestDatabase : IAsyncLifetime
{
    public const string ReaderRole = "qb_it_reader";

    private readonly string? _adminConnectionString =
        Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvironmentVariable) is { Length: > 0 } value ? value : null;

    public string DatabaseName { get; } = $"qb_it_{Guid.NewGuid():N}";

    /// <summary>Connects as the owner, who can read and write everything.</summary>
    public string AdminConnectionString { get; private set; } = string.Empty;

    /// <summary>Connects as <see cref="ReaderRole"/>, which has no CREATE rights and only the grants the seed hands out.</summary>
    public string ReaderConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (_adminConnectionString is null)
        {
            return;
        }

        var server = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Pooling = false };
        await Execute(server.ConnectionString, $"CREATE DATABASE \"{DatabaseName}\"");

        // The role is cluster-wide and shared by every run, so creating it must tolerate a concurrent creator.
        await Execute(server.ConnectionString, $$"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{ReaderRole}}') THEN
                    CREATE ROLE {{ReaderRole}} LOGIN;
                END IF;
            EXCEPTION WHEN duplicate_object OR unique_violation THEN
                NULL;
            END
            $$
            """);

        var database = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = DatabaseName };
        AdminConnectionString = database.ConnectionString;
        await Execute(AdminConnectionString, Seed);

        var reader = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Username = ReaderRole, Password = null };
        ReaderConnectionString = reader.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_adminConnectionString is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        var server = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Pooling = false };
        await Execute(server.ConnectionString, $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private const string Seed = """
        CREATE SCHEMA qb;
        CREATE TYPE qb.mood AS ENUM ('sad', 'ok', 'happy');
        CREATE DOMAIN qb.price AS numeric(10,2);
        CREATE DOMAIN qb.price_nested AS qb.price;
        CREATE COLLATION qb.ci (provider = icu, locale = 'und-u-ks-level2', deterministic = false);
        CREATE DOMAIN qb.ci_text AS text COLLATE qb.ci;

        CREATE TABLE qb.parent (id int PRIMARY KEY);
        CREATE TABLE qb.t (
            id int PRIMARY KEY,
            parent_id int REFERENCES qb.parent (id),
            dropped text,
            c_int2 int2, c_int4 int4, c_int8 int8, c_numeric numeric, c_float4 float4, c_float8 float8,
            c_date date, c_timestamp timestamp, c_timestamptz timestamptz,
            c_bool bool, c_uuid uuid,
            c_text text, c_varchar varchar(20), c_bpchar char(3), c_name name, c_mood qb.mood,
            c_domain qb.price_nested,
            c_money money, c_time time, c_interval interval, c_json json, c_array int4[],
            c_ci text COLLATE qb.ci,
            c_ci_domain qb.ci_text
        );
        ALTER TABLE qb.t DROP COLUMN dropped;

        CREATE TABLE qb.part (id int, region text) PARTITION BY LIST (region);
        CREATE TABLE qb.part_a PARTITION OF qb.part FOR VALUES IN ('a');

        CREATE VIEW qb.v AS SELECT id, c_text FROM qb.t;
        CREATE MATERIALIZED VIEW qb.mv AS SELECT id FROM qb.t;
        CREATE MATERIALIZED VIEW qb."MixedMv" AS SELECT id FROM qb.t;

        CREATE EXTENSION file_fdw;
        CREATE SERVER qb_files FOREIGN DATA WRAPPER file_fdw;
        CREATE FOREIGN TABLE qb.ft (line text) SERVER qb_files OPTIONS (filename '/etc/hostname', format 'text');

        CREATE TABLE qb.secret (id int);
        CREATE TABLE qb.colgrant (a int, b int);

        CREATE VIEW qb.v_ci AS SELECT id, 'x'::text COLLATE qb.ci AS shouty, c_text FROM qb.t;

        CREATE TABLE public."Orders" (id int PRIMARY KEY);
        CREATE TABLE public.orders (id int PRIMARY KEY);
        CREATE SCHEMA "a.b";
        CREATE TABLE "a.b".c (id int);
        CREATE SCHEMA a;
        CREATE TABLE a."b.c" (id int);

        GRANT USAGE ON SCHEMA qb, public, "a.b", a TO qb_it_reader;
        GRANT SELECT ON ALL TABLES IN SCHEMA qb, public, "a.b", a TO qb_it_reader;
        REVOKE ALL ON qb.secret FROM qb_it_reader;
        REVOKE ALL ON qb.colgrant FROM qb_it_reader;
        GRANT SELECT (a) ON qb.colgrant TO qb_it_reader;

        -- Objects for the execution tests live in their own schema so the catalog tests' counts are untouched.
        CREATE EXTENSION citext;
        CREATE SCHEMA ex;
        CREATE TYPE ex.feeling AS ENUM ('calm', 'tense');
        CREATE TABLE ex.ops (
            id int PRIMARY KEY,
            c_int int, c_numeric numeric(12,2), c_float float8,
            c_date date, c_ts timestamp, c_tstz timestamptz,
            c_bool bool, c_uuid uuid,
            c_text text, c_enum ex.feeling, c_citext citext, c_char char(5), c_json json, c_time time,
            c_ci text COLLATE qb.ci, c_tenant text
        );
        INSERT INTO ex.ops VALUES
            (1, 10, 1.50, 2.5, '2024-01-15', '2024-01-15 10:30:00', '2024-01-15 10:30:00+00', true,
             '11111111-1111-1111-1111-111111111111', 'Alpha beta', 'calm', 'Alpha', 'ab', '{"k":1}', '10:30:00', 'Alpha', 'ACME'),
            (2, 20, 2.50, 3.5, '2024-02-20', '2024-02-20 08:00:00', '2024-02-20 08:00:00+00', false,
             '22222222-2222-2222-2222-222222222222', 'gamma', 'tense', 'beta', 'cd', '{"k":2}', '08:00:00', 'ACME', 'acme'),
            (3, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
            (4, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'C:\temp\', NULL, NULL, NULL, NULL, NULL, NULL, NULL);

        CREATE TABLE ex.big (
            c_int int, c_bigint bigint, c_numeric numeric, c_float float8,
            c_date date, c_ts timestamp, c_tstz timestamptz, c_varchar varchar(30)
        );
        INSERT INTO ex.big
            SELECT g, g, g, g, DATE '2020-01-01' + g, TIMESTAMP '2020-01-01' + g * INTERVAL '1 hour',
                   TIMESTAMPTZ '2020-01-01 00:00:00+00' + g * INTERVAL '1 hour', 'v' || g
            FROM generate_series(1, 2000) g;
        CREATE INDEX ON ex.big (c_int);
        CREATE INDEX ON ex.big (c_bigint);
        CREATE INDEX ON ex.big (c_numeric);
        CREATE INDEX ON ex.big (c_float);
        CREATE INDEX ON ex.big (c_date);
        CREATE INDEX ON ex.big (c_ts);
        CREATE INDEX ON ex.big (c_tstz);
        CREATE INDEX ON ex.big (c_varchar);
        ANALYZE ex.big;

        CREATE TABLE ex.counter (n int);
        CREATE FUNCTION ex.bump() RETURNS int LANGUAGE sql AS 'INSERT INTO ex.counter VALUES (1) RETURNING n';
        CREATE VIEW ex.writer AS SELECT ex.bump() AS n;

        GRANT USAGE ON SCHEMA ex TO qb_it_reader;
        GRANT SELECT ON ALL TABLES IN SCHEMA ex TO qb_it_reader;
        -- INSERT is granted on purpose: the writing view must be stopped by read-only mode, not by missing privileges.
        GRANT INSERT ON ex.counter TO qb_it_reader;
        """;
}
