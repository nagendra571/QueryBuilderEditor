# PostgreSQL data sources: specification

Status: **approved and built, with three changes made during the build that await the user's confirmation:** `ESCAPE ''` on `ILIKE` (4.3), whole numbers bound as `bigint` (4.4), and share help text naming both views (4.8). Evidence and reasoning are in `ANALYSIS.md`, especially sections 8 and 9. Results are in `VERIFICATION.md`.

## 1. Goal

A host can register a PostgreSQL database as a data source, and business users get the same experience as on SQL Server: catalog, builder,
parameters, run, export, charts, dashboards, sharing, record limits and row-level scope. One host can use both kinds of source at the same time.

## 2. Out of scope

- QueryBuilder's own tables on Postgres. They stay on SQL Server, so **a Postgres-only company still needs SQL Server for them**.
- A UI for registering data sources. It stays a SQL insert, as today.
- Redshift, CockroachDB and other compatible engines, and MySQL.
- Making Postgres results identical to SQL Server's: NULL sort order and fractional `AVG` follow Postgres.
- Testing behind PgBouncer: the design should suit its transaction mode, but this isn't verified.

## 3. What must not change

- **SQL Server behavior.** Its generated SQL stays byte-identical and is pinned by tests written before the refactor.
- **No migration.** `DataSources.Provider` already stores `1` for Postgres. This depends on the ambiguous-name rule in 4.2: metadata collation can't distinguish `public.Orders` from `public.orders`, so such pairs never reach it.
- **The security model:** every path still goes validate, then scope guard, then build. Scope stays in `WHERE`, ahead of the parenthesized user filters. Undecided objects and failed resolvers stay fail-closed.
- **One NuGet package.** Internal layers stay bundled.

## 4. Behavior

### 4.1 Registration

```sql
INSERT INTO DataSources (Id, Name, Provider, ConnectionStringName, AllowedSchemas, AllowedObjects, CatalogScope, IsActive, CreatedBy, CreatedAtUtc)
VALUES (NEWID(), 'Sales PG', 1, 'SalesPg', '', '', 2, 1, 'admin', SYSUTCDATETIME());
```

The connection string entry uses Npgsql syntax (`Host=...;Database=...;Username=...;Password=...`), including its SSL options. Both READMEs get this, and the outdated insert example with `ViewsOnly` (`src/QueryBuilder.Editor/README.md:87`,
`:93`) is corrected. Exact columns are confirmed against the schema when written.

### 4.2 Identifier matching (do this first)

Each provider supplies its identifier comparer: SQL Server `OrdinalIgnoreCase`, Postgres `Ordinal`. It's used wherever a schema, object or column name is matched:
`SqlServerDataCatalogService.cs:43,45,76,204,284,300`, `DataScopeGuard.cs:53,73,85`, `DataScopeEvaluator.cs:32`, `UpdateCatalogPolicyCommand.cs:26-27`, `UpdateDataScopeCommand.cs`, `GetDataScopeQuery.cs`.
Scope **keys** declared by the host aren't identifiers, so `DataScopeEvaluator.cs:52` doesn't change. The plan audits all 26 case-insensitive comparisons in `QueryBuilder.Application` and classifies each.

**Ambiguous objects are shown disabled, with a reason, and can't be used.** On Postgres, the catalog reader computes `schema.name` for every object the login can read, before any allowlist or kind filter. Objects whose keys are equal ignoring case are all **unavailable** (decided by the user, replacing an earlier "hide" design).
This one rule covers both `public.Orders` with `public.orders`, and schema `a.b` table `c` with schema `a` table `b.c`. It exists because:

- `DataScopeRules` has a unique index on `(DataSourceId, ObjectName, ScopeKey)` where `ScopeKey` is not null, and metadata is typically case-insensitive, so two such objects can't both hold a rule for one key (tested: error 2601).
- `schema.name` keys can't be told apart once joined, under any comparer.

**Unusable by construction.** `DataSourceCatalog` gets a separate `UnavailableObjects` list holding schema, name, kind and a reason code, and **no columns**. These objects are never in `Schemas`, so `ValidateAsync`, the scope guard, rule editing, dropdown options and chart rendering can't reach them without new code. A definition naming one gets the existing "not found in the catalog" error, whoever sends it, so the error confirms nothing to a scoped user.

**Who sees them:**

