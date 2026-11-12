# PostgreSQL data sources — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a host register a PostgreSQL database as a data source and give business users the same catalog, builder, run, export, chart, dashboard and row-scope experience as SQL Server.

**Architecture:** A dialect splits the SQL builder; a catalog reader per provider sits under a shared catalog service; a routing executor picks a SQL Server or Postgres execution service per data source. Postgres results are read as text and parsed by type. Identifier comparison becomes provider-specific, and objects whose `schema.name` clash are moved to a separate "unavailable" list that nothing can query.

**Tech Stack:** .NET 8 and 10, xUnit, Npgsql 9.0.5 (`net8.0`) and 10.0.3 (`net10.0`); React, TypeScript, `node --test`.

**Spec:** `docs/redash-parity/features/postgresql/SPEC.md`. Spike evidence: `ANALYSIS.md` sections 8 and 9.

## Global Constraints

- **Never commit, push, bump the version or pack the real package.** `CLAUDE.md` requires a separate ask for each. This overrides the usual per-task commit step. The work stays uncommitted on `feature/rea1`.
- **No migration.** Don't change `AppDbContext`, entity configurations or `Persistence/Migrations`.
- **SQL Server generated SQL stays byte-identical** (AC-7). The one allowed SQL Server behavior change is the unique-violation 400 in Task 7.
- **Only new package:** Npgsql. No Testcontainers, no paid libraries.
- Only Tasks 5 to 7 may need a database, and only when their environment variable is set. **Don't start containers or apps.** The reviewer provides `QB_TEST_POSTGRES` and runs acceptance in Task 10.
- Every filter value stays a bound parameter. Scope stays in `WHERE` ahead of the parenthesized user filters. Fail closed: anything ambiguous hides or refuses.
- No "what" comments; only "why" for non-obvious constraints.
- JSON keeps `QueryBuilderJson.Options`. New response fields are additive.
- Client: `crypto.randomUUID`, `crypto.subtle` and `navigator.clipboard` are banned (`client/tests/insecure-context.test.ts`). Zustand selectors must not return new arrays. Pure `.ts` model files use `import type` from `@/…` and relative `.ts` runtime imports. Use Zoning Map tokens; **don't use the hatch** for unavailable rows.

## Review Focus

1. A column type with a modifier, such as `timestamp(3) with time zone`, `numeric(10, 2)` or `character varying(50)`: must parse as its base type. Pinned in Task 4.
2. A host running under a non-English culture such as `de-DE`: numbers and dates must parse and round-trip the same way. Pinned in Task 4.
3. A schema containing only unavailable objects, and a name search matching only an unavailable object: the heading and row still appear, with no count or usable-row crash. Pinned in Task 8.
4. A `DataSources` row with `Provider = 2` (MySql, declared but unbuilt): must give a clear 400, not a 500 or a null reference. Pinned in Task 6.
5. A saved chart or dashboard tile whose object becomes unavailable: must show its failure state, not a 500, and must return no rows. Pinned in Task 2 (validation) and Task 10 (browser).

---

## File map

| Path | Responsibility |
|---|---|
| `src/QueryBuilder.Application/Common/IdentifierMatching.cs` | Provider to comparer |
| `src/QueryBuilder.Domain/Model/Catalog.cs` | Adds `Provider`, `UnavailableObjects`, `HasNondeterministicCollation` |
| `src/QueryBuilder.Infrastructure/Sql/ISqlDialect.cs`, `SqlServerDialect.cs`, `PostgresDialect.cs` | Dialect differences |
| `src/QueryBuilder.Infrastructure/Sql/SqlQueryBuilder.cs` | Shared builder, moved from `SqlServerQuerySqlBuilder` |
| `src/QueryBuilder.Infrastructure/Sql/SqlServerQuerySqlBuilder.cs`, `PostgresQuerySqlBuilder.cs` | Thin subclasses, so existing tests keep compiling |
| `src/QueryBuilder.Infrastructure/Catalog/DataCatalogService.cs` | Renamed from `SqlServerDataCatalogService`: cache, policy filters, validation |
| `src/QueryBuilder.Infrastructure/Catalog/ICatalogReader.cs`, `SqlServerCatalogReader.cs`, `PostgresCatalogReader.cs` | Introspection only |
| `src/QueryBuilder.Infrastructure/Catalog/AmbiguousObjects.cs`, `PostgresTypeMapper.cs` | Pure helpers |
| `src/QueryBuilder.Infrastructure/Execution/RoutingQueryExecutionService.cs`, `PostgresQueryExecutionService.cs`, `PostgresValueParser.cs`, `QueryParameterValues.cs` | Execution |
| `client/src/features/catalog/catalog-view.ts`, `client/src/features/catalog/unavailable.ts` | Pure view model and reason copy |
| `tests/QueryBuilder.Tests/Postgres/*` | Dialect, parser, integration tests |

---

### Task 1: Pin SQL Server SQL, then extract the dialect

