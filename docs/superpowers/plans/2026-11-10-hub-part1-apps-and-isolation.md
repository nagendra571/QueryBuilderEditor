# Hub Part 1 — Apps and isolation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every app-owned row in the metadata database belongs to exactly one app (tenant), and EF Core can only read
or write the current app's rows; embedded mode keeps working exactly as today, as the single "Default" app.

**Architecture:** A new `App` entity and an `AppId` on every app-owned entity. `AppDbContext` receives an
`IAppContext` (current app id, or hub-wide) and applies a global query filter plus a `SaveChanges` guard. Embedded
mode always resolves to the well-known Default app; hub mode (option exists, no launch yet) resolves to a sentinel that
matches nothing until Part 2 supplies sessions. The metadata database records which mode owns it.

**Tech Stack:** .NET 8/10, EF Core 9/10 (SQL Server metadata), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-10-multi-app-hub-design.md` (§7 isolation and data model, §2 anti-goals,
"Settled after the builder check").

**Parts that follow (separate plans, written when this part is accepted):** 2 launch and sessions + super admin
sign-in, 3 roles, publishing, sharing, data-source lock and the super admin console, 4 `QueryBuilder.Hub` host and
`QueryBuilder.Launch` package. Nothing is released until all four are done (2.0.0).

## Global Constraints

- Embedded mode (hub off, the default): behaviour, endpoints and JSON unchanged; all 1026 existing server tests and
  175 client tests still pass unmodified, except where a test constructs entities and now also sees `AppId`.
- Cross-app reads return nothing (repositories return `null` → handlers' existing NotFound → **404**, never 403).
- `App.DefaultId` = `00000000-0000-0000-0000-00000000000d`; `App.NoneId` = `00000000-0000-0000-0000-000000000000`
  (`Guid.Empty`, never a row; hub mode without an app resolves to it and sees nothing).
- Metadata store stays SQL Server; no new packages.
- Migrations generated with `dotnet tool run dotnet-ef migrations add <Name> --project src/QueryBuilder.Infrastructure --startup-project src/QueryBuilder.Api --framework net10.0 --output-dir Persistence/Migrations`; any raw SQL naming columns goes in `EXEC(N'…')`.
- CLAUDE.md conventions: hand-rolled mediator, `QueryBuilderJson.Options`, add child entities through their `DbSet`,
  why-only comments.
- **No commits or pushes unless the user asks.** Work on a new local branch `feature/hub` from `feature/rea1`. Each
  task ends with a diff check instead of a commit.
- Before calling Part 1 done: `dotnet test tests/QueryBuilder.Tests` on net8.0 and net10.0 with `QB_TEST_POSTGRES`
  and `QB_TEST_METADATA_SQLSERVER` set, 0 skipped.

## Review Focus

1. **The catalog cache is keyed by data-source id only** (`DataCatalogService.GetCatalogAsync`, `catalog:{id}`): a
   guessed id from another app would be served from cache without touching the filtered DB → test in Task 4.
2. **Code paths with no HTTP request** (hosted services, seeders, `/_setup` diagnostics) in hub mode must see nothing
   unless they explicitly choose an app or hub-wide → test in Task 5.
3. **Upgrading a database that already has data**: every existing row must land in Default, with nothing orphaned,
   and the idempotent script must run twice cleanly → tests in Task 3.
4. **A reference to another app's id inside an otherwise valid write** (a chart tile pointing at App B's
   visualization, a scope rule for App B's data source): the filtered lookup must fail as NotFound → test in Task 4.
5. **Tests and tools that build `new AppDbContext(options)` directly** (existing metadata tests, design-time
   migrations) must keep working as Default → covered by the existing suite plus Task 2's constructor test.

---

### Task 1: Domain — `App`, `IAppOwned`, `IAppContext`

**Files:**
- Create: `src/QueryBuilder.Domain/Entities/App.cs`
- Create: `src/QueryBuilder.Domain/Entities/IAppOwned.cs`
- Create: `src/QueryBuilder.Application/Abstractions/IAppContext.cs`
- Modify: `src/QueryBuilder.Domain/Entities/DataSource.cs`, `SavedQuery.cs`, `QueryShare.cs`, `QueryVisualization.cs`,
  `Dashboard.cs` (Dashboard, DashboardTile, DashboardShare), `DataScopeRule.cs`, `AuditLogEntry.cs`
- Test: `tests/QueryBuilder.Tests/Hub/AppModelTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class App { Guid Id; string Key; string Name; bool IsEnabled = true; DateTimeOffset CreatedAtUtc; const/static readonly Guid DefaultId; static readonly Guid NoneId; const string DefaultKey = "default"; }`
    (`Key` ≤ 64, lowercase letters/digits/hyphen; `Name` ≤ 200.)
  - `public interface IAppOwned { Guid AppId { get; set; } }` — implemented by DataSource, SavedQuery, QueryShare,
    QueryVisualization, Dashboard, DashboardTile, DashboardShare, DataScopeRule.
  - `AuditLogEntry.AppId` is `Guid?` (null = hub-level entry) and does **not** implement `IAppOwned`.
  - `public interface IAppContext { Guid AppId { get; } bool IsHubWide { get; } }` — when `IsHubWide` is true
    `AppId` is meaningless and filters are off.

- [ ] **Step 1: Write the failing test** `Every_metadata_entity_is_app_owned_except_the_known_exceptions`: reflect over
  `QueryBuilder.Domain.Entities` types that are concrete classes with an `Id`; assert each implements `IAppOwned`,
  except exactly `{ App, AuditLogEntry }`. Also `Default_and_none_ids_differ_and_none_is_empty`.
- [ ] **Step 2: Run** `dotnet test tests/QueryBuilder.Tests --filter AppModelTests` → FAIL (types missing).
- [ ] **Step 3: Add the types and `AppId` properties** (default value `Guid.Empty` on entities, so the stamp in Task 2
  decides it).
- [ ] **Step 4: Run** the filter again → PASS; `dotnet build` clean on both targets.
- [ ] **Step 5: Diff check** `git diff --stat` shows only the files above.

### Task 2: Persistence — filter, save guard, configurations

**Files:**
- Modify: `src/QueryBuilder.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/QueryBuilder.Infrastructure/Persistence/Configurations/AppConfiguration.cs`
- Modify: every file in `Persistence/Configurations/` for the `AppId` column, FK and indexes
- Create: `src/QueryBuilder.Application/Exceptions/CrossAppWriteException.cs`
- Modify: `src/QueryBuilder.Infrastructure/DependencyInjection.cs` (context gets `IAppContext` from DI)
- Test: `tests/QueryBuilder.Tests/Hub/AppDbContextFilterTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces:
  - `AppDbContext(DbContextOptions<AppDbContext> options, IAppContext appContext)` plus the existing
    `AppDbContext(DbContextOptions<AppDbContext> options)` which uses a fixed Default context (keeps existing tests and
    design-time tooling working).
  - `public DbSet<App> Apps`.
  - Filter on every `IAppOwned` entity: `e => IsHubWide || e.AppId == CurrentAppId` (context properties so EF
    re-evaluates per instance); audit: `e => IsHubWide || e.AppId == CurrentAppId` on the nullable column.
    Apply via one generic helper over `IAppOwned` types found in the model, so a new entity can't be forgotten.
  - `SaveChanges`/`SaveChangesAsync` override: for `Added` app-owned entries with `AppId == Guid.Empty` stamp
    `CurrentAppId` (hub-wide context: throw `CrossAppWriteException("A hub-wide write must name its app.")`);
    for `Added`/`Modified`/`Deleted` entries with `AppId != CurrentAppId` and not hub-wide → throw
    `CrossAppWriteException`; an `Added` audit entry with null `AppId` in a non-hub-wide context gets `CurrentAppId`.
    Never write while `CurrentAppId == App.NoneId` and not hub-wide → throw.
  - `CrossAppWriteException : Exception` — mapped by `GlobalExceptionHandler` to **404** (Task 4 step).
  - Configurations: `AppId` required (audit optional), FK → `Apps(Id)` `OnDelete(Restrict)`; indexes
    `(AppId)` on DataSources, `(AppId, OwnerId)` on SavedQueries and Dashboards, `(AppId, TimestampUtc)` on audit.
    `Apps`: unique index on `Key`.

