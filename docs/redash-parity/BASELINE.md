# QueryBuilderEditor — Baseline (current state)

Snapshot studied: branch `feature/rea1` at `bca3dcb` (identical to local `main`; `main` is 1 commit
ahead of `origin/main`). Working tree at study time: `CLAUDE.md` modified (uncommitted working
notes), untracked `.playwright-mcp/`, `Sample-AppUsers.sql`, `Sample-DataScope.sql`. No product code
changed during this study. Package version `QueryBuilder.Editor` **1.0.13**.

Evidence labels: **Observation** = read directly from source/test/command output (cited
`file:line`, paths relative to the repo root unless noted). **Inference** = reasoned, with
confidence. **Unknown** = see [UNKNOWNS.md](UNKNOWNS.md). Evidence IDs (`E-…`) are indexed in
[EVIDENCE.md](EVIDENCE.md).

## 1. Toolchain and dependencies (Observation, E-Q01, E-Q02)

| Area | Version (from manifests / tools) |
|---|---|
| Target framework | `net8.0` (all `src/*` and `tests/*` projects) |
| SDKs installed | .NET 10.0.114, 9.0.304, 9.0.320 |
| EF Core | `Microsoft.EntityFrameworkCore.SqlServer` 9.0.9, `dotnet-ef` 9.0.9 pinned in `dotnet-tools.json` |
| SQL client | `Microsoft.Data.SqlClient` 7.1.0 |
| Validation | `FluentValidation` 12.1.1 |
| Export | `ClosedXML` 0.105.1 (xlsx), `CsvHelper` 33.1.0 |
| Frontend | React 19.2, TypeScript ~6.0, Vite 8, Tailwind 4.3, Radix/shadcn, TanStack Query 5, Zustand 5, Monaco |
| Charts | **None installed** (no charting package in `client/package.json`) |
| Tests | xUnit 2.9.3 — `dotnet test tests/QueryBuilder.Tests` → **75 passed, 0 failed** (E-Q03) |
| Node / npm | 24.22.2 / 11.0.0 |

### License audit — no paid/commercial-license libraries (Observation, E-Q02)

The user asked that nothing requiring a paid license is used.

- **NuGet, including transitive packages:** 108 packages from every `obj/project.assets.json`,
  licenses read from each package's cached `.nuspec`. Results: MIT 86, Apache-2.0 19,
  "MS-PL OR Apache-2.0" 1 (CsvHelper), plus two non-SPDX entries:
  - `Microsoft.Data.SqlClient.SNI.runtime` 7.1.0 — Microsoft Software License Terms for the SNI
    library (free, redistributable native component of SqlClient; no fee).
  - `xunit.abstractions` 2.0.3 — xUnit license URL (Apache-2.0 project, test-only).
- **npm, production tree:** 515 non-dev entries in `client/package-lock.json`: MIT 444, ISC 21,
  BSD-3 7, Apache-2.0 6, BSD-2 5, MPL-2.0 24 (`lightningcss*` — Tailwind's build-time CSS
  compiler, not shipped in the bundle), OFL-1.1 2 (fonts), BlueOak 2, 0BSD 1, Python-2.0 1,
  CC-BY-4.0 1 (`caniuse-lite` data, build-time). All free/open-source; none require payment.
- Historical note (from `CLAUDE.md`): MediatR was deliberately replaced by an in-house mediator
  because MediatR moved to a paid license — `src/QueryBuilder.Application/Common/Mediator.cs`.
- **Rule for every new phase:** any added dependency gets the same audit (direct + transitive)
  before it is merged; packages that went commercial (MediatR, AutoMapper ≥15, FluentAssertions ≥8,
  etc.) are excluded.

## 2. Layering (Observation)

| Project | Role | Key evidence |
|---|---|---|
| `QueryBuilder.Domain` | Entities, enums, `QueryDefinition` model, no deps | `Entities/*.cs`, `Model/QueryDefinition.cs` |
| `QueryBuilder.Application` | Commands/queries via in-house mediator, validation, abstractions, data-scope logic | `DataSources/RunQueryCommand.cs`, `DataScoping/DataScopeGuard.cs` |
| `QueryBuilder.Infrastructure` | EF Core, SQL generation, catalog introspection, execution, export, audit | `Sql/SqlServerQuerySqlBuilder.cs`, `Execution/SqlServerQueryExecutionService.cs` |
| `QueryBuilder.Editor` | Published NuGet package: DI, endpoints, auth, identity, setup diagnostics, embedded SPA | `EndpointRouteBuilderExtensions.cs`, `ServiceCollectionExtensions.cs` |
| `QueryBuilder.Api` | Sample host | `Program.cs`, `Seed/*` |
| `client/` | React SPA, built into `src/QueryBuilder.Editor/wwwroot` | `client/src/App.tsx` |
| `tests/QueryBuilder.Tests` | xUnit: SQL builder scope, data-scope evaluator/guard/options, parameter names, record limits | 6 test files, 75 tests |

