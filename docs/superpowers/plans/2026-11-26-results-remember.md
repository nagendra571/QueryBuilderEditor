# Results grid phase 5 (saved layout per query, Chart this column) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Owners/editors save the results grid layout with a saved query so everyone who opens it gets it, and any column can be charted in one click from its header menu.

**Architecture:** New nullable `SavedQueries.LayoutJson` column written only by a new `PUT /api/queries/{id}/layout` endpoint (opaque, versioned JSON; server checks shape `v: 1` and size). The client owns the format: a pure `saved-layout.ts` turns a `GridView` into a saved layout and back (defensively), and `useResultsGrid` uses the applied saved layout as its baseline (first load, Reset, column-set change). "Chart this column" is client-only: a pure `chartForColumn` pre-fills the existing `ChartEditorDialog` through a new `initial` prop.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core (net8 EF 9 / net10 EF 10), xUnit; React 19, TS, TanStack Query, shadcn/radix, Node's built-in test runner.

**Spec:** `docs/superpowers/specs/2026-11-26-results-remember-design.md` (read it, including "Settled after the builder check").

## Global Constraints

- No new packages (server or client).
- Every endpoint reads bodies with `http.Request.ReadFromJsonAsync<T>(QueryBuilderJson.Options, ct)` and writes with `Results.Json(x, QueryBuilderJson.Options)`.
- Plain-http rule: no `navigator.clipboard`, `crypto.*` anywhere in `client/src` (test `client/tests/insecure-context.test.ts` enforces it, comments included).
- Zustand selectors never return newly built arrays/objects.
- Saved parts: `order`, `hidden`, `pinnedLeft`, `pinnedRight`, `widths`, `wrap`, `formats`, `totals`. Never saved: `sort`, `search`, `filters`, `density`.
- Layout JSON: `{ "v": 1, ... }`; cap `GetRawText().Length <= 262144`; `null` clears.
- Audit: `AuditAction.QueryLayoutSaved = 38`; summaries exactly `Saved the results layout of query '{Name}'` / `Cleared the results layout of query '{Name}'`; no layout content in details.
- Saving a layout never changes `DefinitionJson`, `Name`, `UpdatedAtUtc`, `UpdatedBy`.
- Copy (exact): toast after save `Layout saved for everyone who opens this query.`; toast after clear `Saved layout cleared.`; clear confirm text `Everyone will see the plain grid again. Your current view stays until you reset it.`; disabled reasons `Save the query to add charts.` and `Save the query first — this column isn't in the saved version.`
- Comments explain *why* only. Stay in the existing Zoning Map look (reuse toolbar button/menu components).
- Migration command: `dotnet tool run dotnet-ef migrations add AddSavedQueryLayout --project src/QueryBuilder.Infrastructure --startup-project src/QueryBuilder.Api --framework net10.0 --output-dir Persistence/Migrations` (stop any running sample app first; it locks Debug output).
- Never `git add -A`; stage exact files. Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. A saved layout naming columns the query no longer produces (renamed/removed) → those names silently dropped, new columns appended at the end, grid still renders. (Task 2 tests.)
2. A saved layout that hides every remaining column → hidden list dropped so the grid never shows zero columns. (Task 2 test.)
3. Junk in `LayoutJson` (wrong `v`, string instead of array, unknown rule op/colour, 50 rules, negative widths) → parsed field by field, never throws. (Task 2 tests.)
4. A read-only share (query Viewer) calls the endpoint directly → 403, nothing stored, no audit. (Task 1 test.)
5. Editor saves a layout while the grid shows results → the visible view doesn't jump and the Save button turns clean (the new baseline equals the view). (Task 3 test on the pure baseline helper + acceptance.)

---

### Task 1: Server — LayoutJson column, layout endpoint, DTO

