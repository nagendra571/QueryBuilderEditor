# Results grid phase 5: saved layout per query (#17) and "Chart this column" (#18)

Tracker: `docs/superpowers/specs/2026-11-25-results-panel-features.md` items #17 and #18.
Phases 1–4 (grid, details panel, filters, formats/totals) are merged to `main` and unreleased.

## Why

Business users prepare a query once and others open it many times. Today every grid setting from
phases 1–4 (order, widths, pins, hidden columns, formats, highlight rules, bars, totals row) is lost
when the page closes, so whoever opens the query starts from a raw grid. The person who prepares a
query should be able to set how its results look **for everyone who opens it**. Separately, the
grid is where people find something worth charting; going from a column to a chart should be one
click instead of opening the chart editor and picking roles by hand.

## Decisions made with the user

- **Who sees a saved layout: everyone who opens the query (option C).** The owner or an editor
  saves it; it becomes how the grid opens for everyone the query is shared with.
- **How: a separate saved layout with its own Save layout button (approach 1).** A new nullable
  column on `SavedQueries` and a small endpoint that saves only the layout. Saving a layout does not
  save or re-validate the query definition.

## Approaches considered

1. **Separate `LayoutJson` column + own endpoint + explicit "Save layout" (chosen).** One migration,
   DBA script, minor version. A width tweak never re-runs catalog/scope validation, never asks to
   save unsaved builder edits, never logs "query updated".
2. **Layout inside `DefinitionJson`.** No schema change, but every layout tweak needs a full query
   save, and the definition travels with every run/export/preview and is the object security checks
   read — layout data would ride along with all of them. Rejected.
3. **Like 1 but auto-saved as editors change things.** No button, but every drag instantly changes
   what everyone sees, accidental changes break shared layouts, and each change writes to the DB.
   Rejected.

## Goals

