# PLAN — Free-form dashboard layout

Read `SPEC.md` (same folder) first. Write our own code; never copy Redash code. `react-grid-layout`
2.3.0 is already installed in `client/` (MIT; its deps react-draggable, react-resizable, fast-equals,
resize-observer-polyfill, clsx are MIT). No other new packages. Follow `CLAUDE.md` conventions
(QueryBuilderJson.Options, no MediatR, add child entities via DbSet, comments only for *why*, Zustand
selector rule, TanStack queryFn never undefined). Do NOT edit `src/QueryBuilder.Api/Program.cs` (it holds
a temporary persona block that stays for manual testing). No commits.

## Backend
1. **Domain** `Entities/Dashboard.cs`: `Dashboard.GridVersion` (int, new dashboards = 2); `DashboardTile`
   gets `X` (int), `Y` (int), `AutoHeight` (bool). `Width`/`Height` now mean grid units (2–12 / 2–24).
2. **Application** `Dashboards/DashboardAccess.cs`: constants `GridColumns = 12`, `MinWidth = 2`,
   `MinHeight = 2`, `MaxHeight = 24`, `MaxY = 1000`, `DefaultChart = (6, 6)`, `DefaultText = (6, 3)`;
   remove `AllowedWidths`/`AllowedHeights`.
   New pure static class `DashboardLayout` (same folder):
   - `Compact(IReadOnlyList<(T item, int x, int y, int w, int h)>)` → vertical compaction: sort by y then
     x (stable), place each at the lowest y ≥ 0 that doesn't overlap already-placed tiles in its column
     span; return items with new y and derived order.
   - `FromLegacy(tiles ordered by Order)` → width preset kept as w (clamped 2–12), h = legacyHeight × 3,
     flow left-to-right; a tile that doesn't fit starts a new row below the tallest tile of the row.
   - `BottomY(tiles)` = max(y + h) or 0.
   `DashboardMapper.ToDetail/ToTile`: if `GridVersion == 1`, convert with `FromLegacy` before mapping.
   DTOs (`Dtos/DashboardDtos.cs`): `DashboardTileDto` and `SaveDashboardTileRequest` gain `X`, `Y`,
   `AutoHeight` (restricted tiles keep sending position/size only + Restricted).
   `SaveDashboardTilesCommandHandler`: `ValidateShape` checks w 2–12, x ≥ 0, x + w ≤ 12, y 0–1000,
   h 2–24 (400 `CatalogValidationException` with a clear message); after planning, run `Compact` and
   assign X, Y, Order, Width, Height, AutoHeight; set `GridVersion = 2`. Restricted/unchanged tiles keep
   VisualizationId exactly as today.
   `AddChartToDashboardCommandHandler`: convert legacy first if GridVersion 1 (persist converted
   positions for all tiles and set GridVersion 2), then place the new tile at x 0, y = BottomY, 6×6.
3. **Infrastructure**: `DashboardConfiguration` — `GridVersion` default 1 (existing rows) via
   `HasDefaultValue(1)`; `X`, `Y` default 0; `AutoHeight` default false. New migration
   `AddDashboardGridPositions` via the pinned dotnet-ef command in `CLAUDE.md`. Pure DDL only (no data
   SQL), so the idempotent DBA script stays re-runnable for this migration. New dashboards created by
   `CreateDashboardCommandHandler` set `GridVersion = 2`.
4. **Tests** (`tests/QueryBuilder.Tests`): update `DashFixture` (text tile height 1 → 3; `Req` helpers
   carry X/Y/AutoHeight; fixture dashboard GridVersion 2 with explicit positions) and any test relying on
   preset widths/heights. Add `DashboardLayoutTests`: compaction removes gaps and resolves overlaps;
   `FromLegacy` flow (e.g. widths 6,6,12,4 heights 2,1,3,2 → expected x/y/h); bounds refusals
   (x+w>12, w<2, h>24, y<0) persist nothing; AutoHeight round-trips; restricted tile moved by an Editor
   keeps its VisualizationId; add-chart places at the bottom and converts a legacy dashboard.

## Frontend (`client/src`)
5. Types (`types/index.ts`): tile `x`, `y`, `autoHeight`; request same.
6. **`features/dashboards/DashboardGrid.tsx` rewrite** with `react-grid-layout` v2:
   - `import ReactGridLayout, { useContainerWidth } from 'react-grid-layout'`; import
     `react-grid-layout/css/styles.css` and `react-resizable/css/styles.css`; use
     `moveElement` / `getCompactor('vertical')` from `react-grid-layout/core` for keyboard moves.
   - gridConfig `{ cols: 12, rowHeight: 40, margin: [16, 16], containerPadding: [0, 0] }`;
     dragConfig `{ enabled: editing, cancel: 'button, a, input, textarea, select, [role="menu"], [data-no-drag]' }`;
     resizeConfig `{ enabled: editing, handles: ['se'], handleComponent: <our corner handle> }`;
     item constraints minW 2, maxW 12, minH 2, maxH 24.
   - Container width < 768 → render a plain single-column list (reading order y, x; each tile height =
     h*40 + (h-1)*16 px) and no layout editing.
   - Keep the tile chrome from the redesign (dashed parcel edge for live charts, hatch for restricted,
     `ChartTileBody`, edit strip with grip + `w/12` + menu). The grip is now a visual affordance (whole
     tile drags); the strip's menu gets the D3 keyboard actions; announce moves in an `aria-live="polite"`
     region; keep the existing accessible names (`Reorder …` is replaced by the menu `Layout of …`).
   - Survey grid in edit mode: keep the `survey-grid` utility on the grid container.
   - Placeholder and resize handle themed from tokens (index.css): placeholder
     `color-mix(in srgb, var(--zone-blue) 18%, transparent)` with dashed `--parcel` outline; handle =
     two short ink ticks in the corner, 16px hit area, visible on focus/hover; no animations on first
     mount; transitions off under `prefers-reduced-motion`.
   - `onLayoutChange` → update draft x/y/w/h; `onResizeStop` with changed h → that tile's
     `autoHeight = false`.
7. **Auto-height** (`features/dashboards/useAutoHeight.ts`): for tiles with `autoHeight`, a
   `ResizeObserver` on the tile's content wrapper measures natural content height (chart tiles render
   `ChartRenderer` without `fill` at a natural ~280px height when auto-height is on; text tiles measure
   the note) and sets `h = clamp(ceil((px + 16) / 56), 2, 24)`. Applied in view and edit mode; saved
   with the layout on Save.
8. **`pages/DashboardPage.tsx`**: draft tiles carry x/y/autoHeight; Add chart / Add text place new tiles
   at x 0, y = bottom (6×6 / 6×3); Save sends x/y/w/h/autoHeight; nothing else about the page changes
   (rename, share, refresh, auto-refresh, conflict banner, delete, reload-to-view-mode).
9. Remove now-unused `@dnd-kit` imports from DashboardGrid only (the package stays — the builder uses it).

## Validation
`dotnet test tests/QueryBuilder.Tests`; `cd client && npx tsc -b && npm run build`; refresh wwwroot per
`CLAUDE.md` step 2. Report results honestly with the command output summary; list anything not done.
