# PostgreSQL support: analysis

Status: analysis only, nothing implemented. Written 2026-11-06 against commit `aed3dcd` (package 1.1.0).
Redash is used as evidence only. No Redash code is to be copied.

**Decision:** Postgres as a queried data source only (option A in section 6). **Section 8 records the spike results and
overrides any prediction above it.** The design to build is in `SPEC.md`.

## 1. The main finding

**Redash's Postgres support solves about a third of our problem.** Redash users write their own SQL, so its
Postgres runner only has to connect, send text, read rows and list the schema. Redash has no SQL generator and no
row-level scope in its runner.

QueryBuilder generates the SQL and enforces data scope inside it. So the riskiest parts of this feature have no Redash
equivalent: dialect generation, keeping scope correct under a different case-sensitivity model, and binding typed
parameters. Redash is a good guide to connection, catalog and type handling, and nothing more.

## 2. How Redash does it

### Evidence and its limits

| Source | What it is | How it was read |
|---|---|---|
| `redash/redash:latest`, running as `redash_server` | Really **8.0.0+b32245, from October 2019**, Python 2.7, psycopg2 2.7.3.2. The `latest` tag is misleading | `query_runner/pg.py` and `__init__.py`, read in full |
| `redash/redash:25.8.0`, local image | Python 3.10, psycopg2-binary 2.9.6. **The main evidence** | The `PostgreSQL` class in `pg.py` in full, plus targeted searches of the base runner, parameters and task code. The rest of `pg.py` (Redshift and other variants) was not read in full |
| GitHub `master`, `pg.py` | Upstream today | Searched, not diffed line by line. The type map matches 25.8.0 |

These are static readings of readable Python source, so no decompilation was needed. **No Redash query was run** and
Redash's behavior wasn't observed at runtime. Container reads changed nothing; the 25.8.0 reads used throwaway
containers with no network.

### What the source shows (25.8.0 unless noted)

| Topic | What Redash does | Where |
|---|---|---|
| Driver | psycopg2 in asynchronous mode; a new connection for each execution, closed afterwards. No pool | `pg.py:252-264`, `266-298` |
| Dialect | None. The user's SQL is sent as written. The only rewrite is an optional appended ` LIMIT 1000` | `__init__.py:120`, `261-265` |
| Parameters | **Text substitution into the SQL**, using a Mustache renderer with escaping turned off, guarded by per-type validation. Not bound | `utils/__init__.py:146-148`, `parameterized_query.py:125-131` |
| Catalog | `information_schema.columns` unioned with `pg_class` for materialized views, foreign tables and partitioned tables, which it filters by `has_table_privilege` | `pg.py:206-250` |
| Catalog detail | Column name and type only. Types are null for the `pg_class` half. No primary or foreign keys | `pg.py:225`, `243` |
| Type mapping | Table of type OIDs to six logical types. `numeric` becomes float. Unknown OIDs become null | `pg.py:33-55`, `277` |
| Unusual values | Custom JSON encoding for range types, as strings. 8.0.0 dropped NaN at JSON time (`ignore_nan`) | `pg.py:187-196`; 8.0.0 `pg.py:201-203` |
| Duplicate result columns | Renamed with a numeric suffix | `__init__.py:215-229` |
| Timeout and cancel | Connect wait of 10 seconds. A job time limit or user cancel raises an exception, then `connection.cancel()` is sent to the server | `pg.py:268`, `291-293`; `tasks/queries/execution.py:82-90`, `126-127` |
| Safety | **No read-only transaction and no `statement_timeout`** in the runner | Absent from `pg.py` and `__init__.py` |
| SSL | `sslmode` choices, optional certificate files written to temp files | `pg.py:115-138`, `154-169` |
| Connection test | `SELECT 1` | `pg.py:142` |
| Compatible engines | Redshift, and others, are subclasses of the runner with their own catalog query | `pg.py:301` onward |
| Name collisions | Comment records a real clash when `public` is hidden from table names, so it qualifies names in that case | `pg.py:82-86` |

### What to take and what to avoid

**Take:** cover materialized, foreign and partitioned objects; hide objects the login can't select; send a server cancel on
timeout; offer SSL through the connection string; turn exotic types into strings instead of failing; tolerate unknown types.

**Avoid:** substituting parameters as text; turning `numeric` into float; column-only catalog data; relying on the
login's rights with no read-only guard. Our bound parameters, decimals, primary and foreign keys are already stronger.

## 3. Where our code stands

### Already provider-aware

