# SPEC — Dashboards (Phase 2)

Status: **APPROVED for implementation 2026-11-02** (user: "come up with a best idea … let's
implement it"). Builds on Phase 1 saved visualizations (`../saved-visualizations/SPEC.md`).

Goal: a business user puts saved charts from one or more queries onto one page, arranges them,
adds short text notes, and shares the page with named colleagues. **Every viewer sees each chart
with their own access and row scope.** Sharing a dashboard never grants access to data.

---

## 1. Observed Redash behavior (reference only, no code reused)

Source: Redash **v26.9.0** (`R26:`, commit `42fdf9d4`) unless noted. Source-observed and
documented only; nothing was observed at runtime (the local 8.0.0 instance was not driven).

| # | Observation | Evidence |
|---|---|---|
| R1 | A dashboard has a name, slug, owner, `version`, `is_draft`, `is_archived`, `tags`, `options` and `dashboard_filters_enabled`. The legacy `layout` column is kept only to render old dashboards | `redash/models/__init__.py:1116-1135` |
| R2 | The dashboard list shows a dashboard if **any** of its widgets' queries uses a data source in one of the user's groups, or the user owns it. Drafts are visible only to their owner | `models/__init__.py:~1145-1160` (`Dashboard.all`) |
| R3 | A widget is either a visualization or text, with a `width` and `options` (grid position in `options.position`) | 8.0.0 `models/__init__.py:986-1001`; R26 `components/dashboards/DashboardGrid.jsx:6,73` |
| R4 | **Restricted widgets:** when serializing a dashboard for a user, a widget whose query the user cannot view returns only id, width, options and timestamps, flagged `restricted: true`. Its visualization and query are omitted | `redash/serializers/__init__.py:209-232` |
| R5 | Adding a widget needs modify rights on the dashboard **and** view access to the visualization's query. Updating or deleting a widget needs modify rights on the dashboard | `redash/handlers/widgets.py:14-77` |
| R6 | Dashboard updates carry `version`; a stale version (or a `StaleDataError`) returns 409 | `redash/handlers/dashboards.py:236-249` |
| R7 | Public sharing creates an `ApiKey`; anyone with the token gets an anonymous public view. Admin or owner only | `handlers/dashboards.py:278-300`, `:300+` |
| R8 | Grid: 12 columns, row height 50, margins 15; default widget 6×3; min width 2, max 12; min height 2 | `client/app/config/dashboard-grid-options.js:1-13` |
| R9 | Layout uses `react-grid-layout` ^0.18.2 | `package.json` |
| R10 | Auto-refresh comes from the `?refresh=` URL value, clamped to ≥ 30 s and the policy minimum, and runs on a `setInterval` that reloads the widgets | `pages/dashboards/hooks/useRefreshRateHandler.js:7-33` |
| R11 | Dashboard-level parameters are collected from widgets and mapped as dashboard-level, widget-level or static | `pages/dashboards/hooks/useDashboard.js:43,160-163`; `components/ParameterMappingInput.jsx:28-46` |
| R12 | Draft/publish toggle; fork (duplicate) | `useDashboard.js:99-100`; `models/__init__.py` `Dashboard.fork` |
| R13 | Each widget loads its own query result; "refresh dashboard" reloads every affected widget | `useDashboard.js:103-147` |

REA: `analyze_javascript_application` over 12 dashboard client files
(`ev_14e315dcd00d30c1d8c9da9b7d975a8da83a98766a2b873e23b7689340f7eadc`, summary
`ev_9ac01474…`). It parsed all 12 with 0 failures. REA found **0 request relations**, because
the client calls the API through an axios wrapper that REA's extractor does not cover. That is
a tool limit and does not mean there are no calls; the `services/dashboard.js:160-178` and
`services/widget.js:192-208` citations above cover the API surface instead. Its timer family
recorded 3 relations, consistent with R10.

## 2. Desired QueryBuilderEditor behavior (and intentional differences)

### 2.1 Concepts
- **Dashboard:** name, optional description, owner, `Version` (optimistic concurrency), and
  ordered **tiles**.
- **Tile:** either a **chart tile** (references one Phase 1 visualization) or a **text tile**
  (plain text, ≤ 2,000 characters, no HTML or markdown, so no XSS surface). Each tile has a width
  (3, 4, 6, 8 or 12 of 12 columns), a height (S/M/L = 1/2/3 units of 160 px), and an order. An
  optional title overrides the chart name.
- **Sharing:** named users as Viewer or Editor, chosen with the same `AppUsers` picker as query
  sharing. The owner can delete the dashboard and manage sharing.

### 2.2 Intentional differences from Redash
| Redash | QBE | Why |
|---|---|---|
| The list is driven by data-source groups (R2) | The list shows dashboards **I own or that are shared with me** | QBE's sharing model is per object |
| A restricted widget still exposes its id and options (R4) | A restricted tile exposes **only its position, size and the fact that it is restricted**: no chart name, query name, ids or title override | Avoid leaking names of queries the viewer can't see |
| Public anonymous links (R7) | **Not offered** | Security constraint (no anonymous links by default) |
| Free-form drag/resize grid via react-grid-layout (R8, R9) | Ordered flow grid with width/height presets; drag to reorder via the existing `@dnd-kit`; keyboard reordering | No new dependency; accessible; simpler for business users |
| Auto-refresh ≥ 30 s via URL (R10) | Manual **Refresh all**, plus optional auto-refresh of Off / 1 / 5 / 15 / 30 min (minimum 60 s), paused while the browser tab is hidden | Protects the business database; every refresh is a live, scoped run |
| Dashboard-level parameters (R11) | **Not in this phase.** A tile whose query needs a value without a default shows **Set values** (the existing prompt, per tile) | Phase 3 of the roadmap |
| Draft/publish, fork, tags, archive (R12) | Not in this phase. A dashboard is private until shared | Smaller slice |
| Shared result cache | None. Each tile render is a live run with the viewer's scope | Same as Phase 1 |

**Proposed improvement (no Redash equivalent):** when the owner shares a dashboard with someone,
the share dialog **lists the charts that person cannot see** (their queries aren't shared with
them) and offers a checkbox: **"Also share these N queries as Viewer."** It is unchecked by
default, and it shows only queries the current user owns, since only owners can share queries.
Data access is never granted implicitly; the owner chooses it explicitly.

### 2.3 Workflows
- **D1 Create:** Dashboards nav → **New dashboard** (name) → opens empty in edit mode.
- **D2 Add chart tile:** edit mode → **Add chart** → a picker lists every visualization on
  queries I can view (owner or shared), grouped by query name → choose one → tile appended with
  width 6, height M.
- **D3 Add text tile:** edit mode → **Add text** → plain-text editor.
- **D4 Arrange:** edit mode → drag to reorder (mouse or keyboard via dnd-kit sensors), width and
  height from a per-tile menu, remove a tile. **Save** sends the whole tile list with `version`.
  A stale version gives 409 "This dashboard was changed by someone else — reload". **Cancel**
  discards the changes.
- **D5 View:** each chart tile calls the **existing Phase 1 render endpoint**
  `POST /api/queries/{qid}/visualizations/{vid}/data` and draws with `ChartRenderer`. Tiles load
  independently, at most 4 renders in flight per dashboard. Each tile handles its own states:
  loading, empty, needs-updating, disabled, no access, too many rows, needs values, error.
- **D6 Share:** owner only; Viewer or Editor; with the explicit "also share queries" option
  from §2.2.
- **D7 Refresh:** **Refresh all** button, plus the auto-refresh selector.

### 2.4 Permissions
| Action | Owner | Editor share | Viewer share | None |
|---|---|---|---|---|
| list / open dashboard | ✔ | ✔ | ✔ | 403 (404 is acceptable for list absence) |
| rename / edit tiles / reorder | ✔ | ✔ | 403 | 403 |
| add a chart tile | ✔ only if the actor can view that chart's query | same | 403 | 403 |
| delete dashboard, manage shares | ✔ | 403 | 403 | 403 |
| see a chart tile's content | only if the viewer can view the tile's query (owner/share); otherwise **restricted** | same | same | — |

Tile data is **never** returned by dashboard endpoints. Data only flows through the Phase 1
render endpoint, which re-checks query access, disabled state, catalog policy, the viewer's row
scope and record limits on every call. So sharing a dashboard cannot widen data access.
(Redash R5 needs view access to add a widget; QBE keeps that rule, R5-equivalent.)

### 2.5 Data contracts (camelCase via `QueryBuilderJson.Options`)
- `DashboardSummaryDto { id, name, description?, ownerId, ownerName, isOwner, accessLevel,
  tileCount, updatedAtUtc? , createdAtUtc }`
- `DashboardDetailDto { id, name, description?, ownerId, ownerName, isOwner, accessLevel,
  version, tiles: DashboardTileDto[] }`
- `DashboardTileDto { id, kind: "chart"|"text", order, width: 3|4|6|8|12, height: 1|2|3,
  restricted: bool, title?: string, text?: string, chart?: { savedQueryId, savedQueryName,
  visualizationId, visualizationName, type } }`. For a restricted tile only `id, kind, order,
  width, height, restricted=true` are set. A chart tile whose visualization was deleted returns
  `chart: null, restricted: false` plus `missing: true`.
- `SaveDashboardRequest { name, description? }` (create / rename)
- `SaveDashboardTilesRequest { version, tiles: [{ id?, kind, order, width, height, title?, text?,
  visualizationId? }] }` (replaces the full tile set)
- Share endpoints mirror query sharing: `GET/POST /dashboards/{id}/shares`,
  `DELETE /dashboards/{id}/shares/{shareId}`. The POST body
  `{ sharedWithUserId, sharedWithUserEmail, accessLevel, alsoShareQueryIds?: Guid[] }`.
  `alsoShareQueryIds` is validated: each must be a query the caller owns **and** that is used by
  a tile on this dashboard. Each one is shared as **Viewer** using the existing
  `ShareQueryCommand` logic. If a query is already shared at a higher level it is not downgraded.
- `GET /dashboards/{id}/share-preview?userId=…` (owner) returns
  `{ inaccessibleQueries: [{ id, name, canShare }] }` for the dialog.

### 2.6 Persistence
- `Dashboards`: `Id`, `Name` nvarchar(200), `Description` nvarchar(1000) null, `OwnerId`
  nvarchar(450), `OwnerName` nvarchar(256), `Version` int, audit columns.
- `DashboardTiles`: `Id`, `DashboardId` (FK cascade), `Kind` nvarchar(10), `VisualizationId`
  null (FK → `QueryVisualizations`, **ON DELETE SET NULL**; if SQL Server rejects that because
  of cascade paths, use NO ACTION and null the column in the visualization/query delete
  handlers), `Title` nvarchar(200) null, `Text` nvarchar(2000) null, `Order` int, `Width` int,
  `Height` int. Index on `DashboardId`.
- `DashboardShares`: like `QueryShares` (unique `DashboardId` + `SharedWithUserId`).
- `Version` increments on every tile save and rename. A mismatch gives 409.
- Child rows are added via the DbSet (EF sharp edge in `CLAUDE.md`). Tile save replaces the
  set: delete removed tiles, update existing ones, add new ones.
- Limits: 30 tiles per dashboard; text ≤ 2,000; name ≤ 200.
- Audit actions (appended): `DashboardCreated=15, DashboardUpdated=16, DashboardDeleted=17,
  DashboardShared=18, DashboardUnshared=19`. Entity type `Dashboard`. Chart renders keep logging
  `QueryRun` (Phase 1).

### 2.7 Errors and edge cases
| Case | Behavior |
|---|---|
| Version conflict on save | 409 "This dashboard was changed by someone else — reload to see the latest." |
| Adding a chart whose query the actor can't view | 403 |
| A tile's visualization deleted | Tile shows "This chart was removed" (editors can remove the tile) |
| The viewer loses access to a tile's query later | Tile becomes restricted on the next load; a render already in flight gets 403 → "You don't have access to this chart" |
| Query disabled | 409 from render → "This chart's query is disabled" |
| Too many rows / record limit | 409 → message from the server |
| Required parameter without default | "Needs values" + **Set values** → existing prompt; values kept in memory for the tile only |
| Empty dashboard | Empty state with **Add chart** / **Add text** (editors) |
| Text tile | Rendered as plain text (`white-space: pre-wrap`); never `dangerouslySetInnerHTML` |
| > 30 tiles | 400 |

### 2.8 Accessibility
- Tiles are `<section aria-labelledby>` with a heading. Chart accessibility comes from Phase 1.
- Reordering works by keyboard (dnd-kit `KeyboardSensor` + `sortableKeyboardCoordinates`), with
  live announcements.
- Width and height controls are labelled menu items. Restricted tiles have readable text.
- Auto-refresh does not move focus. Updates are announced politely (`aria-live="polite"` on the
  tile status).

### 2.9 Compatibility
Additive migration (3 tables); no change to existing tables except new FKs that point at
`QueryVisualizations`. Existing queries and charts are unaffected. Deleting a saved query
cascades its visualizations, which empties the affected tiles (they show "removed"). The DBA
idempotent script is regenerated at release time.

## 3. Acceptance criteria
**Workflow**
- AC-D1: The owner creates a dashboard, adds 2 chart tiles from 2 different queries and 1 text
  tile, reorders them, changes widths, and saves. After a reload the order, sizes and text are
  identical.
- AC-D2: An Editor share can edit tiles; a Viewer share gets 403 on tile save and rename
  (**negative**); non-owners get 403 on delete and on share management (**negative**).
- AC-D3: Two concurrent edits: the second save with the old `version` → 409 (**negative**).

**Isolation (≥ 2 users)**
- AC-S1: The dashboard holds the F1 chart (ProgramId). Super admin sees 5 bars; a Program-admin
  10 viewer who has the dashboard and query shares sees exactly 1 bar (**negative:** no other
  ProgramIds in any response).
- AC-S2: The dashboard is shared with a user who does **not** have the query share. That tile is
  `restricted: true` in the detail response, with **no** query or visualization name or id, and
  the render endpoint returns 403 for that user (**negative**).
- AC-S3: The "also share queries" option shares exactly the listed queries as Viewer. It refuses
  (400 or 403) a query the caller doesn't own or that isn't on the dashboard (**negative**).
- AC-S4: An unshared user gets 403 on the dashboard and does not see it in their list
  (**negative**).
- AC-S5: Adding a tile for a visualization whose query the actor can't view → 403
  (**negative**).
- AC-S6: Disabling a tile's query → that tile shows disabled and the other tiles still render.

**Edge**
- AC-E1: Deleting a visualization makes its tile show "removed". Deleting the dashboard
  cascades its tiles and shares.
- AC-E2: A text tile with `<script>alert(1)</script>` renders as literal text (no execution).
- AC-E3: The 31st tile → 400.
- AC-E4: Auto-refresh at 1 min re-renders the tiles (network shows repeated render calls). It
  pauses when the tab is hidden (best effort).

**Compatibility / packaging**
- AC-C1: The migration auto-applies on the existing database, and Phase 1 charts and existing
  queries still work.
- AC-C2: The idempotent script for the new migration applies to a fresh DB. (The full script's
  re-run problem from the 1.0.13 `AddCatalogScopeAndAllowedObjects` migration is pre-existing
  and tracked separately.)
- AC-C3: `dotnet test` green; `npm run build`, wwwroot refresh and `dotnet pack` succeed; the
  embedded bundle serves the Dashboards page.
- AC-A1: Keyboard reordering works; tiles have headings; the restricted tile text is readable.

No runtime comparison with Redash (local instance not driven) — **no parity claim**.