**Files:**
- Modify: `src/QueryBuilder.Domain/Entities/SavedQuery.cs` (add `public string? LayoutJson { get; set; }`)
- Modify: `src/QueryBuilder.Domain/Enums/Enums.cs` (add `QueryLayoutSaved = 38` after `QueryDrilled = 37`)
- Modify: `src/QueryBuilder.Infrastructure/Persistence/Configurations/SavedQueryConfiguration.cs` (`LayoutJson` → `nvarchar(max)`, not required)
- Create: migration `AddSavedQueryLayout` (generated; must contain only the AddColumn/DropColumn for `LayoutJson` plus snapshot change — no `AppId` alterations)
- Create: `src/QueryBuilder.Application/Commands.SavedQueries/SaveQueryLayoutCommand.cs`
- Modify: `src/QueryBuilder.Application/Dtos/SavedQueryDtos.cs` (`SavedQueryDetailDto` gains trailing `JsonElement? Layout`; add `public sealed record SaveQueryLayoutRequest(JsonElement? Layout);`)
- Modify: `src/QueryBuilder.Application/Queries.SavedQueries/GetSavedQueryByIdQuery.cs` (fill `Layout` from `LayoutJson`: `null` when null/blank, else `JsonDocument.Parse(json).RootElement.Clone()`; an unparsable stored value → `null`, never a 500)
- Modify: `src/QueryBuilder.Editor/Endpoints/SavedQueryEndpoints.cs` (add `group.MapPut("/{id:guid}/layout", …)` returning `Results.Json(new { hasLayout }, QueryBuilderJson.Options)`; no `.AllowViewers()`)
- Modify: `tests/QueryBuilder.Tests/Hub/CrossAppEndpointMatrixTests.cs` (add `["PUT /api/queries/{id}/layout"] = _ => new { layout = new { v = 1 } }` next to the disable entry; follow whatever else that file requires for a new route)
- Create: `tests/QueryBuilder.Tests/SaveQueryLayoutCommandTests.cs` (use `VizFixture`: `f.Queries`, `f.User`, `f.Audit`, users `VizFixture.Owner/EditorUser/ViewerUser/Stranger`)

**Interfaces:**
- Produces: `public sealed record SaveQueryLayoutCommand(Guid Id, JsonElement? Layout) : IRequest<bool>;` (returns whether a layout is now stored); handler `SaveQueryLayoutCommandHandler(ISavedQueryRepository repository, ICurrentUserService currentUser, IAuditLogger auditLogger)`.
- Produces (HTTP): `PUT /querybuilder/api/queries/{id}/layout` body `{ "layout": {...} | null }` → `{ "hasLayout": bool }`; `GET /api/queries/{id}` JSON gains `"layout": {...} | null` (an object, not a string).

Rules for the handler (in this order): not found → `NotFoundException(nameof(SavedQuery), id)`; `!SavedQueryAccess.CanEdit(existing, AccessActor.From(currentUser))` → `ForbiddenException("You have view-only access to this query.")`; layout present but not an object, or no `v` property that is a number equal to 1, → `ValidationException`-style 400 (use the exception type other commands use for 400 bad input — check `Application/Exceptions`) with message `The layout must be a JSON object with "v": 1.`; `GetRawText().Length > 262144` → 400 `The layout is too large.`; then set `LayoutJson` (`GetRawText()` or `null` for a JSON null/missing layout), `repository.Update`, `SaveChangesAsync`, audit. Works when `IsDisabled`.

- [ ] **Step 1: Write failing tests** in `SaveQueryLayoutCommandTests.cs`:
  - `Owner_and_editor_can_save_a_layout` (Theory Owner/EditorUser): `LayoutJson` contains `"v":1`, returns `true`, one audit entry `QueryLayoutSaved` with summary `Saved the results layout of query '<name>'`.
  - `Viewer_and_stranger_are_refused` (Theory ViewerUser/Stranger): `ForbiddenException`, `LayoutJson` unchanged, no audit.
  - `Unknown_query_is_not_found`.
  - `Null_layout_clears_it`: preset `LayoutJson`, send `null` → `LayoutJson == null`, returns `false`, summary `Cleared the results layout of query '<name>'`.
  - `Layout_must_be_an_object_with_v_1` (Theory over `[1,2]`, `"x"`, `{}`, `{"v":2}`, `{"v":"1"}`): throws the 400 exception, nothing stored.
  - `Layout_v_1_point_0_is_accepted`.
  - `Too_large_layout_is_refused`: object with a 262,200-char string → 400; one exactly at the limit (build so `GetRawText().Length == 262144`) → accepted.
  - `Saving_a_layout_leaves_definition_and_update_stamp_alone`: `DefinitionJson`, `Name`, `UpdatedAtUtc`, `UpdatedBy` equal before/after.
  - `Disabled_query_still_saves_layout`.
  - `Detail_query_returns_layout_as_object` (via `GetSavedQueryByIdQueryHandler`): `Layout.Value.ValueKind == JsonValueKind.Object`; with `LayoutJson = "not json"` → `Layout == null`.
