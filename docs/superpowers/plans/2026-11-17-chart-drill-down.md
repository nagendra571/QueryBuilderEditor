# Chart and Results Drill-Down Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Click a chart mark or an aggregated result row to see the rows behind it, break the chart down one level, or
cross-filter a dashboard, without ever returning a row outside the caller's zone.

**Architecture:** The browser sends only a click trail (`steps`); the server derives a drill definition from the saved
query with a pure transform (`DrillDefinitions`), then runs it through the same access → catalog validation → parameter
binding → `IDataScopeGuard` → `Build(definition, scope)` path as charts (`VisualizationDataLoader`). Cross-filters are
drill steps applied per tile on the existing chart-data route. The client adds a click menu, a drill page route, an
in-place breakdown trail and a dashboard filter bar.

**Tech Stack:** .NET 8/10, EF Core 9/10, xUnit; React + TS + Recharts 3 + react-router 7 + TanStack Query; Node test runner.

**Spec:** `docs/superpowers/specs/2026-11-17-chart-drill-down-design.md` (read it first; this plan argues from it).

## Global Constraints

- Every endpoint reads bodies with `ReadFromJsonAsync<T>(QueryBuilderJson.Options, ct)` and returns `Results.Json(x, QueryBuilderJson.Options)`.
- In-house mediator (`IRequest`/`IRequestHandler`/`ISender`); never MediatR.
- The browser never sends a definition or SQL for a drill; only `steps`, `action`, `breakdownColumn`, `crossFilters`, parameter values, dashboard/tile ids.
- Generated SQL keeps the shape `(scope) AND (user filters AND drill steps)`; `Build`'s scope parameter stays required.
- Drill columns shown = the query's visible non-aggregated columns, aggregated columns replaced by their base column, plus the chart's `drill.columns` (≤ 20). Never the whole view.
- Limits (constants in `DrillLimits`): max 8 steps, step value ≤ 4,000 chars, max 5 cross-filters, max 20 drill columns.
- Identifier matching via `IdentifierMatching` (provider comparer), never hardcoded `OrdinalIgnoreCase`.
- New audit action `QueryDrilled = 37`.
- Migration adds `Dashboards.ClickToFilterEnabled bit NOT NULL DEFAULT 1`; target release Editor **2.2.0** (version bump and DBA script only when the user asks).
- Client: no `crypto.randomUUID`/`crypto.subtle`/`navigator.clipboard` without fallback (insecure-context test); Zustand selectors never return new arrays; `import type` in pure model files with relative `.ts` runtime imports; Zoning Map design world.
- Works on SQL Server and PostgreSQL.
- Never `git push` or `dotnet nuget push`. Never `git add -A` (wwwroot/assets churns).

## Review Focus

