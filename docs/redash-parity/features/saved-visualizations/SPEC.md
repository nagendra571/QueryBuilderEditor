# SPEC — Saved visualizations (Phase 1)

Status: **APPROVED 2026-11-02.** Decisions: U3 = **refuse to draw when truncated**; U4 = **Recharts 3 (MIT)** via the shadcn Chart pattern. (⚑ markers below are now resolved as stated.)

Goal: a business user can save one or more charts for an existing saved visual query, reopen
them later, and see them drawn from **their own** authorized results. Charts never show rows the
viewer could not get by running the query themselves.

---

## 1. Observed Redash behavior (reference only, no code reused)

Source: Redash 8.0.0 as running at `localhost:5000` (code copied from the container) and
v26.9.0 source. These are **source-observed and documented** facts. Runtime behavior has not
been observed yet (U1).

1.1 **Ownership.** A visualization belongs to exactly one query. It has a type, a name, an
optional description, and free-form options stored as JSON text (E-R01). Every new query gets a
default Table visualization (E-R17).

1.2 **Who can change it.** Creating, updating or deleting a visualization needs the
`edit_query` permission **and** modify rights on the parent query (E-R03). This is unchanged in
v26.9.0 (E-R04).

1.3 **Where users edit it.** Visualizations are edited from the query editor screen (E-R21).

1.4 **How data reaches it.** A visualization has no data of its own. It renders the parent
query's latest result, fetched through the query-results endpoints (E-R19). Results are cached
per (data source, query text) and served to any user with view access to the data source; the
cache key does not include the user (E-R06, E-R07, E-R08). View-only users may execute a
parameterized query only when every parameter is "safe" (E-R09, E-R10).

1.5 **Chart model.** The chart type offers Line, Bar (default), Area, Pie, Scatter, Bubble,
Heatmap and Box series. Columns are assigned roles (x / y / series) through a column mapping.
Defaults include sorted X, a legend, and *missing values treated as zero* (E-R15). A separate
Counter type shows one value from a chosen row and column (E-R16).

1.6 **Query edits.** The chart editor drops mappings whose column no longer exists. This happens
on the client; no server-side validation of options was found (E-R15, bounded search).

1.7 **Limits.** No chart-specific row cap was found in 8.0.0 (bounded search, U3). The Table
visualization pages 25 rows at a time by default (`table/index.js:15`).

Runtime confirmation probe (U1, **not yet run, needs your approval**): on `localhost:5000`, with
a synthetic Postgres/SQLite data source and two disposable users (one in an edit group, one
view-only), `capture_browser_scenario` would record create → save → reopen of a Bar chart, a
view-only render, and a render after a column is removed.

---

## 2. Desired QueryBuilderEditor behavior

### 2.1 Workflows

**W1 — Create.** On `/queries/:id` (saved query, user is owner or Editor), the user clicks
**Add chart** in the results area. A dialog (an existing shadcn `Dialog`) offers:
- chart type;
- name (default "<Type> of <measure>");
- X / category column, measure column(s) and optional series column. Pickers list only the query's
  visible output columns, and measure pickers list only numeric ones;
- for Bar: stacked on/off;
- X order: query order / ascending / descending.

The dialog shows a live preview using the current results (or runs the query). Saving persists
the visualization and opens it as a tab.

**W2 — Reopen.** Opening `/queries/:id` lists the saved visualizations as tabs next to
**Results** (Build · Results · *chart tabs* · History). Selecting a chart tab calls the render
endpoint (§2.4). If the query has required parameters without defaults, the existing
`ParameterPromptDialog` asks for values first.

**W3 — Edit / rename / delete.** Owner or Editor only, from the chart tab's menu. Delete asks
for confirmation.

**W4 — View-only user.** A Viewer share holder sees and renders the charts. The add/edit/delete
controls are hidden, **and** the server refuses those calls (403).

**W5 — Query changed after the chart was saved.** At render time, if a referenced column is no
longer an output of the saved definition (removed, renamed alias, or hidden), the chart shows
"This chart needs updating: column(s) X no longer exist." Owners and Editors also get an
**Edit chart** button. No partial chart is drawn.

### 2.2 Chart types (proposed set of 4)