- [ ] **Step 2: Run** `dotnet test tests/QueryBuilder.Tests --framework net10.0 --filter SaveQueryLayoutCommandTests` → fails to compile/fail.
- [ ] **Step 3: Implement** entity, enum, configuration, DTO, command, detail mapping, endpoint; generate the migration with the Global Constraints command and check its `Up` only adds `LayoutJson`.
- [ ] **Step 4: Run** the filter again → PASS. Then `dotnet test tests/QueryBuilder.Tests --framework net10.0 --filter "FullyQualifiedName~Hub|FullyQualifiedName~SaveQueryLayout|FullyQualifiedName~AppModel"` → PASS (matrix, viewer allowlist, model tests). Then `dotnet build` (both targets compile).
- [ ] **Step 5: Commit** the listed files + migration files (`*_AddSavedQueryLayout.cs`, `.Designer.cs`, `AppDbContextModelSnapshot.cs`): `Saved queries: store a results layout (LayoutJson + PUT /layout)`.

---

### Task 2: Client — pure saved-layout module

**Files:**
- Create: `client/src/features/builder/results/saved-layout.ts`
- Create: `client/tests/saved-layout.test.ts`

**Interfaces:**
- Consumes: `GridView`, `defaultView` (`results/grid-view.ts`); `ColumnFormat`, `HighlightRule`, `MAX_RULES`, `RuleColor`, `RuleOp`, `NumberStyle`, `DatePattern`, `ScaleKind` (`results/grid-format.ts`); `TotalFn` (`results/grid-totals.ts`); `MIN_COL_WIDTH = 60`, `MAX_COL_WIDTH = 480` (`results/column-widths.ts`). Relative `.ts` imports so Node runs it.
- Produces:
  ```ts
  export interface SavedLayout {
    v: 1
    order: string[]; hidden: string[]; pinnedLeft: string[]; pinnedRight: string[]
    widths: Record<string, number>; wrap: boolean
    formats: Record<string, ColumnFormat>; totals: { on: boolean; fns: Record<string, TotalFn> }
  }
  export function toSavedLayout(view: GridView): SavedLayout
  export function parseSavedLayout(raw: unknown): SavedLayout | null
  export function applySavedLayout(layout: SavedLayout | null, columns: string[]): GridView
  export function layoutPartsEqual(view: GridView, baseline: GridView): boolean
  export function isPlainLayout(view: GridView, columns: string[]): boolean
  ```
  `layoutPartsEqual` compares only the saved parts (order, hidden, pins, widths, wrap, formats, totals; order-sensitive for `order`/pins, set-equal for `hidden`, deep for formats/totals). `isPlainLayout` = saved parts equal `defaultView(columns)`'s.

Parsing rules: anything not an object or `v !== 1` → `null`. Per field: non-array → `[]`, non-string entries dropped, duplicates dropped; widths: finite numbers only, rounded, clamped to 60–480; `wrap` non-boolean → `false`; formats: per column keep only known `number.style`/`date.pattern`/`scale` values, `bars` boolean, rules with known `op` and `color`, string `value`/`value2` (non-string → `''`), first `MAX_RULES` only, rule `id` kept if string else regenerated as `r${index}` (no crypto); a format with nothing usable left is dropped; totals `fns` only known `TotalFn` values.

Apply rules: start from `defaultView(columns)`; `order` = saved names present in `columns`, in saved order, then the remaining `columns` in query order; drop every name not in `columns` from hidden/pins/widths/formats/totals.fns; a name in both pin lists stays left only; if hidden would cover every column, `hidden = []`; `sort/search/filters/density` stay default.