**Files:**
- Create: `tests/QueryBuilder.Tests/SqlServerGoldenSqlTests.cs`, `src/QueryBuilder.Infrastructure/Sql/ISqlDialect.cs`, `SqlServerDialect.cs`, `SqlQueryBuilder.cs`
- Modify: `src/QueryBuilder.Infrastructure/Sql/SqlServerQuerySqlBuilder.cs`, `src/QueryBuilder.Infrastructure/Execution/SqlServerQueryExecutionService.cs`

**Interfaces:**
- Produces:
```csharp
public interface ISqlDialect
{
    DataSourceProvider Provider { get; }
    string Quote(string identifier);
    string BooleanTest(string expression, bool value);
    // expression is the column or aggregate; dataType comes from the condition or scope predicate.
    string Comparable(string expression, ColumnDataType dataType);
    string Like(string expression, ColumnDataType dataType, string parameterName, bool negate);
    void ValidateAlias(string alias);
    string ApplyRowLimit(string sql, int rowLimit);
}
public abstract class SqlQueryBuilder(ISqlDialect dialect) : IQuerySqlBuilder;
public sealed class SqlServerQuerySqlBuilder() : SqlQueryBuilder(new SqlServerDialect());
```

- [ ] **Step 1: Write `SqlServerGoldenSqlTests` against the current, unrefactored builder.** Use `TestData`. One `Assert.Equal` on the full SQL string for each of: plain select; every `FilterOperator`; aggregates with auto `GROUP BY`; `HAVING`; sort on an aggregated and a non-aggregated column; scope plus an OR user group; joins; a name containing `]`. Also assert `new SqlServerDialect().ApplyRowLimit("SELECT a\nFROM b", 11)` equals `"SELECT TOP (11) a\nFROM b"` in Step 3's version.
- [ ] **Step 2: Run** `dotnet test tests/QueryBuilder.Tests --filter SqlServerGoldenSqlTests`. Expected: all pass except the dialect assertion, which doesn't compile yet; keep that one out until Step 3. The expected strings must come from real output of the old code.
- [ ] **Step 3: Move the builder body to `SqlQueryBuilder`, replacing `SqlIdentifier` and literals with dialect calls.** `Comparable` wraps every non-null test, `IN`, `BETWEEN` and scope `IN` expression; `SqlServerDialect.Comparable` returns its input. `ValidateAlias` is called for each visible column's `EffectiveAlias` in `AppendSelect`, a no-op on SQL Server. Move `ApplyRowLimit` from the execution service into `SqlServerDialect`, and have the service call it. Keep `SqlIdentifier` only as `SqlServerDialect`'s helper.
- [ ] **Step 4: Run** `dotnet test tests/QueryBuilder.Tests`. Expected: all pass on `net8.0` and `net10.0` with **no edits to expected SQL in any test**.

---

### Task 2: Provider-specific identifiers and unavailable objects

No Postgres code here. Tests use fake catalogs with `Provider = PostgreSql`.

**Files:**
- Create: `src/QueryBuilder.Application/Common/IdentifierMatching.cs`, `tests/QueryBuilder.Tests/IdentifierMatchingTests.cs`
- Modify: `src/QueryBuilder.Domain/Model/Catalog.cs`, `src/QueryBuilder.Domain/Enums/Enums.cs`, `src/QueryBuilder.Application/Abstractions/IDataCatalogService.cs`, `DataScopeEvaluator.cs`, `DataScopeGuard.cs`, `UpdateDataScopeCommand.cs`, `UpdateCatalogPolicyCommand.cs`, `GetDataScopeQuery.cs`, `GetAdminDataSourceDetailQuery.cs`, `ParameterOptionsService.cs`, `AdminDtos.cs`, `SqlServerDataCatalogService.cs`, test fakes that implement `IDataCatalogService`, `DataScopeEvaluatorTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class IdentifierMatching
{
    public static StringComparison Comparison(DataSourceProvider provider); // PostgreSql: Ordinal; others: OrdinalIgnoreCase
    public static StringComparer Comparer(DataSourceProvider provider);
}
public enum UnavailableReason { NameClash = 0 }
// In DataSourceCatalog: DataSourceProvider Provider; List<UnavailableObject> UnavailableObjects = [];
public sealed class UnavailableObject { string SchemaName; string Name; SchemaObjectKind Kind; UnavailableReason Reason; }
// Admin only, so business responses can't carry partner names:
public sealed class AdminUnavailableObject { string SchemaName; string Name; SchemaObjectKind Kind; UnavailableReason Reason; List<string> ClashesWith = []; }
// In ColumnMetadata: bool HasNondeterministicCollation;
// IDataCatalogService adds:
Task<List<AdminUnavailableObject>> GetUnavailableObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken);
// DataScopeEvaluator.Evaluate gains a required parameter: StringComparison identifierComparison.
// AdminDataSourceDetailDto and AdminDataScopeDto each gain: List<AdminUnavailableObject> Unavailable.
```