- `DataSourceProvider.PostgreSql = 1` exists (`Enums.cs:6`), stored as an integer, so **no migration is needed for data sources**.
- `IQuerySqlBuilder` and `QuerySqlBuilderFactory` already dispatch on the provider at all five SQL paths (run, export,
  preview, chart render, dropdown options).
- `ConnectionStringResolver` works by name and is not tied to SQL Server. The client has no T-SQL logic, only two
  `dbo.AppUsers` help texts and a provider label (`AdminDataSourcesPage.tsx:47` shows the raw `postgreSql`).

### Hard-wired to SQL Server

| File | Assumption |
|---|---|
| `DependencyInjection.cs:53-59` | One catalog service and one executor, not chosen per data source. **The main structural gap** |
| `SqlServerQuerySqlBuilder.cs` | About 90% is standard SQL. Only these differ: bracket quoting (`SqlIdentifier.cs:10`), `= 1` and `= 0` for booleans (`:184`, `:186`), and case behavior of `LIKE` (`:216-225`) |
| `SqlServerQueryExecutionService.cs` | `TOP` inserted by editing the SQL string (`:81-85`), `SqlConnection`, `SqlParameter`, `SqlException` |
| `SqlServerDataCatalogService.cs` | `sys.*` queries (`:170-202`, `:260-269`). `ValidateAsync` (`:38-134`) is not provider-specific but lives in the same class |
| `SqlTypeMapper.cs` | SQL Server type names only |
| `AppUsersDirectory.cs:34` | `SELECT TOP` from `dbo.AppUsers`; `GetString` on `Id` |
| `AppDbContext` registration (`DependencyInjection.cs:24`) | Metadata store is SQL Server: see section 6 |

## 4. Differences that would actually bite us

### Security, must be solved first

**S1. Case-insensitive identifier matching is a scope risk on Postgres.** SQL Server (typically) treats `Orders` and
`orders` as one object; in Postgres `"Orders"` and `orders` can both exist. Our code matches them case-insensitively:

- `DataScopeEvaluator.cs:32` applies rules by object key ignoring case, so a "not scoped" rule on `public.Orders` **would
  also apply to `public.orders`, and scoped users could read it unfiltered**. This breaks the fail-closed promise.
- `DataScopeGuard.cs:73` picks the first column matching a scope column ignoring case, so it could filter on the wrong one.
- `SqlServerDataCatalogService.cs:300`: an allowlist entry would admit the differently-cased object too.
- `SqlServerDataCatalogService.cs:43`, `DataScopeGuard.cs:53`: `ToDictionary` throws on such a pair, so **one case-clashing
  pair would break every query on that data source**. `UpdateCatalogPolicyCommand.cs:26` silently keeps one of them.
- `:76`: validation accepts wrong-case column names and the builder then emits the browser's spelling.

Fix: an identifier comparer chosen by provider, `Ordinal` on Postgres, used everywhere an object or column is matched.
SQL Server stays as it is. Comparisons of user-declared scope keys are not identifiers and don't change.

**S2. Scope predicates must stay exact.** If we make text filters case-insensitive (below), scope must not follow:
`ProgramId IN (...)` stays a native, exact comparison, and stays ahead of the parenthesized user filters
(`SqlServerQuerySqlBuilder.cs:107-147`). This logic should move to shared code, unchanged, and the scope tests should run
against both dialects.

**S3. Read-only execution.** Generated SQL is always a `SELECT`, but a view can call a function that writes. Redash has no
guard here. Running in `SET TRANSACTION READ ONLY` with `SET LOCAL statement_timeout` is cheap on Postgres and better than
Redash. Recommended. The login should still be read-only.

### SQL generation

| # | Issue | Proposal |
|---|---|---|
| G1 | Quoting: `[x]` becomes `"x"`, with `"` doubled | Dialect method |
| G2 | `boolean = 1` is an error | `IS TRUE` and `IS FALSE` |
| G3 | `TOP` | `LIMIT n` at the end, as a dialect method. Removes the string edit |
| G4 | `LIKE` is case-sensitive, a visible change for business users | `ILIKE` for Contains, Starts with, Ends with. Equals and IN stay exact, so indexes still work |
| G5 | `enum = text`, `enum LIKE` and similar fail with "operator does not exist" | Cast the column to text in text filters: `col::text`. To be confirmed in the spike, including on `char(n)` |
| G6 | `@p0` and `@__scope0` | Npgsql rewrites named `@` parameters by default, so these should work unchanged. Confirm in the spike |
| G7 | Aliases over 63 bytes are silently truncated, so result columns stop matching the chart's column names | Reject them in the Postgres builder with a clear error |
| G8 | NULL sort order differs (last on ascending in Postgres, first in SQL Server) | Keep native behavior and document it |
| G9 | `AVG(int)` returns a fraction in Postgres, a truncated integer in SQL Server | Keep native. Postgres's answer is the better one |
| G10 | `GROUP BY` or `COUNT(DISTINCT)` on `json` fails | Surfaces as a clear database error, like other invalid uses |