| Where | Shown | Reason text |
|---|---|---|
| Builder sidebar, unrestricted users and hosts with scoping off, if the allowlist and kind filter would otherwise include the object | Yes, disabled | "Can't be used: its full name clashes with another table or view. Ask your admin to add a view with a unique name." It doesn't name the other object, which the allowlist may exclude |
| Builder sidebar, **scoped users** | **No.** `FilterCatalogAsync` returns an empty list | Scoped users can't see undecided objects today, and listing names would break that |
| Admin catalog policy page | Always, disabled checkbox | "Clashes with `public.orders`. QueryBuilder can't tell these names apart. Add a uniquely named view over the one you want to expose." |
| Admin data scope section | Always, no state or mapping controls | Same as the policy page |

**Sidebar behavior:** a muted row with a warning icon and the reason visible as text, not only in a tooltip. It can't be selected, expanded or dragged and is not a button. It's still found by name search. The object count covers usable objects, followed by "· 2 unavailable" when there are any. It doesn't use the hatch, because `DESIGN.md` reserves that for "no access".

Consequences:

- The workaround is a uniquely named view over the object.
- The app log gets a warning naming the clash.
- If a clashing object appears later, both become unavailable until it's resolved. Saved queries, charts and dashboard tiles on them show their existing failure states and return no rows.
- **Saving must not lose decisions on unavailable objects.** `UpdateDataScopeCommand` replaces all rules, so it carries forward existing rules for unavailable objects. `UpdateCatalogPolicyCommand` accepts existing allowlist entries that are now unavailable instead of refusing the save as "unknown".
- **Columns** differing only by case stay supported and are matched by `Ordinal`, because `ColumnName` isn't part of that index.

**Safety net:** metadata collation may be broader than ignoring case, for example accent-insensitive. A unique violation while saving rules becomes a 400 naming the clash, instead of today's unmapped 500. The save is already one transaction, so existing rules survive it (`DataScopeRuleRepository.cs:31-37`).

SQL Server stays as it is, with the exception of that error mapping. Its existing dot-clash behavior is recorded as a separate issue in `ANALYSIS.md`.

### 4.3 SQL generation

The shared builder keeps today's structure. The dialect differs as follows:

| Concern | SQL Server | Postgres |
|---|---|---|
| Quoting | `[a]`, `]` doubled | `"a"`, `"` doubled |
| Is true, is false | `= 1`, `= 0` | `IS TRUE`, `IS FALSE` |
| Contains, Starts with, Ends with | `LIKE` | `col::text COLLATE "default" ILIKE @p ESCAPE ''` |
| Text and Unknown column in other filters | as is | `col::text` |
| Row limit | `TOP (n)` after `SELECT` | `LIMIT n` at the end |
| Alias over 63 **bytes** | allowed | Rejected: 400 naming the alias |