**Comparison classification.** Change to provider comparison: `UpdateCatalogPolicyCommand:26,27`; `UpdateDataScopeCommand:37,40,72`; `DataScopeEvaluator:32`; `DataScopeGuard:53,73,85`; `GetDataScopeQuery:40`; `ParameterOptionsService:126`. Leave alone, because these aren't database identifiers: `IDataScope:11`; `UpdateDataScopeCommand:70`; `DataScopeEvaluator:52`; `GetDataScopeQuery:46,48,51`; `DashboardCommands`, `DashboardParameters`, `DashboardQueries`, `DashboardSharing`, `ParameterValidator`, `ParameterBinder`.

- [ ] **Step 1: Write the failing tests in `IdentifierMatchingTests`:**
  - `Evaluate_postgres_rule_on_Orders_does_not_apply_to_orders`: a not-scoped rule on `public.Orders`, `Ordinal`: `public.orders` is `Hidden`.
  - `Evaluate_sql_server_still_ignores_case`.
  - `Guard_postgres_filters_on_exact_column_when_case_pair_exists`: rule column `CustomerId`, object has `customerid` first and `CustomerId`: predicate column is `CustomerId`.
  - `Guard_hides_object_when_scope_column_is_nondeterministic`: `FilterCatalogAsync` omits it; `AuthorizeAsync` throws its existing refusal.
  - `FilterCatalog_scoped_user_gets_no_unavailable_objects_and_keeps_provider`.
  - `FilterCatalog_unrestricted_user_gets_unavailable_objects`.
  - `UpdateDataScope_refuses_nondeterministic_scope_column`: `CatalogValidationException` naming the column.
  - `UpdateDataScope_carries_forward_rules_for_unavailable_objects`: an existing rule on unavailable `public.Orders` is in the replacement list, with a new instance, when the request omits it.
  - `UpdateDataScope_does_not_accept_a_decision_on_an_unavailable_object`: "not in this data source's catalog".
  - `UpdateCatalogPolicy_keeps_existing_unavailable_allowlist_entry`, and `UpdateCatalogPolicy_refuses_newly_added_unavailable_entry`.
  - `UpdateCatalogPolicy_postgres_refuses_wrong_case_entry`.
  - `Validate_refuses_unavailable_object_with_not_found_message`, for a definition naming an entry of `UnavailableObjects`, with the same text as a nonexistent object.
- [ ] **Step 2: Run** `dotnet test tests/QueryBuilder.Tests --filter IdentifierMatchingTests`. Expected: fails to compile.
- [ ] **Step 3: Implement.** `DataScopeGuard.IsUsable` also requires no filter column with `HasNondeterministicCollation`. The `FilterCatalogAsync` scoped branch copies `Provider` and leaves `UnavailableObjects` empty. `GetDataScopeQuery` fills `Unavailable` when enabled, else `[]`. `GetUnavailableObjectsAsync` returns `[]` in the catalog service for now; Task 5 fills it. A rule's carry-forward matches `rule.ObjectName` against unavailable keys using the provider comparer. Update test fakes for the new interface member.
- [ ] **Step 4: Run** `dotnet test tests/QueryBuilder.Tests`. Expected: all pass on both targets.

---

### Task 3: Postgres dialect