### Reading results (the most likely source of runtime failures)

Today's executor passes `reader.GetValue(i)` straight through. SQL Server's types all happen to be safe; Postgres's aren't.

| Postgres type | Default .NET value (Npgsql docs) | What would break in our code |
|---|---|---|
| `date` | `DateOnly` on Npgsql 10, `DateTime` on 9 | `InferDataType` calls it Text (`:127-136`), so date charts and sorting change. Excel export probably throws (`ExportService.cs:78`) |
| `time` | `TimeOnly` on 10, `TimeSpan` on 9 | Same |
| `interval` | `TimeSpan`, and **the read throws if it has months or years** | One such value fails the whole query |
| `float8` | `double`, may be NaN or Infinity | `System.Text.Json` refuses these by default (no option set in `QueryBuilderJson.cs`), so the response fails |
| `numeric` | `decimal`; NaN or more digits than decimal holds is expected to throw | Fails the whole query. Confirm in the spike |
| `inet`, ranges, arrays, `json`, `bit`, `bytea` | Various .NET types | JSON and Excel output are unreliable |
| `timestamptz` | `DateTime` in UTC | Fine, though `timestamp` comes back without a zone |

Proposal: one shared normalizer so that results reach the JSON, CSV, Excel and chart code only as safe types (`null`, `bool`,
numbers, `string`, `DateTime`, `Guid`), and anything else becomes a string. For types that throw on read, the spike chooses
between casting those columns to text in the generated `SELECT` and Npgsql's text-result mode.

Writing parameters is less of a worry. Per Npgsql's docs, a `DateTime` with no zone goes as `timestamp`, which Postgres can
compare to `date` and `timestamptz`, the latter using the session time zone. `money` compared to a decimal, and a null with no
type, need checking in the spike.

### Catalog

- Read `pg_class`, `pg_namespace`, `pg_attribute`, `pg_type` and `pg_constraint`, not `information_schema`, which omits materialized
  views. Filter with `has_table_privilege` and `has_schema_privilege`.
- Treat `r`, `p`, `f` as Table and `v`, `m` as View. **Hide partition children** (`relispartition`): Redash lists them, but here they
  would clutter the catalog and each need their own scope decision, separate from the parent's.
- Exclude `pg_catalog`, `information_schema`, `pg_toast` and `pg_temp_*`. Resolve domains to their base type.
- Mapping: `bool` to Boolean; integers, `numeric`, floats to Number; `date` to Date; `timestamp` and `timestamptz` to DateTime;
  `uuid` to Guid; `text`, `varchar`, `char`, `name`, `citext` and enums to Text; everything else, including `time` and `interval`, to Unknown.
  `time` differs from SQL Server on purpose: a `DateTime` parameter can't be compared to a `time` column in Postgres.

### AppUsers

`dbo.AppUsers` has no direct equivalent. Proposal: `public.app_users` with `id`, `display_name`, `email`, matching Postgres naming, with
`id::text` so that integer or uuid keys work. Update the two client help texts to name the view per provider.

## 5. Proposed design

No new abstraction beyond what two providers need.

```
Application
  IQuerySqlBuilder, IQuerySqlBuilderFactory      already exist
  IDataSourceDialect                             new: comparer plus the other provider facts Application needs

Infrastructure/Sql
  AnsiQuerySqlBuilder (from SqlServerQuerySqlBuilder)   shared SELECT, scope, filters, GROUP BY
  ISqlDialect: SqlServerDialect, PostgresDialect        quote, bool test, contains, limit, text cast

Infrastructure/Execution
  QueryExecutionService                                 shared loop, DbConnection, DbException, normalizer
  IDbProviderDriver: SqlServerDriver, PostgresDriver    connection, parameter conversion, session setup

Infrastructure/Catalog
  DataCatalogService                                    cache and validation (ValidateAsync moved here)
  ICatalogReader: SqlServerCatalogReader, PostgresCatalogReader
```