| Type | Shape required | Why it's in the first set |
|---|---|---|
| **Bar** (vertical; grouped or stacked when a series column is set) | 1 category column + 1–5 numeric measures, or 1 measure + 1 series column | The most common business question is comparing totals across categories. It fits QBE's "totals with automatic grouping" model directly. Redash's own default series is Bar (E-R15) |
| **Line** | 1 X column (date/datetime/number/text) + 1–5 numeric measures, or 1 measure + 1 series column | Trend over time. Pairs with date columns and date parameters, which the builder already supports |
| **Pie** | 1 category column + exactly 1 numeric measure, **≤ 12 slices**, no negative values | Share of a whole, a common request from business users. The slice cap keeps it readable |
| **Counter (KPI)** | 1 numeric column; value taken from row 1 | A single headline number (e.g. "Total enrolments"), shown as a large figure with a label. Redash ships a comparable type (E-R16) |

The existing results grid stays the "table" view. Unlike Redash (E-R17), no Table visualization
row is auto-created (intentional difference: nothing changes for existing queries).

**Excluded for now:** area, scatter/bubble, heatmap, box, maps, pivot, funnel, cohort, sankey,
sunburst, word cloud. They are rarer for business users, need more configuration, and are harder
to make accessible. Any of them can be added later behind the same `type` + versioned options
contract.

### 2.3 Data contract

Columns are referenced by **output name** (`QueryColumn.EffectiveAlias`), the same key the result
rows use (`QueryResultDto.Rows` is `Dictionary<string, object?>` keyed by that name).

`VisualizationOptions` (JSON, `version: 1`):

```json
{
  "version": 1,
  "x": "Region",
  "measures": ["TotalSales"],
  "series": null,
  "stacked": false,
  "sortX": "none",
  "counter": null
}
```
- `x`: required for bar/line/pie; null for counter.
- `measures`: 1–5 numeric output columns. Exactly 1 when `series` is set or the type is pie.
- `series`: optional for bar/line; null for pie/counter.
- `stacked`: bar only.
- `sortX`: `none` | `asc` | `desc`.
- `counter`: `{ "value": "Total", "label": "Total enrolments", "decimals": 0 }` for counter only
  (`decimals` 0–4).

DTOs (camelCase via `QueryBuilderJson.Options`):

- `VisualizationDto { id, savedQueryId, name, type: "Bar"|"Line"|"Pie"|"Counter", options,
  displayOrder, createdBy, createdAtUtc, updatedBy?, updatedAtUtc? }`
- `SaveVisualizationRequest { name, type, options }`
- `RenderVisualizationRequest { parameterValues: { [name]: string|null } }`
- `VisualizationDataDto { columns: QueryResultColumnDto[] (only the referenced columns), rows,
  rowCount, executionTimeMs, rowLimit }`

### 2.4 API (all under `/querybuilder/api`, inside the existing authorization group)

| Method & route | Who | Behavior |
|---|---|---|
| `GET /queries/{id}/visualizations` | owner or any share holder | List in `displayOrder` |
| `POST /queries/{id}/visualizations` | owner or Editor | Validate options against the **saved** definition, then create |
| `PUT /queries/{id}/visualizations/{vid}` | owner or Editor | Same validation, then update |
| `DELETE /queries/{id}/visualizations/{vid}` | owner or Editor | Delete |
| `POST /queries/{id}/visualizations/{vid}/data` | owner or any share holder | Render (below) |

**Render pipeline (server-side, in order):**
1. Load the saved query **from the database**. The browser never supplies the definition (this
   closes D1/D4 for this path). Missing query → 404. The visualization not belonging to this
   query → 404.
2. Access: owner or a share holder, else 403. The check uses the share table at request time,
   so a revoked share takes effect on the next request.