1. A `null` (empty) slice → steps carry `null` → `IS NULL`, and the drill shows exactly the rows with no value (not zero rows, not all rows). Owned by Task 1 and Task 7.
2. A clicked date/datetime value round-trips through the URL and still matches its rows on both providers (timezone/precision). Owned by Task 7 (integration) and Task 8 (`drill-url` tests).
3. A Viewer opening a shared drill link for a dashboard they can't see gets 403, not rows; a Viewer whose dashboard tile was removed after the link was made gets 404. Owned by Task 4.
4. Parameter values on the drill page are the ones the chart was drawn with (a Viewer's constrained values), not defaults. Owned by Task 4.
5. Cross-filter on a dashboard with the switch off is refused server-side even when the URL carries filters (400), and the page shows tiles unfiltered with a notice. Owned by Task 6 and Task 11.

---

## Task 0: Branch

- [ ] **Step 1:** `git switch -c feature/chart-drill-down` from `main` (`f6281a8`). Carry over the uncommitted `CLAUDE.md` notes.
- [ ] **Step 2:** `git cherry-pick 1c2065c` (demo/VIDEO-BRIEF.md, per CLAUDE.md).
- [ ] **Step 3:** Commit `CLAUDE.md`, the spec and this plan: `git add CLAUDE.md docs/superpowers/specs/2026-11-17-chart-drill-down-design.md docs/superpowers/plans/2026-11-17-chart-drill-down.md`, commit "Plan chart drill-down (spec + plan) and session notes".

---

## Task 1: Drill model and definition transform (pure)

**Files:**
- Create: `src/QueryBuilder.Domain/Model/Drill.cs`
- Create: `src/QueryBuilder.Application/Drill/DrillDefinitions.cs`
- Create: `src/QueryBuilder.Application/Drill/DrillLimits.cs`
- Test: `tests/QueryBuilder.Tests/Drill/DrillDefinitionsTests.cs`, `tests/QueryBuilder.Tests/Drill/DrillSqlTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record DrillStep(string Column, string? Value);` — `Column` = output column name (`EffectiveAlias`) of the saved query or a drill column (catalog column name); `Value` null means "no value".
  - `public enum DrillAction { Rows, Breakdown }`
  - `public static class DrillLimits { MaxSteps = 8; MaxValueLength = 4000; MaxCrossFilters = 5; MaxDrillColumns = 20; }`
  - `public static class DrillDefinitions`:
    - `QueryDefinition ForRows(QueryDefinition saved, IReadOnlyList<DrillStep> steps, IReadOnlyList<string> drillColumns, Func<string,string,bool>? sameIdentifier = null)`
    - `(QueryDefinition Definition, VisualizationOptions Options) ForBreakdown(QueryDefinition saved, VisualizationType type, VisualizationOptions options, IReadOnlyList<DrillStep> steps, string breakdownColumn, IReadOnlyList<string> drillColumns)`
    - `QueryDefinition ApplySteps(QueryDefinition definition, IReadOnlyList<DrillStep> steps, IReadOnlyList<string> drillColumns)` (narrow only; used by ForRows, ForBreakdown and cross-filters)
    - `IReadOnlyList<string> AllowedStepColumns(QueryDefinition saved, IReadOnlyList<string> drillColumns)` — visible non-aggregated output names + drill columns.
    - `bool IsAggregated(QueryDefinition saved)` — any visible column with `Aggregate != None`.
    - `static readonly IReadOnlySet<VisualizationType> BreakdownTypes` = Bar, HorizontalBar, Line, Area, Combo, Waterfall, Pie, Funnel (use the real enum member names).
  - Throws `ValidationException`-style 400 (use the project's existing bad-request exception type that `GlobalExceptionHandler` maps to 400) with the messages below.

Rules (from spec §5, §6.2, §6.3):
- Each step's column must be in `AllowedStepColumns`; else 400 `"'{column}' can't be used to drill into this chart."`. More than `MaxSteps` → 400. Value longer than `MaxValueLength` → 400.
- A step maps to a `FilterCondition` on the underlying `(TableAlias, ColumnName, DataType)`: `Value == null` → `IsNull`; Boolean → `IsTrue`/`IsFalse` (value `"true"`/`"false"`, anything else 400); otherwise `Equals` with the literal value. Never `IsParameterized`.
- `ApplySteps` returns a copy whose `Filters` = `And { Groups = [original Filters (if non-empty), And{step conditions}] }`; the original definition object is never mutated.
- `ForRows`: steps applied; `GroupBy` cleared; `Having` = null; columns = visible non-aggregated columns, then each aggregated column replaced by a non-aggregated column on its base `(TableAlias, ColumnName)` aliased as the base column name (dedupe by `(TableAlias, ColumnName)`), then drill columns (as `QueryColumn` on the source alias, dedupe); hidden columns dropped; sorts kept only when they name a remaining column; `RowLimit` left for the loader.
- `ForBreakdown`: `type` must be in `BreakdownTypes` and `IsAggregated(saved)` true, else 400 `"This chart can't be broken down."`; `breakdownColumn` must be in `AllowedStepColumns` and not a step column, else 400. Columns = the chart's measure columns (keep aggregates) + the breakdown column (non-aggregated) + the chart's `Series` column if set and different; `GroupBy` = those non-aggregated columns; `Having` = null; options copy with `X = breakdownColumn` (and `Series` cleared when it equals the breakdown column). Re-run `VisualizationOptionsValidator.Validate(type, newOptions, newDefinition)` and 400 on any issue.

- [ ] **Step 1: Write failing tests** in `DrillDefinitionsTests` (use `VizFixture.Definition()`: Region text, Channel text, Total=Sum(Amount), Orders=Count(OrderId), hidden HiddenNote):
  - `Rows_adds_each_step_as_an_equality_inside_the_filter_group` — steps `[Region=EU]` → one condition `RegionCode Equals "EU"` nested under an `And` group; saved filter conditions still present in the other group.
  - `Null_step_becomes_is_null`
  - `Rows_removes_grouping_and_having_and_replaces_aggregates_with_base_columns` — columns = Region, Channel, Amount, OrderId; no aggregates; `Having == null`.
  - `Rows_never_includes_hidden_columns_but_includes_drill_columns` — `HiddenNote` absent; drill column `Site` present once.
  - `Step_on_an_aggregated_or_unknown_column_is_refused` — `Total` and `HiddenNote` and `Nope` → 400.
  - `Too_many_steps_or_long_value_is_refused` — 9 steps; 4,001-char value.
  - `Saved_definition_is_not_mutated`
  - `Breakdown_regroups_measures_by_the_new_column` — Bar, X=Region, Measures=[Total], steps [Region=EU], breakdown Channel → GroupBy=[Channel], columns Channel+Total, options.X=Channel.
  - `Breakdown_is_refused_for_trend_pivot_scatter_counter_table_and_for_unaggregated_queries`
  - `Breakdown_on_a_column_already_in_the_trail_is_refused`
- [ ] **Step 2:** `dotnet test tests/QueryBuilder.Tests --framework net10.0 --filter FullyQualifiedName~DrillDefinitionsTests` → FAIL (types missing).
- [ ] **Step 3:** Implement the files above.
- [ ] **Step 4: SQL shape tests** in `DrillSqlTests` with `SqlServerQuerySqlBuilder` and the Postgres builder, scope `ProgramId IN (10)`:
  - `Drill_steps_sit_inside_the_scope_and` — SQL contains `WHERE ([t0].[ProgramId] IN (@__scope0)) AND (` and the step parameter is bound, not inlined.
  - `An_OR_saved_filter_plus_a_step_cannot_escape_scope` — saved filters `Or{EU, US}` + step → `(scope) AND ((… OR …) AND (…))`.
- [ ] **Step 5:** Run both test classes on net10.0 and net8.0 → PASS.
- [ ] **Step 6:** Commit "Drill: pure definition transform with scope-safe step filters".

---

## Task 2: Chart drill columns option

**Files:**
- Modify: `src/QueryBuilder.Domain/Model/VisualizationOptions.cs` (add `public DrillOptions? Drill { get; set; }`, `public sealed class DrillOptions { public List<string> Columns { get; set; } = []; }`, and `Normalize()` null-guards it)
- Modify: `src/QueryBuilder.Application/Visualizations/VisualizationOptionsValidator.cs` (≤ `DrillLimits.MaxDrillColumns`, no duplicates under the provider comparer, non-empty names; not part of `ReferencedColumns`)
- Modify: the save handler for `SaveVisualizationCommand` — each drill column must exist on the query's source object in the data source's catalog (`IDataCatalogService.GetCatalogAsync`), else 400 `"'{c}' is not a column of {schema}.{object}."`
- Modify: `client/src/features/visualizations/ChartEditorDialog.tsx` — "Columns to show when drilling" multi-select from the source object's catalog columns (max 20); `client/src/lib/api.ts` types gain `drill?: { columns: string[] }`.
- Test: `tests/QueryBuilder.Tests/VisualizationOptionsValidatorTests.cs`, `tests/QueryBuilder.Tests/VisualizationEditCommandsTests.cs`

- [ ] **Step 1:** Failing tests: `Drill_columns_over_20_or_duplicated_are_refused`; `Drill_columns_do_not_count_as_referenced_columns`; `Saving_a_drill_column_that_is_not_in_the_source_object_is_refused`; `Old_options_without_drill_still_deserialize`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS (both frameworks).
- [ ] **Step 5:** Editor UI; `cd client && npm run build` succeeds.
- [ ] **Step 6:** Commit "Drill: per-chart columns to show when drilling".

---

## Task 3: Loader refactor and row keys in chart data

**Files:**
- Modify: `src/QueryBuilder.Application/Visualizations/VisualizationDataLoader.cs`
- Modify: `src/QueryBuilder.Application/Dtos/VisualizationDtos.cs`
- Test: `tests/QueryBuilder.Tests/RenderVisualizationCommandTests.cs`

**Interfaces:**
- Produces:
  - `VisualizationDataPurpose` gains `Drill`.
  - `public sealed record VisualizationContext(SavedQuery SavedQuery, QueryVisualization Visualization, QueryDefinition Definition, VisualizationOptions Options, DataSource DataSource, Dictionary<string,string?> Values, bool CanView);`
  - `Task<VisualizationContext> ResolveAsync(Guid savedQueryId, Guid visualizationId, Dictionary<string,string?> parameterValues, Guid? dashboardId, Guid? tileId, VisualizationDataPurpose purpose, CancellationToken ct)` — the existing access/grant/ConstrainValues/not-found/disabled/stale/data-source prelude, extracted unchanged.
  - `Task<(QueryResultSet Result, int Cap, int? EffectiveLimit)> ExecuteAsync(VisualizationContext context, QueryDefinition definition, int cap, CancellationToken ct)` — `ValidateAsync` → `parameterBinder.MaterializeAsync(definition, context.Values)` → `dataScopeGuard.AuthorizeAsync` → `Build(bound, scope)` → execute. The ONLY way drill code reaches SQL.
  - `LoadAsync` keeps its signature and behaviour, now composed from the two.
  - `VisualizationDataDto` gains `List<string>? KeyColumns = null` and `CrossFilterResultDto? CrossFilter = null` (record in Task 6).
- Row keys: for Draw on a drillable chart (aggregated query, type not Counter), rows additionally carry every **visible non-aggregated** output column not already referenced, and `KeyColumns` lists them. Hidden columns never appear.

- [ ] **Step 1:** Failing tests: `Chart_rows_carry_key_columns_for_drilling` (bar on Region with the fixture's Channel → row has `Channel`, `KeyColumns == ["Channel"]`); `Key_columns_never_include_hidden_columns` (`HiddenNote` absent); `Counter_and_unaggregated_charts_have_no_key_columns`; existing render/export tests unchanged.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Full `RenderVisualizationCommandTests`, `ExportVisualizationCommandTests`, `Hub/DashboardContextRenderTests` → PASS on both frameworks.
- [ ] **Step 5:** Commit "Chart data: shared resolve/execute path and drill row keys".

---

## Task 4: Drill command, export, audit and routes

**Files:**
- Create: `src/QueryBuilder.Application/Drill/DrillVisualizationCommand.cs`, `src/QueryBuilder.Application/Drill/ExportDrillCommand.cs`
- Modify: `src/QueryBuilder.Domain/Enums/Enums.cs` (`QueryDrilled = 37`)
- Modify: `src/QueryBuilder.Application/Dtos/VisualizationDtos.cs`
- Modify: `src/QueryBuilder.Editor/Endpoints/VisualizationEndpoints.cs`
- Modify: `tests/QueryBuilder.Tests/Hub/CrossAppEndpointMatrixTests.cs` (route table + `ViewerRoutes`)
- Test: `tests/QueryBuilder.Tests/Drill/DrillVisualizationCommandTests.cs`

**Interfaces:**
- Consumes: Task 1 `DrillDefinitions`, Task 3 `ResolveAsync`/`ExecuteAsync`.
- Produces:
  - `public sealed record DrillRequest(DrillAction Action, List<DrillStep>? Steps, string? BreakdownColumn, List<CrossFilterDto>? CrossFilters, Dictionary<string,string?>? ParameterValues, Guid? DashboardId, Guid? TileId);`
  - `public sealed record DrillExportRequest(... same fields ..., string Format);`
  - `public sealed record DrillResultDto(DrillAction Action, List<QueryResultColumnDto> Columns, List<Dictionary<string,object?>> Rows, int RowCount, long ExecutionTimeMs, int RowLimit, bool Truncated, VisualizationType Type, VisualizationOptions? Options);` (`Options` = rewritten options for Breakdown, null for Rows)
  - `DrillVisualizationCommand(Guid SavedQueryId, Guid VisualizationId, DrillRequest Request) : IRequest<DrillResultDto>`
  - `ExportDrillCommand(Guid SavedQueryId, Guid VisualizationId, DrillRequest Request, ExportFormat Format) : IRequest<ExportResult>` (Rows only; Breakdown → 400)
  - Routes: `POST /api/queries/{queryId}/visualizations/{visualizationId}/drill` and `/drill/export`, both `.AllowViewers()`.

Behaviour: Rows cap = effective record limit else the legacy 1,000 grid cap, truncation flagged (like the results grid). Breakdown cap/refusal identical to drawing a chart (`RecordLimits.ChartRows`, refuse truncated, `VisualizationDataChecks.Check`). Export: up to the effective limit or `RecordLimits.LegacyExportRows`, refused (409) when over. `CrossFilters` is accepted on the request but non-empty values are refused (400) until Task 6 wires them in as extra steps via `CrossFilterResolver` (Task 6 adds that test). Audit `QueryDrilled` with `{ VisualizationId, DashboardId, TileId, Action, Steps, BreakdownColumn, RowCount, Scope }`; export audits `QueryExported` with the same plus `Format`.

- [ ] **Step 1: Failing tests:**
  - `Stranger_is_forbidden_and_nothing_runs`; `Viewer_share_can_drill`; `Disabled_query_is_refused`.
  - `Viewer_without_query_access_drills_through_a_dashboard_grant`; `Viewer_with_a_dashboard_that_does_not_contain_the_tile_is_forbidden` (Review Focus 3); `Removed_tile_is_not_found`.
  - `Viewer_parameter_values_are_constrained_like_drawing` (Review Focus 4: a non-Fixed-mapped value is replaced exactly as in `RenderVisualizationCommand`).
  - `Rows_result_has_only_the_allowed_columns` (no `HiddenNote`, even if the executor returns it).
  - `Guard_receives_the_drilled_definition` (`FakeGuard.AuthorizeCalls == 1`; the definition it saw contains the step).
  - `Breakdown_returns_rewritten_options_and_refuses_truncated_data`.
  - `Drill_is_audited_with_steps_and_scope`; `Drill_export_over_the_limit_is_refused_not_truncated`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement command, export, DTOs, endpoints.
- [ ] **Step 4:** Update the endpoint matrix route table and `ViewerRoutes` (allowlist grows from 8 to 10 routes); run `Hub/CrossAppEndpointMatrixTests` and `Hub/ViewerAndPublishingTests` → PASS (the matrix discovers routes and fails on unknown ones).
- [ ] **Step 5:** Run new tests on both frameworks → PASS.
- [ ] **Step 6:** Commit "Drill: rows and breakdown routes on the chart engine, audited, Viewer-allowed".

---

## Task 5: Results-grid drill (saved queries)

**Files:**
- Create: `src/QueryBuilder.Application/Drill/DrillQueryCommand.cs`
- Modify: `src/QueryBuilder.Editor/Endpoints/SavedQueryEndpoints.cs`
- Test: `tests/QueryBuilder.Tests/Drill/DrillQueryCommandTests.cs`; matrix table update

**Interfaces:**
- Produces: `DrillQueryCommand(Guid SavedQueryId, List<DrillStep> Steps, Dictionary<string,string?> ParameterValues) : IRequest<DrillResultDto>` and `ExportDrillQueryCommand(..., ExportFormat Format)`; routes `POST /api/queries/{id}/drill` and `/api/queries/{id}/drill/export` — **not** on the Viewer allowlist (Viewers have no query page). Drill columns = none (no chart). Same access (`SavedQueryAccess.CanView`), disabled, validation, scope, limits and audit as Task 4, sharing its execute path (extract a shared helper rather than copy).

- [ ] **Step 1:** Failing tests: `Results_drill_returns_rows_of_the_clicked_group`; `Unaggregated_query_drill_is_refused` (400 "This query has no totals to drill into."); `Stranger_is_forbidden`; `Results_drill_is_audited`.
- [ ] **Step 2–4:** FAIL → implement → PASS (both frameworks, matrix included).
- [ ] **Step 5:** Commit "Drill: rows behind a saved query's result row".

---

## Task 6: Dashboard cross-filters (server) and the author switch

**Files:**
- Modify: `src/QueryBuilder.Domain/Entities/Dashboard.cs` (`public bool ClickToFilterEnabled { get; set; } = true;`)
- Create migration: `dotnet tool run dotnet-ef migrations add AddDashboardClickToFilter --project src/QueryBuilder.Infrastructure --startup-project src/QueryBuilder.Api --framework net10.0 --output-dir Persistence/Migrations` (column `bit NOT NULL` default `1`; check the generated migration does not touch any `AppId` column)
- Modify: `DashboardDetailDto` (+ `bool ClickToFilterEnabled`), `SaveDashboardRequest`/`RenameDashboardCommand` (+ optional `bool? ClickToFilterEnabled`; null = unchanged; owner/Editor only, as rename today)
- Create: `src/QueryBuilder.Application/Drill/CrossFilterResolver.cs`
- Modify: `RenderVisualizationRequest`/`RenderVisualizationCommand` (+ `List<CrossFilterDto>? CrossFilters`), `RenderVisualizationCommandHandler`
- Test: `tests/QueryBuilder.Tests/Drill/CrossFilterTests.cs`, migration test alongside existing migration tests

**Interfaces:**
- Produces:
  - `public sealed record CrossFilterDto(Guid SourceTileId, string Column, string? Value);` (`Column` = output name in the source tile's query)
  - `public sealed record CrossFilterResultDto(List<string> Applied, List<string> NotApplied);` (column labels)
  - `CrossFilterResolver.ResolveAsync(Dashboard dashboard, IReadOnlyList<CrossFilterDto> filters, SavedQuery targetQuery, AccessActor actor, CancellationToken ct) : Task<(List<DrillStep> Steps, CrossFilterResultDto Result)>`

Rules (spec §6.4): any filters while `ClickToFilterEnabled == false` → 400 `"Click-to-filter is turned off for this dashboard."` (Review Focus 5); filters require dashboard + tile context; > `MaxCrossFilters` → 400; each `SourceTileId` must be a chart tile of this dashboard, else 400; the column must be in `AllowedStepColumns` of the source tile's query + chart drill columns, else 400; resolve the source column to its **catalog column name**; the filter applies to the target when the target query is on the same data source and its source object has a column with that name (catalog lookup, provider comparer) — then it becomes a step on that column (allowed even if the target query doesn't output it, because the value came from a column the source chart shows); otherwise it lands in `NotApplied`. HAVING stays for cross-filtered tiles (spec §6.3).

Also modify `DrillVisualizationCommand` (Task 4) so its `CrossFilters` become extra steps through the resolver.

- [ ] **Step 1: Failing tests:** `Drill_on_a_filtered_dashboard_applies_the_cross_filters`; `Switch_off_refuses_filters`; `Filter_from_a_tile_of_another_dashboard_is_refused`; `Filter_on_a_column_the_source_chart_does_not_show_is_refused` (e.g. `HiddenNote`); `Filter_applies_by_catalog_column_name_to_another_query_on_the_same_view_family`; `Tile_without_the_column_reports_not_applied_and_is_unfiltered`; `Other_data_source_reports_not_applied`; `Having_is_kept_for_cross_filtered_tiles`; `New_dashboards_default_to_click_to_filter_on`; `Rename_can_turn_the_switch_off_and_bumps_version`.
- [ ] **Step 2–4:** FAIL → implement → PASS on both frameworks. Run `Hub/AppModelTests` (EF model check) too.
- [ ] **Step 5:** Commit "Dashboards: server-side click-to-filter with a per-dashboard switch".

---

## Task 7: Zone guarantee against real databases

**Files:**
- Create: `tests/QueryBuilder.Tests/Drill/DrillScopeSqlServerTests.cs` (`[MetadataSqlFact]`, collection `MetadataDatabaseCollection`)
- Create: `tests/QueryBuilder.Tests/Drill/DrillScopePostgresTests.cs` (`[PostgresFact]`, uses `PostgresTestDatabase` or its own throwaway schema)

Setup: create a throwaway business database (`qb_drill_it_<guid>` on the `QB_TEST_METADATA_SQLSERVER` server; dropped in `finally`) with `dbo.Enrolments(Id int, ProgramId int, Status nvarchar(20) NULL, Site nvarchar(20), Amount decimal(10,2), Score float, EnrolledOn datetime2, Note nvarchar(50))` — 60 rows over ProgramId 10/20/30, some `Status` NULL, a few distinct `EnrolledOn` with fractional seconds. Wire `VizFixture`-style handlers with the **real** SQL builder factory and **real** execution service for that provider (a fixed connection-string resolver), and `FakeGuard.Predicates` = the persona's `ScopePredicate`s. Personas: Unrestricted (no predicates), P10 (`ProgramId IN (10)`), P10+30.

- [ ] **Step 1: Write the matrix** — for each persona × action [Rows, Breakdown by Site, Breakdown by ProgramId, Rows with drill column ProgramId, cross-filtered tile, drill export (parse the CSV)] × steps [none, `Status=Active`, `Status=null`, tampered `ProgramId=20`]: assert every returned row (or every CSV row) has `ProgramId` in the persona's set; for P10 + tampered `ProgramId=20` assert zero rows; for Breakdown by ProgramId assert only in-zone groups appear.
- [ ] **Step 2: Exactness tests:** `Null_step_returns_exactly_the_rows_without_a_value` (Review Focus 1); `Datetime_value_from_chart_data_round_trips_to_the_same_rows` (take the value from a rendered chart's JSON row, send it back as a step; Review Focus 2); `Float_step_matches_its_row` — if it can't be made to match exactly on either provider, change Task 1 to refuse steps on that column type with 400 `"This value can't be matched exactly; drill on another column."` and assert that instead.
- [ ] **Step 3:** Run with both env vars set, net10.0 and net8.0 → PASS. A skip is not a pass: report it.
- [ ] **Step 4:** Commit "Drill: zone guarantee proved against SQL Server and PostgreSQL".

---

## Task 8: Client drill model (pure)

**Files:**
- Create: `client/src/features/drill/drill-model.ts`, `client/src/features/drill/drill-url.ts`
- Test: `client/tests/drill-model.test.ts`, `client/tests/drill-url.test.ts`

**Interfaces:**
- Produces:
  - `type DrillStep = { column: string; value: string | null }`
  - `type ClickedMark = { row?: Record<string, unknown>; pivot?: { rowValues: Record<string, unknown>; columnValue?: { column: string; value: unknown } }; anchor: { x: number; y: number } }`
  - `stepsForMark(type: VisualizationType, options: VisualizationOptions, columns: QueryResultColumn[], keyColumns: string[], mark: ClickedMark): DrillStep[] | null` — null when not clickable (waterfall total, scatter point without label/series, pivot grand total → `[]` only for Counter). Steps = every non-aggregated referenced column + every key column of the row; pivot = row values + column value.
  - `toWireValue(value: unknown, dataType: ColumnDataType): string | null` — dates exactly as received from the server (no `Date` reparse), booleans `"true"/"false"`.
  - `menuItems(ctx: { type; aggregated: boolean; onDashboard: boolean; clickToFilter: boolean }): Array<'rows'|'breakdown'|'filter'>`
  - `breakdownChoices(allowed: string[], trail: DrillStep[]): string[]`
  - `encodeTrail(steps: DrillStep[]): string` / `decodeTrail(s: string): DrillStep[] | null` — base64url of JSON, URL-safe without `btoa` on non-Latin1 (use `TextEncoder`), rejects > 8 steps; same for cross-filters `encodeFilters`/`decodeFilters` (`{ sourceTileId, column, value }`, ≤ 5).
- [ ] **Step 1:** Failing tests: round-trip of unicode, `null`, and a datetime string with fractional seconds; malformed input → null; per-type `stepsForMark` table from spec §6.1; `menuItems` hides breakdown for Trend/Pivot/Scatter/Counter/Table and unaggregated queries, hides filter off-dashboard or when switched off.
- [ ] **Step 2:** `cd client && npm test` → FAIL. **Step 3:** Implement. **Step 4:** PASS. **Step 5:** Commit.

---

## Task 9: Clickable charts and the drill menu

**Files:**
- Modify: `client/src/features/visualizations/ChartRenderer.tsx`, `CartesianView.tsx`, `FunnelView.tsx`, `WaterfallView.tsx`, `TrendView.tsx`, `ScatterView.tsx`, `PivotView.tsx`, `TableView.tsx`, and the pie and counter renderers
- Create: `client/src/features/drill/DrillMenu.tsx`, `client/src/features/drill/ExploreValuesButton.tsx`

**Interfaces:**
- `RendererProps` gains `onMarkClick?: (mark: ClickedMark) => void` and `keyColumns?: string[]`; absent = today's behaviour exactly (no cursor change, no handlers).
- `DrillMenu({ anchor, items, breakdownChoices, rowCountHint?, onRows, onBreakdown(column), onFilter, onClose })` — Radix popover anchored at the click point; Escape closes; focus returns to the chart.
- `ExploreValuesButton` in the chart header opens a keyboard-navigable list of the chart's marks (label + value) that opens the same menu — the keyboard path required by spec §6.1.

- [ ] **Step 1:** Wire `onClick` on Recharts marks (Bar cells, Pie sectors, Line/Area active dots, Scatter points, Funnel/Pivot/Table rows/cells, Counter number); pointer cursor only when `onMarkClick` is set.
- [ ] **Step 2:** `npm run build` and `npm test` pass.
- [ ] **Step 3: Browser check** (Playwright, sample app with the `TEMP-DATASCOPE-TEST` personas re-added per CLAUDE.md): clicking a bar, a pie slice, a pivot cell and the counter opens the menu at the mark; keyboard: Tab to "Explore values", Enter, arrow, Enter opens the menu. Wait ~4 s for Recharts animation before screenshots.
- [ ] **Step 4:** Commit "Charts: clickable marks, drill menu and a keyboard path".

---

## Task 10: Drill page and in-place breakdown

**Files:**
- Create: `client/src/pages/DrillPage.tsx`, `client/src/features/drill/DrillTrail.tsx`, `client/src/hooks/useDrill.ts`
- Modify: `client/src/App.tsx` (routes `/queries/:queryId/charts/:vizId/drill` and `/queries/:queryId/drill`, inside `<Gate>`), `client/src/lib/api.ts` (`drillVisualization`, `exportDrill`, `drillQuery`, `exportDrillQuery`), `ChartPanel.tsx`, `client/src/features/dashboards/ChartTile.tsx`, `QueryEditorPage.tsx`/results grid

**Interfaces:**
- URL query: `s` = `encodeTrail(steps)`, `f` = `encodeFilters(crossFilters)`, `d` = dashboardId, `t` = tileId, `p_<name>` parameter values (existing convention).
- Query keys: `['drill', queryId, vizId ?? null, s, f, d, t, params]`.

Behaviour (spec §6.2, §6.3): page header = Back, trail crumbs (each navigates to a shorter `s`), zone chip (`useMe`), read-only parameter values, row count (next to the clicked number when the clicked measure was a `Count`; carry it in the URL as `n`), truncation banner (reuse the results banner), Export ▾ (CSV/XLSX via `lib/download.ts`; the blob-error interceptor already parses JSON errors), Copy link (fallback when `navigator.clipboard` is undefined on http: select a read-only input). Empty result text is the ordinary empty-result text. Breakdown: `ChartPanel`/`ChartTile` keep a local `{ steps, breakdownColumn }[]` stack; when non-empty they render `DrillResultDto` with its returned `options`, show `DrillTrail` above the chart, and pop on crumb click; dashboard refresh clears the stack. Results grid: on a saved, aggregated query, clicking a row opens `/queries/:id/drill?s=…` with every non-aggregated column of that row.

- [ ] **Step 1:** Implement; `npm run build`, `npm test` pass.
- [ ] **Step 2: Browser check** on plain http (route `http://qb.test/**` to localhost:5080 per CLAUDE.md) as persona Program-admin 10: pie slice → See rows → only ProgramId 10 rows, trail and zone chip shown; Copy link works; open the copied link as Program-admin 20 persona → only their rows (likely empty); Breakdown by → chart redraws, crumb returns; export downloads; results-grid row click opens the page.
- [ ] **Step 3:** Commit "Drill page, breakdown trail and results-grid drill".

---

## Task 11: Dashboard filter bar and the author switch (client)

**Files:**
- Create: `client/src/features/dashboards/CrossFilterBar.tsx`
- Modify: `client/src/pages/DashboardPage.tsx`, `ChartTile.tsx`, `DashboardNameDialog.tsx` (switch "Allow click-to-filter"), `client/src/hooks/useDashboards.ts`, `client/src/lib/api.ts`

Behaviour (spec §6.4): filter state lives in `f` in the dashboard URL; "Filter this dashboard" appends `{ sourceTileId, column, value }` (max 5, replacing an existing filter on the same column); `CrossFilterBar` shows `Label = value ✕` chips and Clear all; tiles send `crossFilters` with their data request and show a small "not filtered by X" mark from `crossFilter.notApplied`; with the switch off the menu item is hidden and, if the URL still carries `f`, the bar shows "Click-to-filter is off for this dashboard" and tiles render unfiltered (no request with filters). Saving the switch invalidates the dashboard detail query key.

- [ ] **Step 1:** Implement; build and tests pass.
- [ ] **Step 2: Browser check** on "Program overview" as Program-admin 10: click a slice → Filter this dashboard → matching tiles narrow, others show the mark; chip ✕ restores; turn the switch off → item gone, old link shows the notice.
- [ ] **Step 3:** Commit "Dashboards: click-to-filter bar and switch".

---

## Task 12: Docs, bundle and acceptance

**Files:**
- Modify: `README.md`, `src/QueryBuilder.Editor/README.md` ("Drill-down" section: actions, who can drill, columns rule, zone guarantee, switch, limits; "What's included"/"Roadmap"), `src/QueryBuilder.Editor/Scripts/README.md` (new column; script regenerated at release)
- Modify: `src/QueryBuilder.Editor/wwwroot/` (fresh bundle per CLAUDE.md release step 2)

- [ ] **Step 1:** `cd client && npm run build`; replace `wwwroot/assets` and `index.html` as in CLAUDE.md.
- [ ] **Step 2:** Full suite: `dotnet test tests/QueryBuilder.Tests` on net8.0 and net10.0 with `QB_TEST_POSTGRES` and `QB_TEST_METADATA_SQLSERVER` set; `cd client && npm test`. Report counts and any skips honestly.
- [ ] **Step 3:** Revert any `TEMP-DATASCOPE-TEST` edits in `src/QueryBuilder.Api/Program.cs`; `git status` shows no stray files staged.
- [ ] **Step 4:** Commit "Docs and bundle for chart drill-down". Stop: version bump to 2.2.0, DBA script `QueryBuilder.schema.2.2.0.sql`, pack and push happen only when the user asks.
