# Verification — Free-form dashboard layout

Run on 2026-11-03 on branch `feature/rea1` (uncommitted). Local only: LocalDB `QueryBuilderAppDb`,
synthetic demo queries/charts, temporary dev personas in `Program.cs` (still present for manual testing;
must be reverted before packaging). Throwaway dashboards were deleted afterwards.

## Automated
- `dotnet test tests/QueryBuilder.Tests`: **177 passed / 0 failed** (was 161; +8 layout, +8 bounds cases,
  +2 add-chart placement/legacy).
- `npx tsc -b` clean; `npm run build` ok; wwwroot refreshed.
- New package `react-grid-layout` 2.3.0 — MIT, and its dependencies react-draggable, react-resizable,
  fast-equals, resize-observer-polyfill, clsx are all MIT (checked in `node_modules/*/package.json`).

## Runtime acceptance (SPEC §3)
| AC | Result | Evidence |
|---|---|---|
| G1 drag | PASS | Mouse drag moved "Budget trend" from (0,12) to (6,9); Save + reload kept it |
| G2 corner resize | PASS (after fix) | "Budget share" 5×6 → 7×7. First run FAILED: the custom handle was not in the drag-cancel list, so a corner drag moved the tile; fixed in `DashboardGrid.tsx` |
| G3 auto-adjust on remove | PASS | Removing the top-left chart floated the tiles below up (y 6 → 0) |
| G4 keyboard | PASS | Menu Move right + Narrower: (0,6,6×6) → (1,6,5×6); live region "Budget trend moved to column 6, row 21" |
| G5 auto-height | PASS | Text tile with Fit height grew to 7 rows for 14 lines; a manual height resize turned it off (6×13, flag cleared) |
| G6 server bounds/compaction | PASS | x+w>12, w<2, h>24, y<0 → 400, version unchanged; overlapping/gapped input compacted to `[0,0] [9,0] [3,6]` |
| G7 legacy | PASS | GridVersion-1 demo dashboard read as 2×2 of 6×6 tiles in original order, no DB write |
| G8 scoped viewer | PASS | Program-admin 10: 4 tiles in saved places, own-zone chips, 1 hatched restricted tile, no names leaked, no Edit |
| G9 narrow | PASS | 390px: no grid items (single column list), no horizontal scroll, Edit disabled |
| G10 conflict | PASS | API stale version → 409; UI stale save → Reload banner |
| G11 DBA script | PASS | Fresh idempotent script applied with `sqlcmd -I -b` to a throwaway DB: 9 migrations, `X`/`Y` int default 0, `AutoHeight` bit default 0, `GridVersion` int default 1; DB dropped |
| G12 no regressions | PASS | Refresh all, Share dialog, Add text, Add chart, Add to dashboard (placed at bottom), 0 page errors |

## Fixes made during verification
1. Corner resize moved the tile (handle missing from drag-cancel selector) — fixed.
2. Resize handle marks were visible in view mode (the library renders handles even when resizing is off)
   — now hidden unless editing (`.dash-grid.is-editing`).

## Limits
- No side-by-side runtime comparison with the local Redash instance (not driven); behaviour is matched
  from source evidence (SPEC §1), not parity-tested.
- Touch drag/resize on real tablets not exercised.
- The DBA script for the release is regenerated only at the 1.0.14 bump (not done).