- [ ] **Step 1: Write failing tests** in `client/tests/saved-layout.test.ts` (Node `node:test` + `assert/strict`, import from `../src/features/builder/results/saved-layout.ts`):
  - `round trip keeps saved parts`: a view with order/hidden/pins/widths/wrap/formats (one rule)/totals → `applySavedLayout(parseSavedLayout(JSON.parse(JSON.stringify(toSavedLayout(view)))), cols)` has `layoutPartsEqual(…, view) === true`.
  - `toSavedLayout drops sort, search, filters and density`: result has no such keys.
  - `stale names are dropped and new columns appended`: saved order `['B','X','A']`, columns `['A','B','C']` → order `['B','A','C']`; `hidden:['X']` → `[]`; `widths.X` gone.
  - `never hides every column`: hidden `['A','B']`, columns `['A','B']` → hidden `[]`.
  - `pinned both sides stays left`.
  - `junk input never throws`: `parseSavedLayout` of `null`, `'x'`, `[]`, `{v:2}` → `null`; `{v:1, order:'A', widths:{A:-5,B:'9',C:9999}, formats:{A:{rules:[{op:'nope',color:'red'}], bars:'yes'}}, totals:{on:'y', fns:{A:'median'}}}` → `order []`, `widths {A:60, C:480}`, `formats {}`, `totals {on:false, fns:{}}`.
  - `rules are capped at five`: 7 valid rules → 5.
  - `isPlainLayout` true for `defaultView(cols)`, false after one width.
- [ ] **Step 2: Run** `cd client && node --test tests/saved-layout.test.ts` → FAIL (module missing).
- [ ] **Step 3: Implement** `saved-layout.ts` per the rules above.
- [ ] **Step 4: Run** `cd client && node --test tests/saved-layout.test.ts` → PASS; `npm test` → all pass.
- [ ] **Step 5: Commit** `Results grid: saved layout format (to/parse/apply)`.

---

### Task 3: Client — saved layout in the grid, Layout menu, page wiring

**Files:**
- Modify: `client/src/types/index.ts` (or wherever `SavedQueryDetailDto` lives — `client/src/types/*.ts:221`): add `layout: unknown | null`
- Modify: `client/src/lib/api.ts` (`savedQueriesApi.saveLayout(id: string, layout: SavedLayout | null): Promise<{ hasLayout: boolean }>` → `PUT /queries/{id}/layout` with `{ layout }`)
- Modify: `client/src/hooks/useSavedQueries.ts` (`useSaveQueryLayout(queryId: string)`: on success invalidate the query page's detail key — use the exact key `QueryEditorPage`'s `existingQuery` uses (line ~75) — and `savedQueriesKey`; on error toast the `ApiError` message or `Could not save the layout.`)
- Modify: `client/src/features/builder/results/useResultsGrid.ts`
- Modify: `client/src/features/builder/results/ResultsToolbar.tsx`
- Modify: `client/src/features/builder/ResultsTable.tsx`
- Modify: `client/src/pages/QueryEditorPage.tsx`
- Modify: `client/src/features/builder/results/saved-layout.ts` + `client/tests/saved-layout.test.ts` (add `nextBaseline` below)

**Interfaces:**
- Consumes: Task 2's exports; Task 1's endpoint and `layout` field.
- Produces:
  - `useResultsGrid(result: QueryResultDto, savedLayout?: unknown)`; returned object adds `baselineIsSaved: boolean` (a saved layout exists), `layoutDirty: boolean` (`!layoutPartsEqual(view, baseline)`), `currentLayout: () => SavedLayout`, and `isDefault` now means "view equals baseline incl. sort/search/filters/density defaults"; `reset()` sets the view to the baseline.
  - `ResultsTable` new optional props: `savedLayout?: unknown`, `canSaveLayout?: boolean`, `onSaveLayout?: (layout: SavedLayout | null) => void`, `isSavingLayout?: boolean`, `layoutRef?: React.MutableRefObject<(() => { layout: SavedLayout; plain: boolean }) | null>` (filled by `ResultsTable` so the page can read the shown layout for save-as).
  - Pure helper in `saved-layout.ts`: `export function shouldAdoptBaseline(view: GridView, oldBaseline: GridView, newBaseline: GridView): boolean` — true when the view's saved parts equal the old baseline (untouched) — used so a late-loading or newly saved layout replaces an untouched view but never a view the user changed.