- `COLLATE "default"` is there because `ILIKE` raises an error on a nondeterministic collation, even after `::text` (tested).
- `ESCAPE ''` is there because Postgres treats `\` as an escape in patterns by default, unlike SQL Server, so `C:\temp` would match the wrong rows and a value ending in `\` would raise an error. `%` and `_` in values stay wildcards, as on SQL Server.
- `::text` also applies to scope predicates on Text and Unknown columns. These always use `IN`, **never `ILIKE`**.
- **Scope comparisons must be exact, and `IN` plus `::text` doesn't guarantee that:** on a column with a nondeterministic collation, `tenant::text IN ('acme')` matched `ACME` (tested). So a column with a nondeterministic collation **can't be a scope column** (4.6): saving such a rule is refused, and at runtime the object is hidden, like the existing missing-column case. `COLLATE "C"` was rejected as the general fix because it stops ordinary text columns using their index (tested).
- User Equals and IN are exact on normal columns, including `citext`. On a nondeterministic column they follow that column's collation. These are the user's own filters and not a security boundary.
- The row limit is a dialect method. SQL preview is unchanged, showing the query without it, as today.
- `@p0`, `@__scope0` and runtime `@name` parameters are unchanged.

### 4.4 Executing

Each Postgres execution, on one connection:

1. Begin a transaction, then set: `TRANSACTION READ ONLY`; `LOCAL statement_timeout = 60000`; `LOCAL DateStyle = 'ISO, YMD'`; `LOCAL TimeZone = 'UTC'`; `LOCAL IntervalStyle = 'postgres'`; `LOCAL extra_float_digits = 3`.
2. Run with `AllResultTypesAreUnknown = true` and `CommandTimeout = 65`, so the server's timeout normally fires first and gives the clearer error.
3. Parse each cell by its reported type (4.5). Roll back when done.

Parameter conversion stays as it is today (`decimal`, `bool`, `DateTime` with no zone, `Guid`, `string`, `DBNull`), with one Postgres exception: **a whole number that fits is bound as `long`, not `decimal`.** A `numeric` parameter makes Postgres cast the column, so an `int` filter or scope predicate can't use its index (measured: sequential scan versus index scan). A `bigint` parameter compares against `int` and `numeric` columns without that. Fractions stay `decimal`, so `id > 5.5` still works. A number too long for `decimal` is refused with the existing "not a valid Number" message. With `TimeZone` set to UTC, a date-time filter on `timestamptz` is read as UTC.

### 4.5 Reading values

| Postgres type | Cell becomes | Column data type |
|---|---|---|
| `smallint`, `integer`, `bigint`, `oid` | `long` | Number |
| `numeric` | `decimal` **only if lossless**, see below. NaN and Infinity: `null`. Otherwise the exact text | Number |
| `real`, `double precision` | `double`. **NaN and Infinity: `null`** | Number |
| `boolean` | `bool` | Boolean |
| `date` | `DateTime` at midnight. **Anything .NET can't hold: the text** | Date |
| `timestamp` | `DateTime`, no zone. Same fallback | DateTime |
| `timestamptz` | `DateTime` in UTC. Same fallback | DateTime |
| `uuid` | `Guid` | Guid |
| `character(n)` | Text with trailing spaces removed | Text |
| Everything else, including enums, `citext`, `money`, `interval`, `time`, `inet`, arrays, `json`, and any unrecognized type | The text as sent | Text |

**Lossless numeric rule.** A successful `decimal.TryParse` doesn't prove exactness: it rounded a 31-digit fraction, and turned a very small nonzero value into `0.0000...`, with no error (tested). So a cell becomes `decimal` only if formatting the parsed value with the invariant culture gives back the **identical string**. A value with excess trailing zeros also stays text, which is exact and harmless.

**Date rule.** Parse with exact formats under the pinned ISO style: `yyyy-MM-dd`, `yyyy-MM-dd HH:mm:ss` with up to 6 fractional digits, and the same with a zone suffix for `timestamptz`. Anything that doesn't match stays text, which covers `infinity`, `-infinity`, BC and years after 9999 (formats tested).

**Numeric text in charts.** The grid, CSV and Excel show the exact text. Charts convert it through the existing `toNumber` (`chart-data.ts:85`), so it plots at the **nearest JavaScript double**, as every decimal already does after JSON. It isn't refused, because a column like a ratio with a long fraction would otherwise lose its chart over digits nobody can see.

**Server checks must agree with the client.** `TryToDecimal` (`VisualizationDataChecks.cs:96-106`) returns `null` on overflow, so a value like `-1` followed by 40 digits would pass the funnel's no-negatives check and then plot as negative. Such checks must fall back to a `double` parse for numeric text.

### 4.6 Catalog

- Read from `pg_class`, `pg_namespace`, `pg_attribute`, `pg_type` and `pg_constraint`, using the query shape proven in the spike. Privileges are checked by **OID**.
- `r`, `p`, `f` are Table; `v`, `m` are View. Partition children are hidden.
- `information_schema`, `pg_catalog`, `pg_toast` and other `pg_*` schemas are excluded.
- Domains resolve to their base type, then map as below.
- The reader records whether each column's collation is nondeterministic, from `pg_attribute.attcollation` and `pg_collation.collisdeterministic`. This was tested on plain columns, domains and view columns, including a `COLLATE` expression inside a view.

| Catalog data type | Types |
|---|---|
| Number | `int2`, `int4`, `int8`, `numeric`, `float4`, `float8` |
| Date | `date` |
| DateTime | `timestamp`, `timestamptz` |
| Boolean | `bool` |
| Guid | `uuid` |
| Text | `text`, `varchar`, `bpchar`, `name`, `citext`, enums |
| Unknown | Everything else, including `money`, `time`, `interval`, `json`, arrays |

Column-only grants are hidden from the catalog, and object names with commas can't be allowlisted. Both are fail-safe and documented.

### 4.7 Errors

All driver errors are handled as `DbException` and become `QueryExecutionException`:

| Failure | Message to the user |
|---|---|
| `PostgresException` | "The database rejected the query:" plus `MessageText`, without the SQL state prefix |
| `57014`, or a timeout `NpgsqlException` | "The query took too long and was stopped." |
| `25006` | "This query tried to change data, which isn't allowed." |
| Cancellation | Unchanged, `OperationCanceledException` passes through |
| Can't connect | "Couldn't connect to this data source." Details go to the log only, not to the user |

### 4.8 Sharing

Postgres uses the opt-in view `public.app_users` with `id`, `display_name`, `email`, read as `id::text`. Any failure still means "sharing not available here". The two client help texts name both views with their own columns, since a dashboard can span providers, and the admin list shows "PostgreSQL" and "SQL Server" instead of raw values.

### 4.9 Packaging

Npgsql 9.0.5 on `net8.0`, Npgsql 10.0.3 on `net10.0`, declared in `Infrastructure` and `Editor` the way EF Core is, as version **1.2.0**. No new DBA script is needed, since there's no migration. Check for newer patch versions and advisories when implementing.

## 5. Structure

| New or changed | Responsibility |
|---|---|
| `ISqlDialect`, `SqlServerDialect`, `PostgresDialect` | Everything in the 4.3 table |
| Shared query builder, from `SqlServerQuerySqlBuilder` | Structure, scope, filters. One instance per dialect, through the existing factory |
| `IQueryExecutionService` implementation chosen by provider | Keeps `SqlServerQueryExecutionService` behavior; new `PostgresQueryExecutionService` holds 4.4, 4.5 and 4.7 |
| `ICatalogReader` per provider | Introspection only. Caching and `ValidateAsync` become shared |
| Identifier comparer per provider | 4.2 |
| `IAppUsersDirectory` | Dispatches on provider |

## 6. Acceptance criteria

**Security**

- AC-1. With `public.Orders` and `public.orders` both present, an unrestricted user sees both disabled in the sidebar with the reason, and can't select, expand or drag them. Run, export, preview, save, chart render and dropdown options all refuse a definition naming either, for every user, using the same not-found error. Other objects on the data source keep working.
- AC-1a. The same holds for schema `a.b` table `c` with schema `a` table `b.c`.
- AC-1b. If `public.orders` is created after `public.Orders` already has a "not scoped" rule, both become unavailable after the catalog refreshes and no rows are returned. Saving data scope or catalog policy in that state succeeds and keeps the rule and allowlist entry, and dropping the clash restores them.
- AC-1c. A scoped user's catalog response contains no unavailable objects and no sign of their names, count or reasons, and the sidebar shows no unavailable count.
- AC-1d. Unavailable objects carry no columns in any response. Business reason text never names the other object, and the admin pages do.
- AC-1e. An unavailable object excluded by the allowlist or kind filter doesn't appear in the builder, but does on the admin pages.
- AC-1f. The disabled row meets the sidebar's contrast in light and dark themes, and a screen reader reads its name and reason. SQL Server sources return an empty list and look unchanged.
- AC-2. A scope rule on column `CustomerId` filters on `CustomerId` when `customerid` also exists.
- AC-3. An allowlist entry for an unavailable object admits nothing, and differently-cased allowlist entries don't match on Postgres.
- AC-3a. **Against a case-insensitive SQL Server metadata database:** rules save and reload for a Postgres source with the case preserved. A forced unique violation returns a 400 naming the clash, and the earlier rules are intact.
- AC-3b. Under a case-insensitive nondeterministic collation, a scope rule on that column is refused, the object is hidden from scoped users if such a rule exists anyway, and a tenant value `acme` never returns `ACME` rows. This holds for plain columns, domains and view columns.
- AC-4. Every existing scope test also passes on the Postgres dialect, including that `b` can't bypass scope in `scope AND (a OR b)`.
- AC-5. A view whose function writes fails with the 4.7 message, and no row is written.
- AC-6. Filter values with quotes and SQL fragments stay bound. Identifiers containing `"` are quoted correctly.