Domain/Application/Infrastructure DLLs are bundled into the single `QueryBuilder.Editor` package
(`<None Include="$(OutputPath)QueryBuilder.*.dll" Pack="true" …>` in `QueryBuilder.Editor.csproj`).

## 3. Query model, generation, execution (Observation)

- **Definition** — `src/QueryBuilder.Domain/Model/QueryDefinition.cs:9-20`: one `Source`
  (schema/object/alias), `Joins` (model only; see discrepancy D3), `Columns` (with
  `Aggregate` None/Sum/Avg/Count/CountDistinct/Min/Max, `Alias`, `DataType`, `IsVisible`),
  nested `Filters` (`FilterGroup` And/Or), `GroupBy`, `Having`, `Sorts`, `Parameters`, `RowLimit`
  (default 1000). Column output name = `EffectiveAlias` (`:66`).
- **Parameters** — a filter condition can be `IsParameterized` with `ParameterName`
  (`:99-100`); declared `QueryParameter` has Name/Label/DataType/DefaultValue/IsRequired
  (`:103-110`).
- **SQL build** — `SqlServerQuerySqlBuilder.Build(definition, scopePredicates)`: literals become
  `@p{n}` parameters (`src/QueryBuilder.Infrastructure/Sql/SqlServerQuerySqlBuilder.cs:28`);
  runtime parameters become `@{ParameterName}` only after `QueryParameterNames.IsValid` (`:262-266`);
  scope predicates become `@__scope{n}` (`:130-131`). No value is concatenated into SQL.
- **Execution** — `SqlServerQueryExecutionService`: `SqlCommand` with `CommandTimeout = 60`
  (`src/QueryBuilder.Infrastructure/Execution/SqlServerQueryExecutionService.cs:31`), every
  generated parameter added as a typed `SqlParameter` (`:32-34`, `:87-96`), row cap applied as
  `maxRows + 1` to detect truncation (`:22`, `:50`).
- **Run** — `RunQueryCommand` (`src/QueryBuilder.Application/DataSources/RunQueryCommand.cs:30-75`):
  load data source → if `SavedQueryId` supplied, refuse when disabled (`:35-43`) →
  `catalogService.ValidateAsync` (`:45`) → `dataScopeGuard.AuthorizeAsync` (`:46`) → `Build`
  (`:49`) → grid row cap from record limits (`:51`) → execute → audit `QueryRun` with scope
  description (`:57-66`) → `QueryResultDto(columns, rows, rowCount, ms, truncated, rowLimit)`.
- **Export** — `ExportQueryCommand.cs:33-86`: same guard chain; with a record limit and a
  truncated result it throws `RecordLimitExceededException` (`:60-65`); otherwise legacy 100,000
  silent cap.
- **Result caching** — none. The only `IMemoryCache` users are the catalog
  (`SqlServerDataCatalogService.cs:16`) and data-scope rules (`DataScopeRuleRepository.cs:9`).
  Every run executes against the business database.
- **Cancellation / async jobs** — runs are synchronous HTTP requests; the `CancellationToken` is
  passed through; there is no job queue, no status polling, no user-facing cancel.
- **Background services** — only migration (`DatabaseMigrationHostedService`), default data-source
  seeding, and (sample host) demo seeding. No scheduler.

## 4. Saved queries, ownership, sharing, disabling (Observation)

- Entity `SavedQuery` (`src/QueryBuilder.Domain/Entities/SavedQuery.cs:3-20`): Name, Description,
  DataSourceId, OwnerId/OwnerName, `DefinitionJson`, **`IsFavorite` (single flag on the row)**,
  `IsDisabled`, `Shares`.
- Share `QueryShare` (`QueryShare.cs:12-19`): SharedWithUserId/Email, AccessLevel Viewer|Editor.
- List — `SavedQueryRepository.GetForUserAsync` returns owner's + shared-with-me queries
  (`src/QueryBuilder.Infrastructure/Repositories/SavedQueryRepository.cs:16-22`).
- Get by id — owner or any share (`GetSavedQueryByIdQuery.cs:18-23`).
- Save/update — owner or Editor share; Viewer gets 403 (`SaveQueryCommand.cs:51-56`); save also
  runs catalog validation and the scope guard (`:41-42`).