Behaviour:
- Baseline = `applySavedLayout(parseSavedLayout(savedLayout), names)` (memoised on `savedLayout` + names). Used for the first result, `reset`, and in place of `defaultView` inside the column-set-change branch of `carryOver` (pass the baseline in; same-set carry-over unchanged).
- When `savedLayout` changes: if `shouldAdoptBaseline(view, oldBaseline, newBaseline)` set the view to the new baseline (keeping current sort/search/filters/density); otherwise leave the view. After Save layout the server echo equals the view, so `layoutDirty` becomes false without a jump.
- Toolbar: when `canSaveLayout`, a "Layout" `DropdownMenu` button (icon `LayoutTemplate` from lucide-react, label "Layout", a `size-1.5 rounded-full bg-primary` dot with `aria-label="Unsaved layout changes"` when `layoutDirty && !(no saved layout && isPlainLayout)`) with items **Save layout** (disabled unless that same dirty condition; `isSavingLayout` disables) and **Clear saved layout** (disabled unless `baselineIsSaved`) which opens an `AlertDialog`/`Dialog` with the clear confirm copy and "Clear layout"/"Cancel". Full-screen mode shows the same menu.
- Page: `canSaveLayout = canEditCharts`; pass `savedLayout={existingQuery.data?.layout}` only when `queryId`; `onSaveLayout` calls the mutation, toasts the save/clear copy on success. Save-as copy: in `saveMutation`'s success path when `input.asCopy`, call `layoutRef.current?.()`; unless it is missing or `plain` (`isPlainLayout`), PUT `layout` to the new id; on failure `toast.error('The copy was saved, but its layout could not be.')`.

- [ ] **Step 1: Write failing tests** (append to `client/tests/saved-layout.test.ts`): `shouldAdoptBaseline true for an untouched view`, `false after a width change`, `true when the view already equals the new baseline` (save echo).
- [ ] **Step 2: Run** `cd client && node --test tests/saved-layout.test.ts` → FAIL.
- [ ] **Step 3: Implement** `shouldAdoptBaseline`, then the hook, toolbar, `ResultsTable`, API, hook, and page wiring above.
- [ ] **Step 4: Run** `cd client && npm test` → PASS; `npx tsc -b` (or `npm run build`) → no errors; `npm run lint` if present → clean for touched files.
- [ ] **Step 5: Commit** `Results grid: save layout with the query for everyone who opens it`.

---

### Task 4: Client — Chart this column

**Files:**
- Modify: `client/src/features/visualizations/chart-data.ts` (add `chartForColumn`)
- Create: `client/tests/chart-for-column.test.ts`
- Modify: `client/src/features/visualizations/ChartEditorDialog.tsx` (new optional prop `initial?: ChartStart | null`, used only when `editing` is null, seeding `type`, `x`, `measures`)
- Modify: `client/src/features/builder/results/GridHeaderCell.tsx` (`HeaderActions` gains `chart?: { onSelect: () => void; disabledReason?: string }`; menu item "Chart this column" after "Format…": hidden when `chart` is undefined; when `disabledReason` set → `DropdownMenuItem disabled` with the reason as a second muted line)
- Modify: `client/src/features/builder/results/ResultsGrid.tsx` / `useResultsGrid` wiring as needed to build `actions.chart`
- Modify: `client/src/features/builder/results/DetailsPanel.tsx` (Column tab: a "Chart this column" button with the same enabled/disabled rule)
- Modify: `client/src/features/builder/ResultsTable.tsx` (new optional prop `chartColumn?: { onChart: (column: string) => void; reasonFor: (column: string) => string | undefined }`; when invoked while `fullScreen`, `setFullScreen(false)` then call `onChart` in `requestAnimationFrame`)
- Modify: `client/src/pages/QueryEditorPage.tsx` (state `chartStart: ChartStart | null`; Add chart button and Edit reset it to null; pass `initial={chartStart}`)