3. `IsDisabled` → 409 "Query disabled".
4. Options vs current definition → if stale, 400 naming the missing columns (W5).
5. `catalogService.ValidateAsync` → `dataScopeGuard.AuthorizeAsync` (**the viewer's** scope) →
   `Build(definition, scopePredicates)`. These are the same calls `RunQueryCommand` makes.
6. Row cap = `min(effective record limit, ChartMaxRows = 1,000)`. Fetch cap+1 to detect
   overflow.
7. On overflow: **refuse** with 409 and a reason: "more than N records — add filters or totals"
   (record limit) or "too many points to chart (max 1,000)" (chart cap). (U3, approved.)
8. Pie with > 12 categories or negative values → 400 "Too many slices for a pie chart (max 12);
   use Bar." Counter with zero rows → 200 with empty data (UI shows "—").
9. Project the rows to the referenced columns only (data minimization).
10. Audit `QueryRun` with details `{ VisualizationId, RowCount, Scope }`. The `SavedQueryId`
    recorded is now server-verified.

Parameter values are validated and bound exactly as today (`@name` via
`QueryParameterNames`, typed `SqlParameter`). Visualizations do **not** store parameter values
in Phase 1. Defaults come from the saved query's `QueryParameter.DefaultValue`.

**No result caching.** Every render executes with the viewer's scope (intentional difference
from E-R06/E-R08). The client may keep the render response in TanStack Query memory per
(user session, vid, parameter values) for at most the page lifetime. Each identity runs in its
own browser session, so this cannot cross users.

### 2.5 Persistence

New table `QueryVisualizations`:
- `Id` (uniqueidentifier, PK);
- `SavedQueryId` (FK → `SavedQueries.Id`, **ON DELETE CASCADE**, indexed);
- `Name` nvarchar(200) not null;
- `Type` nvarchar(20) not null (enum stored as string);
- `OptionsJson` nvarchar(max) not null;
- `DisplayOrder` int not null;
- `CreatedBy` nvarchar(450) not null, `CreatedAtUtc`, `UpdatedBy` nvarchar(450) null,
  `UpdatedAtUtc`.

Limit: 20 visualizations per query (400 beyond that). New rows are added via the `DbSet`, never
through a navigation collection (`CLAUDE.md` EF sharp edge).

Audit enum gains (appended, values unchanged for existing rows): `VisualizationCreated = 12`,
`VisualizationUpdated = 13`, `VisualizationDeleted = 14`.

### 2.6 Permissions summary

| Action | Owner | Editor share | Viewer share | No share |
|---|---|---|---|---|
| list / render | ✔ | ✔ | ✔ | 403 |
| create / edit / delete | ✔ | ✔ | 403 | 403 |

Rendering always applies the **renderer's** row-level scope, never the owner's. This mirrors how
shared queries already run with the runner's scope.

### 2.7 Errors, empty and null cases

| Case | Behavior |
|---|---|
| 0 rows | "No data to chart for these filters." Counter shows "—" |
| null X/category | label "(blank)" |
| null measure | bar omitted; line gap (no zero substitution). Intentional difference: Redash defaults missing values to zero (E-R15), which can mislead |
| non-numeric measure at save | 400 "‘X’ is not a number column" |
| text value in a numeric column (should not happen) | value skipped, counted in a footnote "n values could not be plotted" |
| query disabled | 409. Tab shows "This query is disabled" with no data |
| share revoked while the page is open | the next render returns 403. Tab shows "You no longer have access" |
| resolver denies scope / object hidden | 403 from the guard. Shown as no access, with no data |
| invalid parameter value | existing 400 message |
| SQL timeout / failure | existing "Query failed" 400 |
| options JSON with unknown `version` | 400 on save. On render, treated as stale (W5) |

### 2.8 Limits

`ChartMaxRows` = 1,000 (constant; later configurable if needed); pie ≤ 12 slices;
≤ 5 measures; ≤ 20 visualizations per query; name ≤ 200 characters. The record limit (data source
→ host default) still applies and takes precedence when it is lower.

### 2.9 Accessibility

- Each chart container has `role="img"` and a generated `aria-label`, e.g. "Bar chart: Count of
  enrolments by Program, 5 categories".
- **Show data** toggle under every chart renders exactly the plotted rows as a semantic `<table>`.
- Series are distinguished by legend text and tooltips, not color alone. Palette tokens meet
  3:1 contrast against both light and dark backgrounds.
- Keyboard: chart tabs and controls are reachable, and data points can be focused when the library
  supports it (Recharts `accessibilityLayer`).
- `prefers-reduced-motion` turns animations off.

### 2.10 Compatibility

- The migration only adds a table; existing queries have zero visualizations and behave exactly as
  before. The API is additive, and run/export/save are unchanged.