- Owners/editors can save the current grid layout with the query, and clear it.
- Anyone who can open the query (owner, editors, viewers) gets the saved layout when results load.
- Viewers (and editors who don't save) can still change things for the current visit only.
- Reset view returns to the saved layout, not the plain default.
- "Chart this column" in the column header menu opens the chart editor pre-filled with that column.

## Non-goals

- Per-person layouts, per-browser memory of layouts (option A/B — not chosen).
- Remembering sort, grid filters or search (they change which rows are shown; the query has its own
  sort and filters for that).
- Applying the saved layout to dashboards' Table charts, drill pages or exports (exports stay raw).
- Layout history/undo beyond "Clear saved layout".
- Charts drawn from unsaved builder state (charts stay drawn from the saved definition).

## Anti-goals

- Saving a layout must never change the query definition, its `UpdatedAtUtc`, or what SQL runs.
- A saved layout must never be able to break the grid: unknown, stale or malformed parts are
  dropped silently on load, never an error screen.
- Viewers must never be able to change what other people see.

## What a saved layout contains

From `GridView` (`client/src/features/builder/results/grid-view.ts`):

| Saved | Not saved |
|---|---|
| `order`, `hidden`, `pinnedLeft`, `pinnedRight`, `widths`, `wrap`, `formats` (number/date formats, rules, bars, scales), `totals` (`on`, `fns`) | `sort`, `search`, `filters`, `density` |

Density is excluded (my call): it is a personal reading preference, already remembered per browser
by `state/density-store.ts`; a shared layout overriding someone's compact/comfortable choice would
feel wrong. Wrap is included because it is about this query's content (long text columns).

Stored shape (JSON, camelCase), versioned so it can evolve:

```json
{ "v": 1, "order": [...], "hidden": [...], "pinnedLeft": [...], "pinnedRight": [...],
  "widths": { "Name": 180 }, "wrap": false, "formats": { ... }, "totals": { "on": true, "fns": { ... } } }
```

Columns are identified by output name (the same names the grid already uses).

## Server

- **Entity/migration:** `SavedQuery.LayoutJson` (`string?`), column `SavedQueries.LayoutJson
  nvarchar(max) NULL`. Migration `AddSavedQueryLayout` (adds one nullable column; no `AlterColumn`
  on any `AppId`, so the `DF_<Table>_AppId` defaults are untouched).
- **Read:** `SavedQueryDetailDto` gains `Layout` (raw JSON, returned as a JSON object or `null`, not
  a string). Every caller that can view the query gets it.
- **Write:** `PUT /api/queries/{id}/layout`, body `{ "layout": { ... } | null }`, read with
  `ReadFromJsonAsync` + `QueryBuilderJson.Options` like every endpoint. `null` clears it.
  Handler `SaveQueryLayoutCommand`:
  - 404 if the query doesn't exist (or is in another app — the app filter handles this);
  - 403 unless `SavedQueryAccess.CanEdit` (owner or Editor share, admins per existing rules) —
    same message style as `SaveQueryCommand`;
  - 400 unless the body is a JSON object with `"v": 1` and its serialized size is ≤ 256 KB
    (262,144 characters) — the server does not interpret the rest (the client owns the format and
    validates on load);
  - writes only `LayoutJson` (does not touch `UpdatedAtUtc`/`UpdatedBy`, definition, name);
  - audits a new `AuditAction.QueryLayoutSaved = 38` ("Saved the results layout of query 'X'" /
    "Cleared the results layout of query 'X'"), no layout content in the details.
  - Works on disabled queries too (it's presentation, not execution).
  - Not `.AllowViewers()`: a hub Viewer has no query page.
- **Concurrency:** last write wins (two editors saving layouts at once is rare and harmless).
- **Save as a new query:** the copy starts with the layout currently shown (see "Settled after the
  builder check"). Done client-side: after the copy is created, the client PUTs it to the new id. A failure there
  only shows a toast; the copy still exists.
- **Delete:** goes with the row. Hub data-source move: unaffected (names only).

## Client

- **API/types:** `SavedQueryDetailDto.layout: unknown | null`; hook `useSaveQueryLayout(queryId)`
  that invalidates the saved-query detail key on success.
- **Pure module `results/saved-layout.ts`** (node-tested):
  - `toSavedLayout(view: GridView): SavedLayout` — the saved parts only, `v: 1`.
  - `parseSavedLayout(raw: unknown): SavedLayout | null` — defensive: wrong `v`, wrong types, junk
    entries are dropped field by field; returns `null` only when nothing usable is left. Rule/format
    entries are validated with the same limits the Format panel enforces (≤ 5 rules, known colours,
    known formats). Widths clamped to the grid's existing min/max.
  - `applySavedLayout(layout: SavedLayout | null, columns: string[]): GridView` — starts from
    `defaultView(columns)`, then: `order` = saved names that still exist, in saved order, followed by
    columns not in the saved order in their query order; `hidden`/pins/widths/formats/totals keep only
    names that still exist; never hides every column (if the saved layout would, hidden is dropped);
    a column can't be pinned both sides (left wins). Sort/search/filters/density stay default.
  - `sameLayout(view, layout, columns): boolean` — whether the current view's saved parts equal the
    applied saved layout (drives the "unsaved layout changes" state).
- **Grid integration (`useResultsGrid` / `ResultsTable` props):**
  `savedLayout?: unknown`, `canSaveLayout?: boolean`, `onSaveLayout?(layout: SavedLayout | null)`,
  `isSavingLayout?: boolean`.
  - **Baseline** = `applySavedLayout(parse(savedLayout), columns)` instead of `defaultView(columns)`:
    used for the first result, for Reset view, and when the column set changes (where `carryOver`
    currently resets to the default it now resets to the baseline for the new columns).
  - `isDefault` (shows the Reset button) compares against the baseline, ignoring nothing new.
  - When `savedLayout` arrives or changes after results are already shown (e.g. after Save layout
    returns, or the detail query loads late), the grid does **not** jump: the saved parts are already
    what the user sees after a save; on a late first load with an untouched view it applies the
    baseline.
- **Toolbar (results toolbar, only when `canSaveLayout`):** a "Layout" menu button with
  - **Save layout** — enabled when the view's saved parts differ from the saved layout (or there is
    none and the view isn't the plain default); toast "Layout saved for everyone who opens this
    query."
  - **Clear saved layout** — enabled when a layout is saved; a confirm ("Everyone will see the plain
    grid again. Your current view stays until you reset it."); toast on success.
  - A small dot on the button when there are unsaved layout changes (same dot language as the
    header format dot).
  - Viewers see no Layout menu; their changes are session-only as today.
- **Where it applies:** only on the query editor page for a saved query (`queryId` present). New,
  unsaved queries behave exactly as today. Drill pages and dashboard Table charts ignore it.
- **Unsaved builder changes:** saving a layout while the definition has unsaved edits is allowed; it
  stores names as they are now; names the saved query doesn't produce are simply dropped on load
  until the query is saved.

## "Chart this column" (#18)

- **Where:** a "Chart this column" item in the column header menu (`GridHeaderCell` menu), after
  "Format…". Also in the Details panel's Column tab actions (my call: same action, same rules).
- **Who/when it is enabled:** only when charts can be added today (`canEditCharts`: saved query,
  caller is not a Viewer) **and** the column exists in the saved definition's output columns
  (`outputColumns(savedDefinition.columns)`). Otherwise the item is shown disabled with the reason
  as its description:
  - unsaved query → "Save the query to add charts.";
  - Viewer → item hidden (Viewers can't add charts anywhere);
  - column not in the saved query → "Save the query first — this column isn't in the saved version."
- **What it opens:** the existing `ChartEditorDialog` in "Add chart" mode with a new optional prop
  `initial?: { type, x, measures }` used only when `editing` is null. Pre-fill (pure function
  `chartForColumn(column: OutputColumn, columns: OutputColumn[])` in
  `features/visualizations/chart-data.ts`, node-tested):
  - number column → `bar`, `x` = `defaultRoles(columns).x` (first non-aggregate column; if that is
    this column, the first other non-number column), `measures` = [this column];
  - date / dateTime column → `line`, `x` = this column, `measures` = [default measure];
  - text / boolean / other → `bar`, `x` = this column, `measures` = [default measure].
  - Missing roles stay empty; the editor's existing `optionsProblem` explains what to pick.
- **Full screen:** choosing it while the grid is in full screen exits full screen first, then opens
  the dialog (the dialog is portalled outside the full-screen root, which is `inert` underneath).
- On save, behaviour is exactly today's: the new chart tab opens.

## Constraints

- No new packages. Plain-http rule: no `navigator.clipboard`, `crypto.*` in `client/src`.
- Every endpoint uses `QueryBuilderJson.Options` explicitly.
- Zustand selectors never return new arrays/objects.
- The DB change needs: EF migration (command in CLAUDE.md, `--framework net10.0`), both READMEs
  ("Working with query results" → replace "The layout, formats and totals are not remembered across
  visits yet." with how saved layouts work), `Scripts/README.md` if it lists columns, tracker #17/#18
  "Done (unreleased)". DBA script regeneration happens at release (Editor 2.3.0 suggested), not now.
- Server tests: handler rules (404, 403 for viewer share, 400 bad `v`/non-object/too large, clear
  with null, definition/UpdatedAt untouched, audit written), endpoint matrix/cross-app tests updated
  for the new route, `AppModelTests` unaffected. Run on net10 and net8.
- Client tests: `saved-layout.ts` (stale names, new columns appended, all-hidden guard, junk input,
  pins both sides, round trip), `chartForColumn`.

## Settled after the builder check

- **Who gets the Layout menu:** `canSaveLayout = canEditCharts`. On `QueryEditorPage`, `isViewer` is
  `myAccessLevel === 'viewer'` from the detail DTO (the caller's *share* level, `AccessLevelFor`),
  so read-only shares get no menu and no 403. The DTO already carries it; no new field.
- **Renames (my call):** layouts key on output names; renaming or re-aliasing a column in the builder
  drops that column's saved settings on load. Accepted: names are what users see, and identity-based
  keys would complicate the format for a rare case.
- **Saved layout changing under an open page (my call):** the baseline is live: it follows the latest
  `savedLayout` from the detail query. The visible view never jumps by itself; Reset goes to the latest
  saved layout.
- **Save as a new query (my call, replaces the earlier line):** the copy gets the layout the person is
  *looking at* (`toSavedLayout(currentView)`), not the source's saved one, because they just arranged
  the grid. Anyone who can open a query can save a copy, and they own the copy, so this needs no extra
  permission. Skipped when the current view's saved parts are the plain default.
- **Size cap (my call):** 256 KB is checked on the raw request body length before parsing; `v` must be
  the JSON number `1` (`1.0` is accepted as the same number). The server stores the object as
  received (re-serialized); the client never uses any part of it without `parseSavedLayout`.
- **Chart this column with no possible X (my call):** stays a bar chart with an empty X; the editor's
  existing problem text asks for one.
- **Full screen (my call):** exit full screen, then open the dialog on the next frame, after `#root`
  is no longer inert; on close, focus goes to the page's normal place (the grid is no longer full
  screen).

## Left to the builder

- Exact component placement/visuals of the Layout menu inside the existing toolbar (stay in the
  Zoning Map look, match existing toolbar buttons).
- How `useResultsGrid` threads the baseline (props vs a small context), as long as the behaviour
  above holds.
- Whether the save-as-copy layout PUT lives in the save mutation or the page.
