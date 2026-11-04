# SPEC — Free-form dashboard layout (drag, resize, auto-adjust, auto-height)

Branch `feature/rea1`. Extends the Phase 2 dashboards (`features/dashboards/SPEC.md`). Decisions made by
the user on 2026-11-03: adopt `react-grid-layout` (MIT); keep explicit Save / Cancel; auto-height for
text and chart tiles.

## 1. Observed Redash behaviour (evidence, not to be copied)
Source: Redash v26.9.0 (`scratchpad/ref/redash-v26.9.0-git`, BSD-2-Clause). All Observation unless marked.

| # | Observation | Evidence |
|---|---|---|
| L1 | Grid: 12 columns, row height 50 incl. margin, margin 15; default widget 6×3; min 2×2; max width 12 | `client/app/config/dashboard-grid-options.js:1-13` |
| L2 | Uses `react-grid-layout` `Responsive` + `WidthProvider`; `isDraggable`/`isResizable` only while editing; drag cancelled on inputs | `components/dashboards/DashboardGrid.jsx:5,241-255` |
| L3 | Widget position stored as `{col,row,sizeX,sizeY,autoHeight}` in widget options | `DashboardGrid.jsx:112-129,217-223`; `services/widget.js:102-113` |
| L4 | Default compaction (react-grid-layout default: vertical) — widgets float up, others are pushed down on collision. *Inference: no compactType override is passed.* | `DashboardGrid.jsx:241-255` |
| L5 | Below 800px a single-column layout is shown and is never saved | `DashboardGrid.jsx:149,176-182,244,254` |
| L6 | Auto-height: per-widget flag; a 200ms poll measures content (header, renderer, footer) and sets `h = ceil((px + margin) / rowHeight)`; a manual height resize turns it off | `AutoHeightController.js:5-13,87-107`; `DashboardGrid.jsx:194-215` |
| L7 | Per-visualization size limits (minColumns, maxColumns, minRows, maxRows, autoHeight default) | `services/widget.js:31-70` |
| L8 | Layout changes auto-save, debounced 2s, one request per changed widget, with Saving/Saved/Failed + retry | `pages/dashboards/hooks/useEditModeHandler.js:37-97` |
| L9 | Legacy layouts are normalised on load: sorted by row, each row placed below the tallest widget of the previous row | `services/dashboard.js:50-80` |
| L10 | *Inference:* no keyboard path to move or resize a widget (react-grid-layout is pointer-only; none added) | `DashboardGrid.jsx` (no key handlers) |

## 2. Desired behaviour (ours)
- **D1 Grid.** 12 columns, row unit 40px with 16px gaps (both axes). Tiles are stored as `x, y, w, h` in
  grid units. Width 2–12, `x + w ≤ 12`; height 2–24 rows; `y` 0–1000. New chart tile 6×6; new text
  tile 6×3.
- **D2 Edit mode.** Drag a tile from anywhere on it (not from its buttons, menus or inputs); resize from
  the bottom-right corner handle; other tiles move out of the way and everything floats up (vertical
  compaction). The 12-column survey grid shows behind the tiles while editing. **Save** stores the
  layout with the existing version check (409 on conflict); **Cancel** discards.
- **D3 Keyboard (keeps today's keyboard support).** Each tile's menu offers Move left / right / up /
  down, Wider / Narrower, Taller / Shorter, width presets (quarter, third, half, two-thirds, full),
  height presets (small 3, medium 6, large 9), Fit height to content, Remove. Every move is announced in
  an `aria-live` region ("Budget share moved to column 7, row 1").
- **D4 Auto-height.** A tile can "Fit height to content" (text and chart). Its height follows the
  measured content (text: the note; chart: header + chart at its natural height + controls).
  Resizing that tile's height by hand, or choosing a height preset, turns it off. The flag is saved.
- **D5 Narrow screens.** Below 768px container width the dashboard is a single column in reading order
  (`y`, then `x`), heights preserved, editing of layout disabled (a short note says to use a wider
  screen); nothing is saved from this view.
- **D6 Server normalisation.** The server validates bounds (400 on any violation), then compacts the
  layout vertically (sort by `y`, `x`; each tile drops to the lowest free row in its column span) so
  overlaps or gaps sent by any client are repaired. `Order` is kept and derived from the compacted
  `(y, x)` order (screen-reader and narrow-screen order).
- **D7 Legacy dashboards.** Existing dashboards (stored with width presets and heights 1–3, no
  positions) are converted on read: heights ×3 rows, tiles flowed left-to-right in `Order` into
  12 columns, each new row starting below the tallest tile of the previous row. They are saved in the
  new format on the next Save. (`Dashboards.GridVersion`: 1 legacy, 2 positioned.)
- **D8 Add to dashboard** (from the query page) and **Add chart / Add text** place the tile at `x = 0`
  below the lowest tile.
- **D9 Unchanged security.** Restricted tiles keep their position and size and still reveal nothing
  else; an Editor can move/resize them; Viewers cannot save; every chart still renders through the
  scoped render endpoint.

## 3. Acceptance criteria
| AC | Criterion |
|---|---|
| G1 | Mouse drag of a tile to a new place, Save, reload → same positions |
| G2 | Corner resize changes `w`/`h`; neighbours are pushed; after Save + reload the sizes persist |
| G3 | Removing a tile in edit mode makes the tiles below float up (no gap) |
| G4 | Keyboard only: open a tile menu, Move right, Wider, Save → persisted; the live region announces |
| G5 | Fit height to content on a text tile: height grows/shrinks with the note; a manual height resize turns it off; flag persists |
| G6 | Server: `x+w>12`, `w<2`, `h>24`, `y<0` → 400 and nothing saved; overlapping input is compacted |
| G7 | A legacy (GridVersion 1) dashboard opens with the same visual order and sizes; Save stores v2 |
| G8 | Program-admin 10 viewing the shared demo dashboard sees the same layout, own-zone chips and the hatched restricted tile in its saved place |
| G9 | At 390px the dashboard is one column in reading order; no horizontal scroll; Edit hidden or disabled |
| G10 | Concurrent edit still returns 409 with the Reload banner |
| G11 | `dotnet test` green; `npm run build`; DBA idempotent script applies the new migration on a fresh DB |
| G12 | No functionality lost: Add chart/text, text edit, share, refresh, auto-refresh, rename, delete still work |