**Files:**
- Create: `src/QueryBuilder.Infrastructure/Sql/PostgresDialect.cs`, `PostgresQuerySqlBuilder.cs`, `tests/QueryBuilder.Tests/Postgres/PostgresSqlBuilderTests.cs`
- Modify: `src/QueryBuilder.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `ISqlDialect`, `SqlQueryBuilder` from Task 1.
- Produces: `public sealed class PostgresQuerySqlBuilder() : SqlQueryBuilder(new PostgresDialect());`, registered as a second `IQuerySqlBuilder`.

- [ ] **Step 1: Write the failing tests.** Mirror every case in `SqlBuilderScopeTests.cs` with Postgres quoting (AC-4), plus these exact expectations:
```csharp
Assert.Contains("\nWHERE \"t0\".\"RegionCode\"::text = @p0", sql);                      // Text, Equals
Assert.Contains("\"t0\".\"RegionCode\"::text COLLATE \"default\" ILIKE @p0", sql);      // Text, Contains; value "%EU%"
Assert.Contains("\"t0\".\"RegionCode\"::text COLLATE \"default\" NOT ILIKE @p0", sql);  // Text, NotContains
Assert.Contains("\"t0\".\"Amount\" > @p0", sql);                                        // Number: no cast
Assert.Contains("\"t0\".\"Active\" IS TRUE", sql);
Assert.Contains("\"t0\".\"Active\" IS FALSE", sql);
Assert.Contains("(\"t0\".\"Tenant\"::text IN (@__scope0))", sql);                       // Text scope; never ILIKE
Assert.Contains("(\"t0\".\"ProgramId\" IN (@__scope0, @__scope1))", sql);               // Number scope; no cast
Assert.Contains("\"a\"\"b\"", sql);                                                     // embedded quote doubled
Assert.Equal("SELECT a\nFROM b\nLIMIT 11", new PostgresDialect().ApplyRowLimit("SELECT a\nFROM b", 11));
```
  Also: `Alias_of_64_bytes_is_refused` throws `CatalogValidationException` naming the alias; `Alias_of_63_bytes_is_accepted`; `Multibyte_alias_counts_bytes`: 32 `é` is refused and 31 accepted; `Is_null_has_no_cast`: `"t0"."RegionCode" IS NULL`; `Having_on_text_aggregate_casts_the_aggregate`: `MIN("t0"."RegionCode")::text = @p0`; `Factory_returns_postgres_builder_for_PostgreSql`.
- [ ] **Step 2: Run** `dotnet test tests/QueryBuilder.Tests --filter PostgresSqlBuilderTests`. Expected: fails to compile.
- [ ] **Step 3: Implement `PostgresDialect`.** `Comparable` appends `::text` for `ColumnDataType.Text` and `Unknown`, else returns its input. `ValidateAlias` uses `Encoding.UTF8.GetByteCount` against 63. Register the builder.
- [ ] **Step 4: Run** `dotnet test tests/QueryBuilder.Tests`. Expected: all pass, and `SqlServerGoldenSqlTests` is unchanged.

---

### Task 4: Value parser and chart checks

**Files:**
- Create: `src/QueryBuilder.Infrastructure/Execution/PostgresValueParser.cs`, `tests/QueryBuilder.Tests/Postgres/PostgresValueParserTests.cs`
- Modify: `src/QueryBuilder.Application/Visualizations/VisualizationDataChecks.cs`, `tests/QueryBuilder.Tests/RenderVisualizationCommandTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class PostgresValueParser
{
    public static ColumnDataType ColumnType(string dataTypeName);
    public static object? Parse(string dataTypeName, string text);
}
```

- [ ] **Step 1: Write the failing `[Theory]` tests with these exact cases.** Names are as Npgsql reports them.

| Type name | Text | Expected |
|---|---|---|
| `integer`, `smallint`, `bigint`, `oid` | `7` | `7L`, Number |
| `numeric(10, 2)` | `12.50` | `12.50m`, Number, and `ToString` gives `12.50` |
| `numeric` | `79228162514264337593543950335` | `decimal` |
| `numeric` | `79228162514264337593543950336` | same string |
| `numeric` | `0.1234567890123456789012345678` | `decimal` |
| `numeric` | `0.12345678901234567890123456789` | same string |
| `numeric` | `0.1234567890123456789012345678901` | same string |
| `numeric` | `0.00000000000000000000000000000000001` | same string |
| `numeric` | `1.0000000000000000000000000000000000` | same string |
| `numeric` | `NaN`, `Infinity`, `-Infinity` | `null` |
| `real`, `double precision` | `1.5` | `1.5d` |
| `double precision` | `NaN`, `Infinity`, `-Infinity` | `null` |
| `boolean` | `t`, `f` | `true`, `false` |
| `date` | `2026-01-15` | `DateTime(2026,1,15)`, Date |
| `date` | `infinity`, `-infinity`, `0044-03-15 BC`, `12000-01-01` | same string |
| `timestamp without time zone`, `timestamp(3) without time zone` | `2026-01-15 10:00:00.5` | `DateTime`, Kind Unspecified, DateTime |
| `timestamp with time zone`, `timestamp(3) with time zone` | `2026-01-15 10:00:00.1+00` | `DateTime`, Kind Utc |
| `timestamp with time zone` | `infinity`, `0044-03-15 00:00:00 BC` | same string |
| `uuid` | a Guid string | `Guid`, Guid |
| `character(5)` | `ab   ` | `ab`, Text |
| `character varying(50)`, `text`, `spike.mood`, `public.citext`, `money`, `interval`, `inet`, `integer[]`, `json`, `-.-` | any | same string, Text |

  Also `Parsing_ignores_host_culture`: repeat `12.50`, `1.5` and a date under `CultureInfo.CurrentCulture = de-DE`.
- [ ] **Step 2: Write a failing test in `RenderVisualizationCommandTests`:** `Funnel_refuses_huge_negative_numeric_text`: a measure cell `"-1000000000000000000000000000000000000000"` gives the existing "cannot show negative values" error. Also confirm the pie negative check takes the same path.
- [ ] **Step 3: Run** `dotnet test tests/QueryBuilder.Tests --filter "PostgresValueParserTests|Funnel_refuses_huge"`. Expected: fail.
- [ ] **Step 4: Implement.** Strip modifiers with `\(\d+(,\s*\d+)?\)` before matching. Numeric rule: `TryParse` with `NumberStyles.AllowLeadingSign | AllowDecimalPoint` and invariant culture, and keep the `decimal` only if `d.ToString(Invariant) == text`. Dates: `TryParseExact`, formats `yyyy-MM-dd`; `yyyy-MM-dd HH:mm:ss`, `yyyy-MM-dd HH:mm:ss.FFFFFF`; for `timestamptz`, the same with `z` and `AdjustToUniversal`. In `VisualizationDataChecks.TryToDecimal`, on overflow return the sign through a `double` parse, keeping `null` for non-numeric text.
- [ ] **Step 5: Run** `dotnet test tests/QueryBuilder.Tests`. Expected: all pass on both targets.

---

### Task 5: Catalog readers

**Files:**
- Create: `ICatalogReader.cs`, `SqlServerCatalogReader.cs`, `PostgresCatalogReader.cs`, `PostgresTypeMapper.cs`, `AmbiguousObjects.cs` in `src/QueryBuilder.Infrastructure/Catalog/`; `tests/QueryBuilder.Tests/Postgres/PostgresFactAttribute.cs`, `PostgresTestDatabase.cs`, `PostgresCatalogTests.cs`, `AmbiguousObjectsTests.cs`
- Modify: rename `SqlServerDataCatalogService.cs` to `DataCatalogService.cs`; `DependencyInjection.cs`; `QueryBuilder.Infrastructure.csproj`; `QueryBuilder.Editor.csproj`; `ParameterNameValidationTests.cs:30`

**Interfaces:**
- Consumes: `UnavailableObject`, `AdminUnavailableObject`, `IdentifierMatching` from Task 2.
- Produces:
```csharp
public interface ICatalogReader
{
    DataSourceProvider Provider { get; }
    Task<List<SchemaObjectMetadata>> ReadObjectsAsync(string connectionString, CancellationToken ct);
    // Key: "schema.name". Dictionary uses IdentifierMatching.Comparer(Provider).
    Task<Dictionary<string, List<ColumnMetadata>>> ReadColumnsAsync(string connectionString, CancellationToken ct);
}
public static class AmbiguousObjects
{
    public static (List<SchemaObjectMetadata> Usable, List<AdminUnavailableObject> Unavailable) Partition(IReadOnlyList<SchemaObjectMetadata> objects);
}
public static class PostgresTypeMapper { public static ColumnDataType Map(string baseTypeName, string typeType); }
[AttributeUsage(AttributeTargets.Method)] public sealed class PostgresFactAttribute : FactAttribute; // Skip set when QB_TEST_POSTGRES is unset
public sealed class PostgresTestDatabase : IAsyncLifetime // creates qb_it_<guid>, seeds, drops
```

Packages, conditional like EF Core, in both `Infrastructure` and `Editor`: `Npgsql` 9.0.5 on `net8.0`, 10.0.3 on `net10.0`. After editing, run `dotnet restore --force`, then `dotnet list package --vulnerable --include-transitive` and report any Npgsql finding.

Postgres columns query, proven in the spike apart from the added collation join. The objects query uses the same `WHERE` without the attribute and type joins:
```sql
SELECT n.nspname::text, c.relname::text, a.attname::text, a.attnum::int, a.attnotnull,
       base.typname::text, base.typtype::text,
       COALESCE(pk.is_pk, false), fk.ref_table::text, fk.ref_column::text,
       COALESCE(NOT coll.collisdeterministic, false)
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
JOIN pg_type t ON t.oid = a.atttypid
LEFT JOIN pg_collation coll ON coll.oid = a.attcollation
CROSS JOIN LATERAL (
    WITH RECURSIVE b AS (
        SELECT t.oid, t.typname, t.typtype, t.typbasetype
        UNION ALL
        SELECT bt.oid, bt.typname, bt.typtype, bt.typbasetype
        FROM pg_type bt JOIN b ON bt.oid = b.typbasetype WHERE b.typtype = 'd'
    )
    SELECT typname, typtype FROM b WHERE typtype <> 'd' LIMIT 1
) base
LEFT JOIN LATERAL (
    SELECT true AS is_pk FROM pg_constraint pc
    WHERE pc.conrelid = c.oid AND pc.contype = 'p' AND a.attnum = ANY (pc.conkey) LIMIT 1
) pk ON true
LEFT JOIN LATERAL (
    SELECT rc.relname AS ref_table, ra.attname AS ref_column
    FROM pg_constraint fc
    CROSS JOIN LATERAL unnest(fc.conkey, fc.confkey) AS k(col, refcol)
    JOIN pg_class rc ON rc.oid = fc.confrelid
    JOIN pg_attribute ra ON ra.attrelid = fc.confrelid AND ra.attnum = k.refcol
    WHERE fc.conrelid = c.oid AND fc.contype = 'f' AND k.col = a.attnum
    ORDER BY fc.conname LIMIT 1
) fk ON true
WHERE c.relkind IN ('r','p','f','v','m')
  AND NOT c.relispartition
  AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
  AND has_schema_privilege(n.oid, 'USAGE')
  AND has_table_privilege(c.oid, 'SELECT')