Dispatch by `dataSource.Provider`, as the SQL builder already does, so **one host can query SQL Server and Postgres sources at
the same time**. Existing behavior is preserved: the SQL Server SQL text must stay byte-identical, and the existing tests pin it.

**Driver:** Npgsql, PostgreSQL license (permissive, no fee), NuGet metadata checked 2026-11-06.

| Target | Npgsql | Why |
|---|---|---|
| `net8.0` | 9.0.5 | A host already on the EF Core 9 Postgres provider (9.0.4, built on Npgsql 9) isn't forced onto 10 |
| `net10.0` | 10.0.3 | Matches `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 |

This is the same conditional pattern as EF Core, but `date` and `time` read differently between the two, which is another reason for the
normalizer, and for testing both targets. Check for advisories before committing to versions. Note that .NET 8 reaches end of support on
November 10, 2026, so whether to keep `net8.0` at all is worth a decision of its own.

**One package, not an add-on:** every consumer gets the Npgsql dependency, even if they only use SQL Server. A separate
`QueryBuilder.Editor.PostgreSql` package would avoid that but breaks the "install one package" rule in `CLAUDE.md`. Built in is recommended.

**Registration:** still a raw SQL insert into `DataSources`, with `Provider = 1` and a `ConnectionStringName` whose entry uses Npgsql syntax
(`Host=...;Database=...`). A registration UI stays out of scope. The README's insert example at `src/QueryBuilder.Editor/README.md:87` still names
the removed `ViewsOnly` column and needs correcting regardless.

**Tests:** run the SQL builder and scope tests against both dialects; add golden-SQL tests for Postgres; add case-clash tests for S1; add
integration tests against a real Postgres, opt-in by an environment variable so `dotnet test` still needs no database. Then a browser
acceptance run on a seeded Postgres source: catalog, run, export, each chart type, dashboards, dropdown parameters, and scoped personas.

## 6. Scope question: which "support"

| | A. Postgres as a data source | B. Postgres as the metadata store too |
|---|---|---|
| Who it serves | Hosts that keep QueryBuilder's tables in SQL Server and query Postgres | Hosts with no SQL Server at all |
| Migration | None | A second migration chain of all 10 migrations, in a separate bundled assembly, and a Postgres DBA script |
| Touches | Sections 4 and 5 | `nvarchar(max)` in `SavedQueryConfiguration.cs:20`, `AuditLogEntryConfiguration.cs:18`, `QueryVisualizationConfiguration.cs:16`; `DefaultDataSourceSeeder.cs:39`; EF provider on two targets; a package that picks a provider at startup |
| New risk | Limited to Postgres sources | Case-sensitive metadata lookups: user ids and emails in sharing could stop matching. Needs an audit of every string comparison in the repositories |
| Test load | Dialect tests on both targets | Everything above, multiplied by two metadata stores, plus upgrade tests |
| Relative size | Medium to large | Large, on top of A |

**Recommendation: build A now, and B as a separate release if a real Postgres-only host needs it.** A delivers the query experience and contains
the risk. Its limit is real, though: **a Postgres-only company would still need a SQL Server for QueryBuilder's own tables until B ships.** Redash
keeps the same separation, with a fixed metadata store (Postgres) and pluggable data sources.

## 7. Proposed order for A

1. **Spike** (throwaway, needs approval): a local `postgres:16` container and a scratch console app with Npgsql, to settle the open questions below.
2. **Refactor with no behavior change:** split out dialect, driver and catalog reader. SQL Server output stays identical, tests stay green.
3. **Identifier comparer (S1)**, with tests, before any Postgres code can reach it.
4. **Postgres dialect, driver and catalog reader**, with result normalizer.
5. **AppUsers, client text, READMEs, `/_setup`.**
6. **Acceptance run on both targets**, then version 1.2.0. No DBA script change, since no migration.

### Open questions for the spike

These come from documentation or inference and have **not been run**:

1. `numeric` NaN and oversized values: what exactly happens on read?
2. Best way to read `interval` with months, `inet`, arrays: `SELECT` cast or text-result mode?
3. Excel export of `DateOnly`: confirm the failure.
4. `col::text` on enum, `citext` and `char(n)`: results, trailing spaces, index impact.
5. `money` filters against a decimal parameter.
6. Null parameters with no type in `=` and `IN`.
7. `@__scope0` and repeated names through Npgsql's rewriting.
8. Read-only transaction and `SET LOCAL` through PgBouncer in transaction mode.
9. Timeout and cancel: does Npgsql's cancel stop the server-side query, and what exception does it raise?

All are answered in section 8, except PgBouncer.

### Decided by this analysis, unless you object

- Contains, Starts with and Ends with ignore case on Postgres; Equals and IN stay exact.
- Partition children are hidden from the catalog.
- Run inside a read-only transaction with a statement timeout.
- `public.app_users` is the sharing convention.
- Npgsql is a direct dependency of the one package.
- Redshift and CockroachDB are out of scope, but the dialect layout leaves room for them.

## 8. Spike results

Run 2026-11-06 on PostgreSQL 16.14 in a temporary local container, from one small app built for both package targets:
`net8.0` with Npgsql 9.0.5 and `net10.0` with Npgsql 10.0.3. Container and scratch code were then removed. `dotnet list package
--vulnerable` reported nothing for either Npgsql version. Results matched on both targets unless noted.

This tested the driver and SQL shapes. **QueryBuilder itself was not run against Postgres**, so S1 above is still a reading of our code
plus a confirmed fact (both `"Orders"` and `orders`, and columns differing only by case, can exist and be queried).

### Corrections to the analysis

| Earlier statement | What the spike showed |
|---|---|
| Excel export of `DateOnly` "probably throws" | **Wrong.** It works. The real effects are `InferDataType` calling it Text, and different values between targets (`DateOnly` on 10, `DateTime` on 9) |
| `timestamp` and `date` values are fine | **Worse than predicted.** `infinity` silently reads as 9999-12-31, and `-infinity` as 0001-01-01, a wrong value with no error |
| `numeric` only fails on NaN or huge values | Also fails on **31 digits of scale**, and reading it as `double` doesn't help |
| Normalizing .NET values after reading is enough | **Not enough**: many failures happen during the read, before any value exists. See "Reading" below |
| Filtering a `money` column might work | Fails with "operator does not exist: money = numeric", in HAVING too |

A first run also showed `enum` and `citext` as unreadable. That was partly caused by the test creating those types after its pool opened: from a fresh
process they read correctly as strings. The real, narrower problem is below.

### The nine questions

| # | Question | Answer |
|---|---|---|
| 1 | `numeric` NaN and oversized on read | `InvalidCastException` for NaN, `OverflowException` for Infinity, 40 digits, 31 digits of scale. **The whole query fails** |
| 2 | `interval` with months, `inet`, arrays | `interval` with months throws on read. `inet` and `cidr` read, then **JSON throws `SocketException`**. `macaddr` serializes as `{}`. `bit` and `tsvector` break CSV. Text mode handles all of them |
| 3 | Excel and `DateOnly` | Works. `float` NaN and Infinity do break Excel and JSON |
| 4 | `col::text` | Makes enum and json filters work. `char(5)` matches `ab`. **Indexes on `varchar` and `text` are still used.** Enum indexes are not, which is expected. `citext = text` is case-sensitive with or without the cast |
| 5 | `money` against a decimal | Error, as above |
| 6 | Untyped null parameters | Fine in `=`, `IN`, `LIKE`, `BETWEEN` on int, text, date and uuid |
| 7 | Parameter names | `@p0`, `@__scope0`, repeated names, and `@` inside quoted names all work |
| 8 | Read-only and timeout | **A view that writes really does write without the guard.** With it: error 25006 and no row added. `statement_timeout` fires as 57014. Settings don't leak back into the pool. **PgBouncer not tested** |
| 9 | Timeout and cancel | `CommandTimeout` gives `NpgsqlException` wrapping `TimeoutException`; a cancellation token gives `OperationCanceledException`. In both, **the server-side query stopped**. All driver errors derive from `DbException` |

### Other findings

- **A type created after the pool opens can't be read** (`InvalidCastException`, type name `-.-`) until `ReloadTypes`. So a DBA adding an enum could break queries on a
  running host. Text mode isn't affected.
- **Text formats follow session settings.** `DateStyle=German` turned dates into `15.01.2026` and `TimeZone` moved `timestamptz`, so they must be pinned. `SET LOCAL` did this.
- **`money` as text is locale-formatted** (`$12.50`), and `lc_monetary='C'` didn't remove the symbol.
- **Aliases:** over 63 bytes are truncated with only a NOTICE. Two long aliases can become identical. 40 `é` characters became 31, so the check must count **bytes**.
- **Checking privileges by a built-up name fails on mixed case**: `has_table_privilege('spike.MixedMv', ...)` looks for `mixedmv` and throws. This is the shape Redash uses, though Redash itself wasn't
  run. Checking by OID works.
- **Candidate catalog query worked under a restricted login in 8 to 22 ms**: hid ungranted tables and partition children, included partitioned, foreign and materialized objects, resolved nested domains, ignored dropped columns,
  returned primary and foreign keys. Tables with only column-level grants are hidden, which is fail-safe. A table named `a,b` can't be allowlisted, because `AllowedObjects` is stored comma-separated; also fail-safe.
- `information_schema.columns` doesn't list materialized views, confirming the choice of `pg_class`.
- Reader dispose **drains** remaining rows: 2 to 3 seconds after breaking early from 5 million. Always sending `LIMIT` avoids this.
- `PostgresException.Message` starts with the SQL state (`42703: column...`). `MessageText` is clean.
- `MIN` on `boolean` and `uuid`, `SUM` on text, and `GROUP BY` on `json` are errors.

### Reading: three options measured

Time to read 50,000 rows, average of 20 runs. The binary run narrowed `numeric` so it could be read at all, so compare roughly.

| Approach | `net8.0` | `net10.0` | Failure on unusual values |
|---|---|---|---|
| Binary read | 96 ms | 100 ms | Many, as above |
| **All columns as text, parsed by type** | 100 ms | 119 ms | None seen. Type names stay available |
| Describe first, then text per column | 120 ms | 163 ms | None seen, and keeps `money` numeric, at the cost of a round trip |

**Chosen: all columns as text.** It is the simplest and the only uniform rule: no read-time exceptions, no silent `infinity` mistakes, and no difference between the two Npgsql versions. It gives up numeric `money`, which is a
documented limit: a view can cast it to `numeric`.

## 9. Review findings, checked 2026-11-06

A second agent reviewed the analysis and SPEC. Each point was checked before changing the SPEC. Postgres results below are from a temporary `postgres:16` container (16.14), since removed. QueryBuilder itself still hasn't run against Postgres.

| # | Finding | Result | What changed |
|---|---|---|---|
| 1 | Metadata collation collides on case-only object names | **Confirmed.** Local metadata is `SQL_Latin1_General_CP1_CI_AS`, and a matching filtered unique index rejected the second row with error 2601. Rows with a null `ScopeKey` don't collide, as the index is filtered. The save is transactional, so the harm is a failed save returning an unmapped 500, not leaked data or lost rules | Hide ambiguous objects, so **no migration is needed**. The reviewer asked to reconsider that; this resolves it without one. Map the violation to a 400 |
| 2 | SPEC said numeric text can't be plotted | **Confirmed: the SPEC was wrong.** `toNumber` calls `Number()`. **The proposed fix wasn't adopted**: refusing would cost charts on long-fraction columns, and every JSON decimal is already a JavaScript double. A related bug was found: `TryToDecimal` returns `null` on overflow, so a huge negative passes the no-negatives check | SPEC now describes the real behavior, fixes the check, and tests the whole path |
| 3 | `TryParse` success isn't exactness; dates beyond 9999 | **Confirmed.** A 31-digit fraction rounded, and `0.00000000000000000000000000000000001` became zero, both with no error. Year 12000 failed exact parsing, so it falls back | Round-trip rule and exact formats |
| 4 | `IN` plus `::text` isn't exact | **Confirmed.** On a nondeterministic column, `tenant::text IN ('acme')` matched `ACME`, through plain columns, domains and view columns. `COLLATE "C"` is exact but dropped an ordinary column's index to a sequential scan. `ILIKE` errors on such columns, and `COLLATE "default"` fixes that | Such columns can't be scope columns, detected from `pg_collation` |
| 5 | `schema.name` keys are ambiguous | **Confirmed**, at `UpdateDataScopeCommand.cs:37` and also `DataScopeGuard.cs:64,87`, `SqlServerDataCatalogService.cs:43,300`, `GetDataScopeQuery.cs:39`, `UpdateCatalogPolicyCommand.cs:26` | Covered by the same hiding rule |
| - | Make real-Postgres tests a release requirement | Adopted | SPEC section 7 |

**Later decision by the user:** ambiguous objects are shown disabled with a reason, not hidden. This table's "hide" and "hiding" describe the earlier design; `SPEC.md` 4.2 is current. They still can't be used and still need no migration.

**Separate existing issue, not fixed by this feature:** on SQL Server, a schema `a.b` with table `c` beside schema `a` with table `b.c` should make the `ToDictionary` calls throw, from reading the code, which would break queries on that data source. It wasn't run, and it's rare. It needs its own decision, because changing SQL Server behavior is out of scope here.