**Interfaces:**
- Consumes: `OutputColumn`, `defaultRoles` (`chart-data.ts`); page's `canEditCharts`, `savedDefinition`, `outputColumns(...)`, `setEditingChart`, `setChartEditorOpen`.
- Produces: `export interface ChartStart { type: VisualizationType; x: string; measures: string[] }` and `export function chartForColumn(column: string, columns: OutputColumn[]): ChartStart` in `chart-data.ts`.

`chartForColumn` rules (with `d = defaultRoles(columns)`; `col` = the column's `OutputColumn`):
- `col.dataType === 'number'` → `{ type: 'bar', x: d.x !== column ? d.x : (first column with dataType !== 'number' and name !== column)?.name ?? '', measures: [column] }`
- `'date' | 'dateTime'` → `{ type: 'line', x: column, measures: d.measure && d.measure !== column ? [d.measure] : [] }`
- otherwise → `{ type: 'bar', x: column, measures: d.measure && d.measure !== column ? [d.measure] : [] }`
- column not found → `{ type: 'bar', x: '', measures: [] }`.

Page rules: Viewers (`isViewer`) → `chartColumn` not passed (item hidden). No `queryId`/`existingQuery.data` → `reasonFor` returns `Save the query to add charts.`. Column not in `outputColumns(savedDefinition.columns)` names → `Save the query first — this column isn't in the saved version.`. Otherwise `onChart` sets `editingChart` null, `chartStart = chartForColumn(column, outputColumns(savedDefinition.columns))`, opens the editor. Note the dialog is rendered only when `savedDefinition` exists; for an unsaved query the item is disabled so nothing opens.

- [ ] **Step 1: Write failing tests** `client/tests/chart-for-column.test.ts`, columns: `Region` text (non-aggregate), `Day` date, `Sales` number aggregate, `Qty` number:
  - number → `{type:'bar', x:'Region', measures:['Sales']}`
  - date → `{type:'line', x:'Day', measures:['Sales']}`
  - text → `{type:'bar', x:'Region', measures:['Sales']}`
  - aggregate measure itself as text-less: columns only `[Sales]` → `{type:'bar', x:'', measures:['Sales']}`
  - date column when the only number is absent → `measures: []`
  - unknown column → `{type:'bar', x:'', measures:[]}`
- [ ] **Step 2: Run** `cd client && node --test tests/chart-for-column.test.ts` → FAIL.
- [ ] **Step 3: Implement** `chartForColumn`, dialog `initial`, header item, details action, ResultsTable prop with full-screen exit, page wiring.
- [ ] **Step 4: Run** `cd client && npm test` → PASS; `npm run build` → OK.
- [ ] **Step 5: Commit** `Results grid: Chart this column opens the chart editor pre-filled`.

---

### Task 5: Docs, tracker, bundle

**Files:**
- Modify: `README.md` and `src/QueryBuilder.Editor/README.md` ("Working with query results": replace `The layout, formats and totals are not remembered across visits yet.` with a "Saved layout" bullet (who can save, what's included/excluded, Reset goes to the saved layout, viewers' changes last for the visit, Save as new query copies the shown layout) and a "Chart this column" bullet; mention the new `SavedQueries.LayoutJson` column wherever the READMEs list schema changes for the next release)
- Modify: `src/QueryBuilder.Editor/Scripts/README.md` only if it lists per-version schema changes (add "next release: `SavedQueries.LayoutJson`, migration `AddSavedQueryLayout`")
- Modify: `docs/superpowers/specs/2026-11-25-results-panel-features.md` (#17, #18 → `Done (unreleased)`)
- Replace: `src/QueryBuilder.Editor/wwwroot/assets` + `wwwroot/index.html` from a fresh `cd client && npm run build` (steps in CLAUDE.md release workflow step 2)

- [ ] **Step 1:** Edit docs and tracker.
- [ ] **Step 2:** `cd client && npm test && npm run build`, copy the bundle into `wwwroot` (rm old assets first).
- [ ] **Step 3:** `git status` — stage only the docs, tracker, `wwwroot/index.html`, and the new/removed `wwwroot/assets` files.
- [ ] **Step 4: Commit** `Results grid phase 5: docs and bundle`.