- Owner-only: delete (`DeleteSavedQueryCommand.cs:20-22`), share/unshare (`ShareQueryCommand.cs:40-43`,
  `RemoveShareCommand.cs:22-24`), view shares (`GetQuerySharesQuery.cs:19-21`), disable/enable
  (`SetQueryDisabledCommand.cs:20-23`).
- Audit history visible to owner or share holders (`GetAuditLogQuery.cs:25-28`).

## 5. Identity, authorization, data restrictions (Observation)

- **Actor** — `CurrentUserService` (`src/QueryBuilder.Editor/Identity/CurrentUserService.cs:26-42`):
  host `ActorResolver(HttpContext)` → `User.Identity.Name` → `"anonymous"`. No HttpContext ⇒
  `"anonymous"`.
- **Route gate** — all routes under `/querybuilder`; `Authorization` applied to the whole group
  and `AdminAuthorization` additionally to `/api/admin` (AND composition)
  (`src/QueryBuilder.Editor/EndpointRouteBuilderExtensions.cs:21-31`, modes at `:33-60`).
- **Catalog restrictions** — per data source: `AllowedSchemas`, `CatalogScope`
  (Views/Tables/Both), `AllowedObjects` allowlist (`DataSource.cs:26-36`), enforced by
  `catalogService.ValidateAsync` on run/export/preview/save.
- **Row-level scope** — `CurrentDataScopeService.ResolveAsync`
  (`src/QueryBuilder.Editor/DataScoping/CurrentDataScopeService.cs:28-63`): feature off ⇒
  Unrestricted; **no HttpContext ⇒ Denied** (`:36-40`); resolver throws/null ⇒ Denied.
  `DataScopeGuard.AuthorizeAsync` (`src/QueryBuilder.Application/DataScoping/DataScopeGuard.cs:40-79`)
  turns the user's scope into `ScopePredicate`s for the source and every join, or throws 403.
  Scope is resolved once per request (memoized `_resolved`, `:25`).
- **Record limits** — `RecordLimits.Effective(dataSource.MaxRecords, host DefaultMaxRecords)`;
  with a limit the grid caps at it (ignores client `maxRows`) and export refuses when exceeded
  (`src/QueryBuilder.Application/Common/RecordLimits.cs:21-26`).

## 6. Frontend (Observation)

- Router basename `/querybuilder` (`client/src/main.tsx:26`); routes `/`, `/admin`,
  `/admin/data-sources/:id`, `/queries/new`, `/queries/:id` (`client/src/App.tsx:12-16`).
- `QueryEditorPage` uses `Tabs` with `build` / `results` / `history`
  (`client/src/pages/QueryEditorPage.tsx:305-324`); the run request is built with
  `maxRows: 1000, savedQueryId` (`:455-456`); disabled queries disable Run/Export buttons
  (`:235-261`).
- Results: `ResultsTable.tsx` + `results/useResultsView.ts` (client-side paging, search, column
  show/hide, sort) and a truncation banner driven by `truncated`/`rowLimit`
  (`client/src/types/index.ts:257-264`).
- UI kit available: shadcn `card`, `dialog`, `select`, `tabs`, `tooltip`, `table`, `alert`, etc.
  (`client/src/components/ui/`). No chart component.
- Known sharp edges to respect (from `CLAUDE.md`): Zustand selectors must not return new
  arrays; Radix ScrollArea fix; `min-w-0` for truncation; add child EF entities via `DbSet`.

## 7. Host integration and provisioning (Observation)

- Single call registration (`AddQueryBuilderEditor` / `MapQueryBuilderEditor`); options surface
  (`src/QueryBuilder.Editor/QueryBuilderEditorOptions.cs`): `ConnectionString`, `ActorResolver`,
  `ApplyMigrations` (default true), `Authorization`, `AdminAuthorization`, `DataScope`
  (`Keys`, `Resolver`, `ResolverAsync`), `DefaultMaxRecords`.
- JSON contract: every endpoint uses `QueryBuilderJson.Options` explicitly.
- Migrations: 6 EF migrations (InitialCreate → AddDataSourceMaxRecords); DBA path =
  `src/QueryBuilder.Editor/Scripts/QueryBuilder.schema.1.0.13.sql` (idempotent script, regenerated
  per release that adds a migration).

## 7a. Catalog validation and export (Observation, E-Q16)