- [ ] **Step 1: Write the failing tests** (in-memory SQLite is **not** used in this repo; use the EF model only, no DB):
  - `Every_app_owned_entity_has_a_query_filter` — `new AppDbContext(options).Model` (SQL Server provider, never
    opened): every entity type whose CLR type implements `IAppOwned`, plus `AuditLogEntry`, has a non-null query filter.
  - `Legacy_constructor_uses_the_default_app` — context built with the one-arg constructor reports
    `CurrentAppId == App.DefaultId` and `IsHubWide == false` (expose both as `internal` properties;
    `InternalsVisibleTo` the test project if not already).
- [ ] **Step 2: Run** `dotnet test --filter AppDbContextFilterTests` → FAIL.
- [ ] **Step 3: Implement** the constructor, `Apps` set, filter helper, save guard and configurations above.
- [ ] **Step 4: Run** the filter → PASS; then the whole suite on net10.0 → the existing tests still pass (those that
  build `new AppDbContext(options)` run as Default).
- [ ] **Step 5: Diff check.**

### Task 3: Migration `AddApps` and the mode marker table

**Files:**
- Create: `src/QueryBuilder.Infrastructure/Persistence/Migrations/<timestamp>_AddApps.cs` (+ Designer, snapshot)
- Create: `src/QueryBuilder.Domain/Entities/MetadataSetting.cs` + `Configurations/MetadataSettingConfiguration.cs`
  (`Key nvarchar(64)` PK, `Value nvarchar(400)`; not app-owned → add to Task 1's exception list)
- Test: `tests/QueryBuilder.Tests/Hub/AddAppsMigrationTests.cs`

**Interfaces:**
- Produces: tables `Apps`, `MetadataSettings`; a seeded `Apps` row `(DefaultId, 'default', 'Default', IsEnabled 1)`;
  `AppId` columns on all app-owned tables with default `DefaultId` so existing rows land in Default; the default
  constraint is then dropped so new rows must carry an app (the save stamp supplies it).

- [ ] **Step 1: Generate** the migration with the command in Global Constraints; edit it so: create `Apps` and insert
  the Default row **before** adding the `AppId` columns (`defaultValue: App.DefaultId`), then add FKs/indexes, then drop
  the column defaults. Any raw SQL in `EXEC(N'…')`.
- [ ] **Step 2: Write the failing tests** (`[MetadataSqlFact]`, reuse the `WithDatabaseAsync` pattern from
  `DataSourceManagementMetadataTests`; move it to a shared `tests/QueryBuilder.Tests/Hub/MetadataDb.cs` helper):
  - `Fresh_database_has_only_the_default_app` — after `MigrateAsync`, `Apps` has exactly one row, id `DefaultId`,
    key `default`.
  - `Upgrade_puts_existing_rows_in_the_default_app` — migrate to `AddDataSourceScopeKeys` (the previous migration)
    via `IMigrator.MigrateAsync("20261110023605_AddDataSourceScopeKeys")`, insert one row in each app-owned table
    with raw SQL, migrate to latest, assert every row's `AppId == DefaultId` and `COUNT(*)` unchanged.
  - `New_rows_without_an_app_are_refused_by_the_database` — raw `INSERT` into `SavedQueries` without `AppId` fails
    (no default remains).
- [ ] **Step 3: Run** with `QB_TEST_METADATA_SQLSERVER` set → PASS (not skipped).
- [ ] **Step 4: Idempotent script check:** generate `migrations script --idempotent` from a temp copy of `src`, apply
  it to a throwaway LocalDB database **twice** with `sqlcmd -I -b` from PowerShell, then once more to a database
  already at 1.5.0 with sample rows; all succeed; drop the databases. (The packaged DBA script is regenerated only at
  release time.)
- [ ] **Step 5: Diff check.**

### Task 4: Cross-app isolation at the data layer

**Files:**
- Modify: `src/QueryBuilder.Infrastructure/Catalog/DataCatalogService.cs` (gate before cache)
- Modify: `src/QueryBuilder.Editor/Middleware/GlobalExceptionHandler.cs` (or wherever exceptions map) — `CrossAppWriteException` → 404
- Test: `tests/QueryBuilder.Tests/Hub/CrossAppIsolationTests.cs`, `tests/QueryBuilder.Tests/Hub/CatalogCacheIsolationTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3; a test-only `FixedAppContext(Guid appId, bool hubWide = false) : IAppContext` in the test
  project.
- Produces: `DataCatalogService.GetCatalogAsync`, `ValidateAsync`, `GetRawObjectsAsync`,
  `GetUnavailableObjectsAsync` first confirm the data source is visible through the filtered `AppDbContext`
  (`db.DataSources.AnyAsync(d => d.Id == id)`); not visible → the same NotFound the method throws today for an
  unknown id. The cache key stays `catalog:{id}` (ids are globally unique; the gate is what isolates).

- [ ] **Step 1: Write the failing tests** (`[MetadataSqlFact]`, real repositories over two contexts on one database;
  seed App A = a second `Apps` row and App B = Default, each with one data source, query (+share), visualization,
  dashboard (+tile, +share), scope rule, audit entry):
  - `App_b_cannot_load_app_a_rows_by_id` — for each repository's get-by-id method (`DataSourceRepository`,
    `SavedQueryRepository`, `VisualizationRepository`, `DashboardRepository`, `DataScopeRuleRepository`), App B
    context with App A's id → `null`.
  - `App_b_lists_contain_none_of_app_a` — every list method used by the list endpoints returns only App B rows.
  - `Writing_an_app_a_entity_from_app_b_is_refused` — attach App A's dashboard to an App B context, change its name,
    `SaveChangesAsync` → `CrossAppWriteException`; DB unchanged.
  - `A_chart_tile_pointing_at_another_apps_visualization_is_not_found` — run the real add-chart-tile handler
    (`DashboardCommands`) wired to App A repositories with App B's visualization id → `NotFoundException`; no tile row.
  - `Hub_wide_context_sees_both_apps_and_must_name_the_app_on_insert` — hub-wide lists include both; inserting a
    `SavedQuery` with `AppId == Guid.Empty` throws.
  - `None_context_sees_nothing_and_cannot_write` — `FixedAppContext(App.NoneId)`: every list empty, any insert throws.
  - (`CatalogCacheIsolationTests`, unit, no DB) `A_cached_catalog_is_not_served_to_another_app` — pre-seed
    `IMemoryCache` with `catalog:{idA}` (existing pattern), context = App B → `GetCatalogAsync(idA)` throws the
    not-found exception; with App A context → returns the cached catalog.
  - `Cross_app_write_maps_to_404` — the exception handler returns 404 with the generic not-found problem body.
- [ ] **Step 2: Run** → FAIL on the catalog and 404 tests (filters from Task 2 should already make most pass —
  any that fail point to a repository that bypasses the context, e.g. raw SQL or `IgnoreQueryFilters`; fix those).
- [ ] **Step 3: Implement** the catalog gate and the exception mapping.
- [ ] **Step 4: Run** the Hub tests and the full suite on net10.0 → PASS.
- [ ] **Step 5: Diff check;** also `grep -rn "IgnoreQueryFilters\|FromSql\|ExecuteSql" src` and confirm each hit is
  either absent or justified in a why-comment (none expected in Part 1).

### Task 5: App context resolution, hub option, mode guard

**Files:**
- Create: `src/QueryBuilder.Editor/Identity/AppContextAccessor.cs`
- Create: `src/QueryBuilder.Editor/HubOptions.cs`
- Modify: `src/QueryBuilder.Editor/QueryBuilderEditorOptions.cs` (`public HubOptions Hub { get; } = new();`)
- Modify: `src/QueryBuilder.Editor/ServiceCollectionExtensions.cs` (register `AppContextAccessor` scoped as
  `IAppContext`; pass mode to Infrastructure)
- Create: `src/QueryBuilder.Infrastructure/Persistence/MetadataModeGuard.cs` (hosted service)
- Modify: `src/QueryBuilder.Infrastructure/DependencyInjection.cs` (register the guard after migrations, before the seeder; `AddInfrastructure(..., bool hubMode = false)`)
- Modify: `src/QueryBuilder.Infrastructure/Persistence/DefaultDataSourceSeeder.cs`, `src/QueryBuilder.Api/Seed/DemoDataSeederHostedService.cs`, `src/QueryBuilder.Editor/Diagnostics/SetupDiagnosticsRunner.cs` (choose an app explicitly)
- Test: `tests/QueryBuilder.Tests/Hub/AppContextTests.cs`, `tests/QueryBuilder.Tests/Hub/MetadataModeGuardTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `public sealed class HubOptions { public bool Enabled { get; set; } }` (more settings arrive in Part 2).
  - `public sealed class AppContextAccessor(QueryBuilderEditorOptions options) : IAppContext` with
    `void UseApp(Guid appId)` and `void UseHubWide()` for explicit background use. Without an explicit choice:
    embedded → `App.DefaultId`; hub → `App.NoneId`. (Part 2 adds resolution from the session.)
  - `MetadataModeGuard`: reads `MetadataSettings['Mode']`; missing → writes `Embedded` or `Hub`; present and different
    → throws `InvalidOperationException("This QueryBuilder metadata database belongs to a <stored>-mode installation; this app is configured for <current> mode. Use a separate metadata database for the hub.")`.
    Runs whether or not migrations are applied by the app.
  - Seeder: embedded → `UseApp(App.DefaultId)` (unchanged behaviour); hub → does nothing (the hub's Default has no
    config-defined source unless one is configured, and it is then seeded into Default with `UseApp(DefaultId)`).
    Demo seeder and `/_setup` diagnostics: `UseApp(App.DefaultId)`.

- [ ] **Step 1: Write the failing tests:**
  - `Embedded_without_a_request_resolves_to_default` / `Hub_without_a_request_resolves_to_none` /
    `Explicit_choice_wins` (unit, `AppContextAccessor` with options).
  - `[MetadataSqlFact] First_run_records_the_mode`, `Embedded_refuses_a_hub_database`,
    `Hub_refuses_an_embedded_database` (message names both modes), `Same_mode_starts_again`.
  - `[MetadataSqlFact] Embedded_seeder_still_creates_the_default_source_in_the_default_app` — existing seeder
    behaviour, now asserting `AppId == DefaultId`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** as above.
- [ ] **Step 4: Run** the Hub tests and the full suite → PASS.
- [ ] **Step 5: Diff check.**

### Task 6: Acceptance for Part 1

- [ ] **Step 1:** Full server suite on **net8.0 and net10.0** with `QB_TEST_POSTGRES` (throwaway `postgres:16`
  container per CLAUDE.md, `qb_it_reader` trust line) and `QB_TEST_METADATA_SQLSERVER` (LocalDB) → all pass,
  0 skipped. `cd client && npm test` → 175 pass (no client change expected).
- [ ] **Step 2:** Real embedded run on a copy, never the original: `BACKUP` `QueryBuilderAppDb` and `RESTORE` it as
  the throwaway database `qb_part1_check`, point the sample app at it, start it
  (`dotnet run --project src/QueryBuilder.Api --no-launch-profile --urls http://localhost:5080` with the
  connection string overridden by environment variable), and with Playwright: list queries, open and run
  "Enrolments by program", open dashboard "Program overview", open `/admin` data source page → all as before;
  database now has `MetadataSettings.Mode = Embedded` and every row in Default.
- [ ] **Step 3:** Start the same app with `Hub.Enabled = true` against that database → refuses to start with the mode
  message. Drop `qb_part1_check`; stop stray `dotnet` processes.
- [ ] **Step 4:** Opus review of the whole Part 1 diff against the spec's §7 and Review Focus; fix findings.
- [ ] **Step 5:** Update CLAUDE.md "Where we left off" with Part 1's state. Report to the user; commit only if asked.