ORDER BY n.nspname, c.relname, a.attnum
```

`PostgresTestDatabase` seed, connecting a restricted role `qb_it_reader` for catalog reads: schema `qb` with enum `mood`; nested domains over `numeric(10,2)`; nondeterministic collation `ci` (ICU `und-u-ks-level2`); table `t` with a PK, an FK to `parent`, a dropped column and one column of each mapped type; a partitioned table with a child; view; materialized view; `"MixedMv"`; foreign table via `file_fdw`; ungranted `secret`; column-grant-only `colgrant`; `public."Orders"` and `public.orders`; `"a.b".c` and `a."b.c"`; a view with a `COLLATE ci` column.

- [ ] **Step 1: Write failing tests with no database.** `AmbiguousObjectsTests`: `Case_only_pair_is_unavailable_with_partner_names`; `Dot_clash_is_unavailable`; `Unique_names_are_usable`; `Three_way_clash_lists_two_partners_each`. `PostgresTypeMapper` theory, per SPEC 4.6 table: `int2`,`int4`,`int8`,`numeric`,`float4`,`float8` Number; `date` Date; `timestamp`,`timestamptz` DateTime; `bool` Boolean; `uuid` Guid; `text`,`varchar`,`bpchar`,`name`,`citext` Text; any name with `typtype` `e` Text; `money`,`time`,`interval`,`json`,`_int4` Unknown.
- [ ] **Step 2: Write failing `[PostgresFact]` integration tests in `PostgresCatalogTests`** through `DataCatalogService`, for AC-9: kinds; partition child, `secret`, `colgrant` and system schemas absent; nested domain maps to Number; PK and FK present; dropped column absent; `HasNondeterministicCollation` true on the plain, domain and view `COLLATE` columns and false otherwise; `public.Orders`, `public.orders` and both dot-clash objects absent from `Schemas` and present in `UnavailableObjects`; `MixedMv` readable; an allowlist excluding `public.Orders` removes it from `UnavailableObjects` but not from `GetUnavailableObjectsAsync`.
- [ ] **Step 3: Run** `dotnet test tests/QueryBuilder.Tests`. Expected: new tests fail to compile, or `[PostgresFact]` tests show as skipped.
- [ ] **Step 4: Implement.** Split today's service: move `sys.*` queries and system-schema exclusion to `SqlServerCatalogReader`, unchanged. `DataCatalogService(IDataSourceRepository, IConnectionStringResolver, IEnumerable<ICatalogReader>, IMemoryCache, ILogger<DataCatalogService>)`. On Postgres, partition **before** `AllowedSchemas`, `CatalogScope` and `AllowedObjects`, and log one warning naming each clash. Those filters then apply to both lists, with the provider comparer. On SQL Server, skip partitioning. `ValidateAsync` uses the provider comparer and removes `ToLowerInvariant` from keys on Postgres while leaving SQL Server's behavior as is. `GetRawObjectsAsync` returns usable objects only, and `GetUnavailableObjectsAsync` returns the admin list unfiltered. `PostgresCatalogReader` wraps connection failures as `QueryExecutionException("Couldn't connect to this data source.")`, with details only in the log. Unknown provider: `CatalogValidationException` naming the provider.
- [ ] **Step 5: Run** `dotnet test tests/QueryBuilder.Tests`. Expected: all non-database tests pass on both targets; `[PostgresFact]` skipped, and the reviewer runs them with `QB_TEST_POSTGRES`.

---

### Task 6: Postgres execution, routing and sharing

**Files:**
- Create: `src/QueryBuilder.Infrastructure/Execution/QueryParameterValues.cs`, `PostgresQueryExecutionService.cs`, `RoutingQueryExecutionService.cs`; `tests/QueryBuilder.Tests/Postgres/PostgresExecutionTests.cs`, `tests/QueryBuilder.Tests/RoutingQueryExecutionServiceTests.cs`
- Modify: `SqlServerQueryExecutionService.cs`, `AppUsersDirectory.cs`, `DependencyInjection.cs`

**Interfaces:**
- Consumes: `PostgresValueParser` from Task 4, `PostgresDialect` from Task 3, `PostgresTestDatabase` from Task 5.
- Produces:
```csharp
public static class QueryParameterValues
{
    public static object Resolve(GeneratedQueryParameter parameter, IReadOnlyDictionary<string, string?> runtimeValues); // moved unchanged from SqlServer service
}
public sealed class RoutingQueryExecutionService(SqlServerQueryExecutionService sqlServer, PostgresQueryExecutionService postgres) : IQueryExecutionService;
```

Exact session statement, on a transaction, before the query:
```sql
SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = 60000; SET LOCAL DateStyle = 'ISO, YMD'; SET LOCAL TimeZone = 'UTC'; SET LOCAL IntervalStyle = 'postgres'; SET LOCAL extra_float_digits = 3
```
Query command: `AllResultTypesAreUnknown = true`, `CommandTimeout = 65`. Never commit the transaction; dispose rolls back.

Exact user messages:

| Failure | `QueryExecutionException` message |
|---|---|
| `PostgresException` 57014, or `NpgsqlException` with inner `TimeoutException` after connecting | `The query took too long and was stopped.` |
| `PostgresException` 25006 | `This query tried to change data, which isn't allowed.` |
| Other `PostgresException` | `The database rejected the query: {MessageText}` |
| Any failure opening the connection, including a malformed string | `Couldn't connect to this data source.`; details logged only |
| `OperationCanceledException` | Passes through |
| `Provider` other than SqlServer or PostgreSql | `CatalogValidationException`: `This data source's provider '{provider}' isn't supported.` |