**Unchanged**

- AC-7. SQL Server SQL for a representative set of definitions is byte-identical before and after the refactor, and all current tests pass on both targets.
- AC-8. A sample app run on SQL Server still passes run, export, chart, dashboard and scoped persona.

**Behavior, against real Postgres, on both targets**

- AC-9. The catalog shows tables, views, materialized views, partitioned parents and foreign tables with correct types and keys, and hides partition children, ungranted objects and system schemas.
- AC-10. Every filter operator works on each data type it's offered for, and on enum, `citext`, `char(n)`, `json` and `time` columns through text operators.
- AC-10a. Contains, Starts with and Ends with work on a nondeterministic column.
- AC-11. A result holding every type in 4.5, including NaN, Infinity, 40-digit `numeric`, `infinity` dates, BC dates, years after 9999, `interval` with months and `inet`, runs and exports to CSV and Excel without error. `infinity` is never shown as 9999-12-31.
- AC-11a. Parser tests: `12.50` stays `12.50`; a 29-digit and a 31-digit fraction, and `0.00000000000000000000000000000000001`, come back as the identical text and never as a rounded `decimal` or zero; `79228162514264337593543950335` is a `decimal` and one more is text.
- AC-11b. Full path, parser to chart: a 40-digit value and a long fraction show exactly in the grid and exports, and plot at their nearest double. A large negative value stored as text is refused by the funnel and pie negative checks.
- AC-12. An enum type created while the host is running can be selected and read after the catalog cache expires.
- AC-13. A date displays and charts the same on both targets.
- AC-14. Totals, HAVING, sorting, record limits with truncation and refused export all work.
- AC-15. Each of the 13 chart types renders, and a dashboard mixing tiles from a Postgres source and a SQL Server source works, with parameters, on one host.
- AC-16. Fixed and query-based dropdown parameters, multi-select, ranges and relative dates work.
- AC-17. A slow query stops at the timeout with the 4.7 message, and the server-side query is gone. Leaving the page cancels it.
- AC-18. An alias of 64 bytes, including multibyte text, is refused with a clear message; 63 bytes works.
- AC-19. Sharing works with `public.app_users`, and without it reports sharing unavailable.
- AC-20. A wrong connection string shows the 4.7 message and doesn't reveal the host or password.

