# Chart and results drill-down — design

Date: 2026-11-17 · Status: draft for approval · Target release: QueryBuilder.Editor 2.2.0 (Launch unchanged)

## 1. Why

A client saw the full product in a demo on 2026-11-17 and was impressed. Their one piece of feedback: they want to
click into a chart and see what is behind it. Their example was Monday.com: click a pie slice, land on a screen with
the items behind that slice. The user wants a version that goes beyond what the client saw, without ever weakening
row-level data scoping, which the user calls the product's best feature, to be protected "at any cost".

What others do (researched 2026-11-17): Monday.com drills only on charts grouped by Status or Person columns, not on its
Data Over Time widget; Metabase offers a click menu (see these records / break out by / zoom in / filter) shown only to
people who may build queries, with author-defined destinations as an alternative; Power BI separates drill down (same
visual, next level), drill through (another page, filtered, with Back) and cross-filter (the default); Looker lets
modellers list drill fields per column and offers dashboards that filter on the clicked field.

## 2. Goals, non-goals, anti-goals

**Goals (this release)**
1. **See these rows**: click a mark (pie slice, bar, point, pivot cell, counter, an aggregated result row) and open a
   page listing the underlying rows, with the click trail, the caller's zone chip, row count, export and a shareable link.
2. **Break down by**: re-draw the same chart, in place, for the clicked value, grouped by another column; a trail lets
   the user step back up to any level.
3. **Filter this dashboard** (cross-filter): clicking a value on a dashboard tile filters every tile that can take that
   filter; tiles that can't are marked "not filtered". Authors can turn this off per dashboard.
4. Works for every person who can see the chart today, including hub Viewers on dashboards they can see.

**Non-goals (later, already agreed as the second release)**
- Zooming into dates (year → quarter → month). Needs a new "group by period" feature first.
- Author-linked destinations ("open the Program detail dashboard with this program").

**Also out of scope**: drilling on unsaved queries; cross-filtering across data sources; an admin list of hidden columns
(option 3 in the conversation; may come later); editing data; joins.

**Anti-goals (failure even if it "works")**
- Any drill path, at any depth, returning a row outside the caller's zone. A user zoned to `ProgramId = 10` sees only
  ProgramId 10 rows through every action, link, export and tile. Non-negotiable.