- [ ] **Step 1: Write a failing test with no database:** `Routing_refuses_mysql_with_clear_message`. Routing to each real service is covered by Task 6's integration tests and Task 10.
- [ ] **Step 2: Write failing `[PostgresFact]` tests** in `PostgresExecutionTests`, as `qb_it_reader`: AC-5 a view whose function writes gives the 25006 message and adds no row; AC-11 a result with every SPEC 4.5 type, including NaN, Infinity, 40-digit numeric, `infinity`, BC, year 12000, `interval` with months, `inet`, serialized with `QueryBuilderJson`-equivalent options and exported by `ExportService` to CSV and Excel with no exception, and `infinity` never `9999-12-31`; AC-12 an enum created after the pool has opened reads as text; AC-17 `pg_sleep` past a lowered timeout gives the timeout message and `pg_stat_activity` shows it gone, and a cancelled token throws `OperationCanceledException` with it gone; AC-20 a wrong connection string gives the message and neither host nor password in it; truncation returns N rows with `Truncated` true from N+1; `Same_date_type_on_both_targets`: `date` cell is `DateTime`; AC-3b a scoped definition on a nondeterministic column never reaches SQL, and on a plain `text` column `acme` doesn't match `ACME`; AC-6 values with quotes stay bound; AC-10 and AC-10a every offered operator per type, plus enum, `citext`, `char(n)`, `json`, `time`, and Contains on the nondeterministic column; AC-19 `public.app_users` with an integer `id` returns the user, and its absence gives `Available = false`.
- [ ] **Step 3: Run** `dotnet test tests/QueryBuilder.Tests --filter Routing`. Expected: fail.
- [ ] **Step 4: Implement.** Register both services as scoped and `IQueryExecutionService` as the router. `AppUsersDirectory` branches on `dataSource.Provider`: Postgres runs `SELECT id::text, display_name, email FROM public.app_users ORDER BY display_name LIMIT 500`, in the same read-only session form. SQL Server's path is unchanged.
- [ ] **Step 5: Run** `dotnet test tests/QueryBuilder.Tests`. Expected: all non-database tests pass on both targets.