**Packaging**

- AC-21. The packed 1.2.0 runs Postgres queries from a throwaway host on each target, with the metadata store on SQL Server.

## 7. Testing

- Dialect golden-SQL tests, scope tests on both dialects, and case-clash tests in `tests/QueryBuilder.Tests`, needing no database.
- Parser tests for 4.5, needing no database.
- Integration tests that run only when `QB_TEST_POSTGRES` is set, so everyday `dotnet test` stays database-free. **They're a required check before any package release** on both targets, and a missing setting at that step counts as not verified, not as passed. `CLAUDE.md`'s release workflow gets this step.
- Browser acceptance through Playwright on a temporary Postgres container, per `CLAUDE.md`. The container is described before it's started.

## 8. Limits to document

- `money` displays as the database formats it (`$12.50`) and can't be totaled or charted. Workaround: a view casting it to `numeric`.
- `numeric` beyond what `decimal` holds displays exactly, but charts plot it at the nearest double.
- Objects whose `schema.name` clash, ignoring case, are shown disabled and can't be used. Scoped users don't see them. Use a uniquely named view.
- Columns with a nondeterministic collation can't be scope columns.
- NaN and Infinity display as blank.
- Equals on `citext` is case-sensitive.
- Enum columns filtered through `::text` don't use their index.
- The login should be read-only anyway. The guard protects against mistakes, not a hostile database.
- PgBouncer is untested.

## 9. Decisions to confirm

Each has a default. Say if you'd like any changed:

1. **Decided by the user:** ambiguous objects are shown disabled with a reason, not supported through a migration. **Default to confirm:** scoped users don't see them at all (4.2), because listing names would reveal objects they can't otherwise see.
2. **Very long numerics are charted at the nearest double (4.5), not refused.**
3. **Nondeterministic columns can't be scope columns (4.3), not compared with `COLLATE "C"`.**
4. `money` is displayed as text, as in `ANALYSIS.md` section 8, rather than adding a round trip to every query to keep it numeric.
5. NaN and Infinity become blank.
6. Contains, Starts with and Ends with ignore case; Equals and IN don't.
7. The timeout is 60 seconds, the same as SQL Server today.
8. The sharing view is `public.app_users` in snake_case.
9. `net8.0` stays, though .NET 8 reaches end of support on November 10, 2026. Dropping it would be a separate decision.
