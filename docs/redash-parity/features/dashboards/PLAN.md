# PLAN — Dashboards (Phase 2)

Branch `feature/rea1` (Phase 1 is uncommitted on it too). No commits or pushes unless asked.
TDD for the backend handlers. **No new dependencies:** reuse `@dnd-kit` (already in
`client/package.json`, used by the builder's column panel), Recharts and `ChartRenderer` from
Phase 1.

## Backend
1. **Domain:** add `Entities/Dashboard.cs`, `DashboardTile.cs` and `DashboardShare.cs`. In
   `Enums.cs`, add `DashboardTileKind { Chart, Text }` and append AuditAction values 15–19.
2. **Application** (`Dashboards/`):
   - `DashboardAccess` (CanView = owner or share; CanEdit = owner or Editor; IsOwner);
   - `GetDashboardsQuery` (mine + shared), `GetDashboardQuery` (detail; computes `restricted`
     per tile via `SavedQueryAccess.CanView` on the tile's query; strips names and ids from
     restricted tiles);
   - `CreateDashboardCommand`, `RenameDashboardCommand` (version bump),
     `SaveDashboardTilesCommand` (version check → 409 via a new `ConcurrencyConflictException`;
     validates kinds, sizes and limits; a new chart tile needs `CanView` on its query),
     `DeleteDashboardCommand`;
   - `ShareDashboardCommand` (+ `alsoShareQueryIds`, validated and reusing
     `ShareQueryCommand`'s logic), `RemoveDashboardShareCommand`, `GetDashboardSharesQuery`,
     `GetDashboardSharePreviewQuery`;
   - a visualization picker query: all visualizations on queries the user can view (id, name,
     type, query id/name).
3. **Exceptions:** `ConcurrencyConflictException` → 409 in `GlobalExceptionHandler`.
4. **Infrastructure:** EF configurations for the three tables (cascade from Dashboards; tile →
   visualization ON DELETE SET NULL, falling back to NO ACTION plus handler cleanup if SQL
   Server reports multiple cascade paths); `DashboardRepository`; DI registration. Migration
   `AddDashboards` via the pinned dotnet-ef command (see `CLAUDE.md`).
5. **Editor:** `Endpoints/DashboardEndpoints.cs` under the main `/querybuilder` group
   (`/api/dashboards…`, plus `/api/visualizations/pickable`); `ReadFromJsonAsync` /
   `Results.Json` with `QueryBuilderJson.Options`.

## Frontend
6. Routes `/dashboards` and `/dashboards/:id` in `App.tsx`; a "Dashboards" nav item in
   `AppShell`.
7. `hooks/useDashboards.ts` (list, detail, create, rename, save tiles, delete, shares,
   share-preview, pickable visualizations). Invalidate list and detail keys on mutations.
   Never let `queryFn` return undefined; never return new arrays from Zustand selectors.
8. `pages/DashboardListPage.tsx`, `pages/DashboardPage.tsx`. In
   `features/dashboards/`: `DashboardGrid.tsx` (CSS grid with 12 columns and dnd-kit sortable in
   edit mode), `ChartTile.tsx` (calls the Phase 1 render endpoint, reuses `ChartRenderer`, and
   handles every state in SPEC §2.7), `TextTile.tsx` (plain text), `AddChartDialog.tsx`,
   `ShareDashboardDialog.tsx` (reuses the AppUsers picker pattern from `ShareDialog.tsx` +
   share-preview + "also share" checkbox), `RefreshControl.tsx`, and a render-concurrency limiter
   (max 4).
9. Accessibility per SPEC §2.8.

## Tests
- Handler tests with fakes:
  - restricted tile stripping (no names or ids);
  - Viewer 403 on save and rename;
  - non-owner 403 on delete and share;
  - version conflict 409;
  - add-tile 403 without query access;
  - 31-tile limit;
  - `alsoShareQueryIds` refusal for a query the caller doesn't own or that isn't on the
    dashboard;
  - text length limit.
- Negative proof: temporarily remove the restricted-tile stripping, show the test fails, then
  restore it.

## Validation commands
`dotnet test tests/QueryBuilder.Tests`; `cd client && npm run build`; wwwroot refresh (the
`CLAUDE.md` step 2); `dotnet clean && dotnet pack src/QueryBuilder.Editor -c Release -o
<scratchpad>`. Runtime acceptance (SPEC §3) is a separate step using the temporary DevPersonas,
reverted afterwards.

## Docs
Both READMEs: a feature bullet and a "Dashboards" section in the package README; remove
"Dashboards composed of multiple visualizations" from the root Roadmap.