- Works on the auto-migration and DBA-script paths (G-DB).
- Deleting a saved query cascades to its visualizations. Editing a query never deletes
  visualizations; stale ones surface as W5.

### 2.11 Chart rendering dependency ⚑ (U4)

Recommended: **Recharts 3 (MIT)** composed through shadcn/ui's Chart component pattern, which
matches the existing shadcn/Tailwind conventions and theming. Alternatives: hand-rolled SVG (no
dependency, more code and accessibility work) or Plotly basic dist (MIT, ~1 MB+). Whichever is
chosen, its full transitive tree goes through the G-LIC audit after install.

### 2.12 Test fixtures (synthetic only)

`Sample-DataScope.sql` (already applied to `QueryBuilderDemoSource`): `sales.vw_ProgramEnrollments`,
100 rows, exactly 20 per ProgramId 10/20/30/40/50, plus the `DevPersonas` pattern from
`CLAUDE.md` (temporary, reverted before packaging):
- **F1** saved query "Enrolments by program" = `vw_ProgramEnrollments`, columns ProgramId +
  Count(*) → Bar.
- **F2** Counter over Count(*).
- **F3** Line over a date column of the same view (or ProgramId as X if no date column).
- **F4** Pie of F1.
- **F5** empty: F1 + filter ProgramId = 999.
- **F6** nulls: xUnit with an in-memory result containing null categories and null measures.

---

## 3. Acceptance criteria

Derived from this spec. Each one has a test or a recorded runtime check.

**Workflow**
- AC-W1: Owner creates a Bar chart on F1, reloads the page, and the chart tab is there with the
  same options.
- AC-W2: Editor share holder can create, edit and delete charts. A Viewer share holder can render
  but gets 403 on POST/PUT/DELETE (**negative API test**).
- AC-W3: Deleting the saved query deletes its visualizations (DB check).

**Isolation (two users minimum)**
- AC-S1: F1 rendered as Super admin → 5 bars (20 each). The same visualization rendered by a
  share holder whose scope is ProgramId 10 → exactly 1 bar (20). Rendered by "No scope" → 403 or
  no data, as for the run path. This is a **negative test**: the scoped user must never receive
  ProgramId 20–50 rows.
- AC-S2: A user with no share gets 403 on list and render (**negative**).
- AC-S3: Revoking a share makes the next render return 403 (**negative**).
- AC-S4: Disabling the query makes render return 409. Re-enabling restores it (**negative**).
- AC-S5: The render response contains only the referenced columns.
- AC-S6: An xUnit test proves the render handler passes the scope predicates from
  `IDataScopeGuard` into `Build`. A fake guard returning a predicate yields SQL containing
  `@__scope0`. A test that drops the predicate must fail.

**Validation / limits**
- AC-V1: Saving with a non-existent or non-numeric measure → 400.
- AC-V2: Stale chart after removing its column from the query → render 400 naming the column; UI
  shows the "needs updating" state.
- AC-L1: Data source `MaxRecords = 3` with F1 (5 groups) → render refused (409) with the record-limit
  message; no partial chart.
- AC-L2: A query with > 1,000 result rows → "too many points" refusal.
- AC-L3: Pie with 13+ categories → 400. Pie with a negative value → 400.
- AC-E1: F5 → empty state. F6 → "(blank)" label, line gap, and no zero substitution.
- AC-P1: Parameterized F1 (ProgramId = `@pid`): a malicious value such as `1; DROP TABLE x` is
  rejected as an invalid number or bound as a parameter. It never appears in the SQL text
  (SQL-preview/xUnit check).

**Compatibility / packaging**
- AC-C1: Existing 1.0.13 database auto-migrates. Saved queries, runs and exports work unchanged.
- AC-C2: Regenerated DBA idempotent script creates the table when run twice (second run is a
  no-op).
- AC-C3: `dotnet test` green. `npm run build`, wwwroot copy, `dotnet clean && dotnet pack`
  succeed, and the packed SPA shows chart tabs.
- AC-A1: Every chart has an aria-label and a working "Show data" table. Controls are reachable by
  keyboard.

**Redash comparison:** runtime comparison with the same fixture shape is possible only after U1
is approved (Redash needs its own synthetic data source). Until then, comparison is limited to
source-observed behavior, and **no parity claim** is made.