---

### Task 7: Unique violation becomes a 400

**Files:**
- Modify: `src/QueryBuilder.Infrastructure/Repositories/DataScopeRuleRepository.cs`
- Create: `tests/QueryBuilder.Tests/MetadataCollationTests.cs`

**Interfaces:**
- Produces: `[MetadataSqlFact]`, skipped unless `QB_TEST_METADATA_SQLSERVER` is set. Each test creates and drops a throwaway database using `Database.MigrateAsync`.

- [ ] **Step 1: Write failing tests:** `Saving_case_only_pair_returns_validation_error_and_keeps_existing_rules`: seed one rule, replace with `public.Orders` and `public.orders` under one key, expect `CatalogValidationException` containing `public.orders`, and the seeded rule is still there. `Mixed_case_rule_round_trips_exactly`: `public.Orders` reloads with that case.
- [ ] **Step 2: Implement:** catch `DbUpdateException` whose inner `SqlException.Number` is 2601 or 2627 in `ReplaceForDataSourceAsync`, and throw `CatalogValidationException($"Couldn't save: the metadata database treats two object names as the same. {firstLineOfInnerMessage}")`. The surrounding transaction already rolls back on dispose.
- [ ] **Step 3: Run** `dotnet test tests/QueryBuilder.Tests`. Expected: non-database tests pass, and `[MetadataSqlFact]` tests are skipped.

---

### Task 8: Client

**Files:**
- Create: `client/src/features/catalog/catalog-view.ts`, `client/src/features/catalog/unavailable.ts`, `client/tests/catalog-view.test.ts`
- Modify: `client/src/types/index.ts`, `client/src/features/catalog/CatalogSidebar.tsx`, `client/src/pages/AdminDataSourcePolicyPage.tsx`, `client/src/features/admin/DataScopeSection.tsx`, `client/src/pages/AdminDataSourcesPage.tsx`, `client/src/features/builder/ShareDialog.tsx`, `client/src/features/dashboards/ShareDashboardDialog.tsx`
- Rebuild: `src/QueryBuilder.Editor/wwwroot/` per `CLAUDE.md` release step 2.