- `SqlServerDataCatalogService.ValidateAsync` (`src/QueryBuilder.Infrastructure/Catalog/SqlServerDataCatalogService.cs:38-131`)
  validates against the **cached, policy-filtered** catalog (`BuildCatalogAsync` passes
  `applyCatalogScopeAndAllowlist: true`, `:168`; scope/allowlist filters at `:289-300`). It rejects:
  - a missing alias or an unknown object (`:51-56`);
  - duplicate aliases (`:60`);
  - unknown aliases or columns (`:74-78`);
  - joins without conditions (`:86`);
  - filters that reference an undeclared parameter (`:131`).

  The unfiltered overload (`:157`) is the admin path.
- Export formats: CSV and XLSX only (`src/QueryBuilder.Application/Abstractions/IExportService.cs:3-7`,
  `src/QueryBuilder.Infrastructure/Export/ExportService.cs:13-28`).

## 7b. Provisioning paths (Observation, E-Q17)

- Auto: `DatabaseMigrationHostedService` runs `Database.MigrateAsync` at startup and is registered
  only when `ApplyMigrations` is true
  (`src/QueryBuilder.Infrastructure/Persistence/DatabaseMigrationHostedService.cs:9,19`).
- DBA: `ApplyMigrations = false`, and the DBA runs
  `Scripts/QueryBuilder.schema.<version>.sql`, generated with
  `dotnet-ef migrations script --idempotent` (`src/QueryBuilder.Editor/Scripts/README.md:4-29`).
- `/_setup` reports pending migrations and names the fix for each mode
  (`src/QueryBuilder.Editor/Diagnostics/SetupDiagnosticsRunner.cs:52-61`).

## 7c. What the automated tests actually cover (Observation, E-Q03, E-Q18)

53 test methods (`[Fact]`/`[Theory]`), which expand to 75 test cases:

| File | Methods | Covers |
|---|---|---|
| `DataScopeEvaluatorTests.cs` | 10 | scope rule evaluation |
| `DataScopeGuardTests.cs` | 7 | guard → predicates / 403 |
| `DataScopeOptionsTests.cs` | 15 | `DataScope` API incl. the string/params overload bug |
| `ParameterNameValidationTests.cs` | 3 | runtime parameter-name injection guard |
| `RecordLimitTests.cs` | 8 | limit resolution |
| `SqlBuilderScopeTests.cs` | 10 | scope predicates in generated SQL |

**No automated tests cover** these README claims:
- saved-query access rules (owner, Viewer, Editor);
- sharing and unsharing;
- disable/enable;
- favorites;
- export file content;
- audit visibility;
- endpoint authorization modes;
- the frontend.

Per `CLAUDE.md`, those were verified by manual and Playwright runs. **Inference (high):** these
claims are confirmed by source reading here, but nothing guards them against regressions. The
visualization slice adds handler-level tests for the access rules it reuses (PLAN tasks 24–25).

## 8. Discrepancies between documented claims and source

| ID | Claim (where) | What source shows | Impact |
|---|---|---|---|
| D1 | "Disable a saved query to block it from being run or exported by anyone" (README Features) | Disabled check only happens when the browser sends `SavedQueryId` (`RunQueryCommand.cs:35-43`, `ExportQueryCommand.cs:38-46`). The same definition sent without the id runs. **Observation.** | Not a data leak (the caller still needs catalog + scope access to build that definition), but "disabled" is enforced per-request-shape, not per-definition. **Any visualization render path must load the saved query server-side and enforce disabled itself.** |
| D2 | Favorites (not documented in README, exposed in API/UI) | `IsFavorite` is one flag on the `SavedQuery` row, toggled with **no ownership/share check** (`ToggleFavoriteCommand.cs:13-21`). A favorite is shared by all users of the query, and any caller who knows the id can flip it. **Observation.** | Low severity; out of scope for this slice. Logged for the gap matrix (per-user favorites). |
| D3 | Joins "deliberately skipped" (`CLAUDE.md`) | Domain/builder/guard support `Joins` (`QueryDefinition.cs:12`, `DataScopeGuard.cs:59`), but the UI never creates them. **Observation.** | Consistent with the decision; the scope guard already covers joins if a definition carries them. |
| D4 | Audit records `SavedQueryId` for runs | The id is browser-supplied and not checked for access on run/export (only existence + disabled). **Observation.** | An audit row can attribute a run to a query the caller cannot open. Visualization renders must check access before auditing. |
| D5 | Domain enum lists providers PostgreSql/MySql/Sqlite (`Enums.cs:3-9`) | Only SQL Server builder/executor exist. **Inference (high):** other providers are placeholders. | Gap matrix: providers = partial. |