- A drill showing columns nobody chose to show (the view's other columns).
- A tile that looks filtered but isn't, or a breakdown chart drawn from truncated data (wrong totals).
- The browser being able to shape the SQL beyond "what was clicked".

## 3. Decisions made with the user

| Decision | Choice |
|---|---|
| Who may drill | Anyone who can see the chart or dashboard today. Row-level scope still applies to every drill. |
| Which columns "See these rows" shows | The query's own columns (aggregates replaced by their base column), plus a per-chart author list "Columns to show when drilling". Never the whole view by default. |
| Row-level scope | Absolute at every level and through every path (see §5). |
| Release split | 1st: See these rows, Break down by, Filter this dashboard. 2nd: date zoom, linked destinations. |
| Server approach | A new drill route on the existing chart engine (`VisualizationDataLoader`); the browser sends only what was clicked. |
| Where "See these rows" opens | Its own page whose address holds the trail (shareable, Back works). |
| Cross-filter | Automatic by column name within the data source, plus an author switch per dashboard to turn it off. |

## 4. Approaches considered

1. **Server route on the chart engine (chosen).** `VisualizationDataLoader` already loads the saved definition from the
   database, checks shares or the dashboard grant, binds parameters, validates against the catalog, applies the
   caller's scope and record limits. Drill adds steps to that path, so it inherits every check.
2. **Browser rewrites the definition and calls run (rejected).** Viewers can't run queries, and it would let the
   browser shape SQL; it breaks "the browser only says what was clicked".
3. Detail screen as **a side panel** (rejected for now): cramped for wide rows and an awkward shareable link.
4. Cross-filter **through dashboard parameters only** (rejected as the default): does nothing until an author sets
   parameters up, so most existing dashboards would not respond.

## 5. The scope guarantee (how "at any cost" is met)

1. **The browser sends only the click trail**: `steps = [{ column, value | null }]` naming *output columns of the saved
   query*, plus the action, parameter values and the dashboard/tile context. Never a definition, never SQL.
2. **The server derives the drill definition** from the saved definition. Each step's column must map to a
   non-aggregated column of the query (or, for "See these rows"/"Break down by", one in the chart's drill-column list).
   Unknown or aggregated columns → 400. Values are bound SQL parameters through the existing filter path; `null` →
   `IS NULL`.
3. **Steps are ANDed inside the user-filter group**, and the generated SQL stays `(scope) AND (filters AND steps)`.
   A step can only narrow.
4. **Same gate as every query**: `ValidateAsync` → `IDataScopeGuard.AuthorizeAsync` → `Build(definition, scope)`. Any
   drill-column is validated against the catalog and is subject to the same undecided/stale-rule refusals.
5. **Tampering yields nothing**: a ProgramId 10 user sending `Program = 20` gets an ordinary empty result, worded the
   same as any empty result.
6. **Totals are computed inside the zone**: "Break down by" aggregates only scoped rows, so it can't reveal counts of
   other zones.
7. **Links and exports**: a shared drill link carries only the trail; the opener's own access and zone apply. Drill
   exports apply scope and record limits exactly like the existing results export (refused, never truncated).
8. **Cross-filter values** are drill steps applied per tile on the server, under each tile's own scope.
9. **Proved by tests**: a matrix over every action (rows, breakdown, cross-filter, export, link) × personas (Super admin
   = Unrestricted, ProgramId 10, 10+30, no scope → refused) asserting every returned row's scope column is in the
   persona's zone; including tampered steps, `null` steps, breakdown by the scope column itself, a drill column that is
   the scope column, Viewers via a dashboard grant, and a stale scope rule (refused).

## 6. Behaviour

### 6.1 What a click means, per chart type

| Type | Clickable mark | What the user sees it as (steps: see the rule below the table) |
|---|---|---|
| Bar, Horizontal bar, Line, Area, Combo, Trend | a bar / point | X value (+ Series value when the chart has a series) |
| Pie, Funnel | a slice / row | X (category) value |
| Waterfall | a step bar (not the total bar) | X value |
| Scatter / bubble | a point | Label value, plus Series if set; a point with neither is not clickable |
| Pivot | a cell / a row total | row values (+ column value for a cell); grand total = no steps |
| Counter | the number | no steps ("See these rows" only) |
| Table (chart) on an aggregated query | a row | every non-aggregated column of that row |
| Table on a non-aggregated query | — | not drillable (the rows already are the detail) |
| Query page results grid (saved, aggregated query) | a row | every non-aggregated column of that row |

Note: pie charts are refused above 12 slices today, so there is no "Other" slice to handle.

**Steps = the clicked result row, not just the visible dimensions (decision, author of this doc).** Every chart mark
except pivot cells/totals is drawn from exactly one result row (charts draw rows that share an X as separate rows and
warn via `duplicateXCount`). The steps are therefore every non-aggregated column of that row, so "Program 20" on a
query grouped by Program *and* Status opens only the rows of the bar that was clicked, and a `Count` matches. Pivot
cells aggregate several rows client-side, so their steps are the shown row values + column value.

**Non-aggregated queries (decision):** "See these rows" still works (the steps filter the raw rows to the clicked one(s));
"Break down by" is not offered, because there is no aggregate to regroup with.

A click opens a small menu at the mark. On the query page: **See these rows**, **Break down by ▸**. On a dashboard:
those two plus **Filter this dashboard** (when the switch is on). Charts must also be drillable from the keyboard (a
reachable list of the chart's values offering the same menu); the exact control is the builder's call.

```
 Click "Program 20 · 140 enrolments"
 ┌──────────────────────────────┐
 │ See the 140 rows             │
 │ Break down by ▸ Status, Site │
 │ Filter this dashboard        │
 └──────────────────────────────┘
```

### 6.2 See these rows (detail page)

Route: `/queries/:queryId/charts/:vizId/drill?…` (results-grid drill: `/queries/:queryId/drill?…`), with the dashboard
and tile ids when opened from a dashboard, the steps, and the `p_<name>` parameter values. The page shows: a trail
(`Enrolments by program › Program = 20 › Status = Active`, each crumb clickable), the zone chip, the parameter values
in effect (read-only), the row count, a table with paging, export (CSV/XLSX), Copy link, and Back.

Derived definition: same source and filters (with bound parameters) + steps; `GroupBy` and `Having` removed; columns =
the query's non-aggregated columns, each aggregated column replaced by its base column (deduplicated; `COUNT(*)`-style
columns with no base column dropped), then the chart's drill columns; sorts kept when they name a remaining column.
Rows: the grid shows the first N (effective record limit, else the legacy 1,000) with the existing truncation banner.
When the clicked measure was a `Count`, the page shows the row count next to the clicked number; a mismatch is
expected for distinct counts, so no claim of equality is made for other aggregates.

```
 ← Back   Enrolments by program › Program = 20            ZONE · ProgramId 10, 20
 140 rows · Term = 2026                                   [Export ▾] [Copy link]
 ┌──────────┬──────────┬────────┬─────────────┐
 │ Program  │ Status   │ Site   │ Enrolled on │
 ...
```

### 6.3 Break down by

The tile/chart re-draws in place: same chart type and measures, filtered by the steps, grouped by the chosen column
(which replaces the chart's X; the series is dropped if it equals the new column). Choices: the query's non-aggregated
columns plus the chart's drill columns, minus columns already in the trail. A trail above the chart steps back up. The
same chart rules apply (pie ≤ 12 slices, no negative slices, refusal of truncated data with the existing message). The
breakdown is a view state, never saved; on a dashboard it is per tile and cleared on refresh-to-default.

Offered for (decision): Bar, Horizontal bar, Line, Area, Combo, Waterfall, Pie, Funnel — the new column replaces X.
Not offered for Trend (a time axis), Pivot, Scatter, Counter or Table. The server rewrites the options and re-runs the
existing options validation (`ReferencedColumns`), and the client's `optionsProblem` must agree with it.

HAVING (decision): removed for "See these rows" and "Break down by" (an author's threshold on the parent groups would
silently drop small sub-groups and make the breakdown's totals look wrong); kept for cross-filtered tiles, which are
the same chart narrowed.

### 6.4 Filter this dashboard (cross-filter)

Clicking **Filter this dashboard** adds `column = value` to a dashboard filter bar (`Program = 20 ✕`); several filters
AND together. For each tile the server applies the filter when the tile's query reads the same data source and its
source view has a column of that name (matched with the provider's identifier comparer); otherwise the tile renders
unfiltered with a visible "not filtered by Program" mark. Filters combine with dashboard parameters. The filter state
lives in the page address so it can be shared.

Which columns a cross-filter may name (decision, consistent with the user's column rule): each filter carries the
**source tile** it was clicked on, and the server accepts it only if the column is a non-aggregated output column or a
drill column of that tile's chart, and the tile belongs to this dashboard and is visible to the caller. Otherwise 400.
A hand-typed address can therefore never filter on a column no author put in front of people. The author switch **Allow click-to-filter** (default on) lives in the
dashboard settings; when off the menu item is hidden and the server refuses cross-filter values for that dashboard.

### 6.5 Who sees what

Drill is allowed wherever the chart can be seen: owner/shared users, Admins/super admins in hub mode, and hub Viewers
through the dashboard grant (Viewer parameter values constrained exactly as for drawing). The new drill routes are
added to the Viewer allowlist (`.AllowViewers()`), and the Viewer/endpoint matrix tests are updated.

### 6.6 Audit

New `AuditAction.QueryDrilled` (next free number) with query, visualization, dashboard/tile, action, steps, row count,
scope. Drill exports reuse `QueryExported` with the steps added. Values follow the same rules as existing run audits.

## 7. Server shape (decided parts)

- `POST /api/queries/{q}/visualizations/{v}/drill` `{ action: "rows" | "breakdown", steps, breakdownColumn?,
  crossFilters?, parameterValues, dashboardId?, tileId? }` and its export twin; a results-grid route for saved queries
  without a chart. All read bodies with `QueryBuilderJson.Options`.
- `VisualizationDataLoader` gains a `Drill` purpose (or a sibling that shares its access/validation/scope steps); the
  definition transform is a pure, unit-tested function.
- Dashboard tile rendering accepts cross-filters (the current tile-chart route is a GET; the builder picks the shape).
- New `Dashboards.ClickToFilterEnabled` (bit, default 1) → **an EF migration**, so the DBA script is regenerated
  (`QueryBuilder.schema.2.2.0.sql`) and the version is a minor bump (2.2.0).
- Chart options: additive JSON `drill: { columns: string[] }` in `VisualizationOptions` (catalog column names of the
  query's source; max 20). No migration needed for this part.

## 8. Constraints

- Every endpoint uses `QueryBuilderJson.Options`; no MediatR; embedded mode unchanged apart from the new feature.
- Works on SQL Server and PostgreSQL; identifier matching via `IdentifierMatching`; PostgreSQL text-mode reads stay.
- New UI stays in the Zoning Map design world (DESIGN.md); no insecure-context APIs (`crypto.randomUUID` etc.).
- No new paid or copyleft packages; Recharts click events are enough.
- Both READMEs document the feature; release steps per CLAUDE.md (tests on net8.0 and net10.0 with both DB variables).

## 9. Left to the builder

- Exact encoding of steps and filters in the URL (must round-trip types, including `null` and dates; cap length).
- Limits: maximum trail depth, value length, number of cross-filters.
- Equality on floating-point columns and exact date-time round-tripping (test both; refuse float steps if they can't
  be made exact).
- The keyboard-accessible drill control and the menu component.
- How the drill column list is edited in the chart editor.
- Client caching keys (must include steps, filters and parameters).