**Interfaces:**
- Consumes: JSON fields from Task 2: `provider`, `unavailableObjects`, `unavailable`, `reason`, `clashesWith`.
- Produces:
```ts
export interface CatalogViewSection { name: string; objects: SchemaObjectMetadata[]; unavailable: UnavailableObject[] }
export function buildCatalogView(catalog: DataSourceCatalog, term: string): CatalogViewSection[]
export function countLabel(catalog: DataSourceCatalog): string // "12 objects · 2 unavailable"
export const BUSINESS_UNAVAILABLE_REASON: string
export function adminUnavailableReason(clashesWith: string[]): string
export function providerLabel(provider: DataSourceProvider): string // "SQL Server", "PostgreSQL", "MySQL", "SQLite"
```

Exact copy:
- Business: `Can't be used: its full name clashes with another table or view. Ask your admin to add a view with a unique name.`
- Admin: `Clashes with public.orders. QueryBuilder can't tell these names apart. Add a uniquely named view over the one you want to expose.` Several partners are comma-separated.
- Share dialogs: name both views: `dbo.AppUsers` on SQL Server and `public.app_users` on PostgreSQL.

- [ ] **Step 1: Write failing tests in `client/tests/catalog-view.test.ts`:** a schema with only unavailable objects gets a section; search matches an unavailable object by name; search by a column name doesn't crash on rows with no columns; sections stay sorted by name; `countLabel` is `1 object` with none unavailable, `12 objects · 2 unavailable` with two; `countLabel` with a missing `unavailableObjects` field, as from an older cached response, doesn't throw; `adminUnavailableReason` wording.
- [ ] **Step 2: Run** `cd client && npm test`. Expected: fail.
- [ ] **Step 3: Implement.** Sidebar rows: not a button, no drag, no expand; muted text with a warning icon and the reason as visible text; `min-w-0` on text. `expandAll` and `totalCount` stay on usable objects. Policy page: unavailable rows after the checklist, disabled checkbox reflecting `allowedObjects`, the admin reason; `handleScopeChange`'s `kindByKey` includes unavailable objects, so pruning doesn't drop their entries. `DataScopeSection`: unavailable rows with the reason and no state or mapping controls, left out of `effectiveAll`, so saves don't send them. `AdminDataSourcesPage:47` uses `providerLabel`.
- [ ] **Step 4: Run** `cd client && npm test && npm run build`, then copy the bundle into `wwwroot` per `CLAUDE.md`. Expected: all pass, including `insecure-context.test.ts`.

---

### Task 9: Documentation

**Files:**
- Modify: `README.md`, `src/QueryBuilder.Editor/README.md`, `CLAUDE.md`, `docs/redash-parity/GAP-MATRIX.md`

- [ ] **Step 1: Add a "PostgreSQL data sources" section to both READMEs:** registration, with an insert checked against `AppDbContextModelSnapshot.cs`; Npgsql connection string; every limit in SPEC section 8; `public.app_users`; that the metadata store remains SQL Server; recommended read-only login. Replace the `ViewsOnly` example at `src/QueryBuilder.Editor/README.md:87,93`. Update "What's included" and "Roadmap".
- [ ] **Step 2: Update `CLAUDE.md`:** Layout; the release workflow gains the required step from SPEC section 7, with `QB_TEST_POSTGRES` and `QB_TEST_METADATA_SQLSERVER`, and unset counts as not verified; Known sharp edges gains text-mode reads, session pinning, `::text` and collations, and clash handling; Feature status. Don't state a version as released.
- [ ] **Step 3: Verify** `git diff --stat` shows no `.sql` script or migration changes, and every limit in SPEC section 8 appears in both READMEs.

---

### Task 10: Acceptance (reviewer, on Opus, not a subagent)

- [ ] **Step 1:** Describe to the user, then start, a temporary `postgres:16` on a random loopback port, and a throwaway LocalDB database for `QB_TEST_METADATA_SQLSERVER`.
- [ ] **Step 2:** Run `dotnet test tests/QueryBuilder.Tests` with both variables set. Expected: no skips, on `net8.0` and `net10.0`. Run `cd client && npm test`.
- [ ] **Step 3:** Pack to a scratch folder with `-p:Version=1.2.0-test`, not the real version, for a throwaway multi-target host using SQL Server metadata and one SQL Server and one Postgres source. Run both targets (AC-21).
- [ ] **Step 4:** Playwright against that host, with personas as in `CLAUDE.md`: AC-1 and AC-1a to AC-1f in the sidebar and admin pages in both themes; AC-14 totals, HAVING, limits; AC-15 all 13 chart types plus a dashboard mixing both sources; AC-16 parameters; AC-11b exact grid value with a plotted chart; AC-18 alias messages; AC-19 sharing; Review Focus item 5 on a saved chart and tile; AC-8 SQL Server flows on the same host.
- [ ] **Step 5:** Write `docs/redash-parity/features/postgresql/VERIFICATION.md`, mapping every AC to its evidence, with anything unverified stated plainly, including PgBouncer. Remove the containers, databases and scratch host. Confirm `Program.cs` carries no temporary persona block.
- [ ] **Step 6:** Report to the user. Bumping, packing, committing and pushing each wait for their own ask.
