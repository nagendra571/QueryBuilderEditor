# Results grid, phase 1: the foundation — design

Date: 2026-11-25. Status: approved in conversation (approach B, look A "Ledger", behaviour list below).
Feature list and tracking: `docs/superpowers/specs/2026-11-25-results-panel-features.md` (this phase =
items #1–5 and #16). Mockup (committed copy): `docs/superpowers/specs/assets/2026-11-25-results-grid-mockup.html`
— option A is the approved look; the mockup is the reference for header, hairline and badge treatment.

## Intent

After **Run**, the Results tab shows a plain paged table. QueryBuilder is used by many kinds of
businesses, and the goal is a results panel people can *work in* for a while before moving on to
charts: professional, dense, enterprise-grade, in the existing Zoning Map design world. Phase 1 rebuilds
the grid itself so later phases (side panels, filters, formatting, remembered layouts) have something
solid to hang off.

## Goals

1. Smooth scrolling through every loaded row; no pages. Header and row-number column stay fixed.
2. Pin columns to the left or right (any number).
3. Resize columns by dragging the header edge; double-click the edge to fit content. One "Wrap text"
   switch for the whole grid; wrapped rows grow taller.
4. Density: Comfortable / Compact.
5. Full screen.
6. Cell selection and copy that pastes cleanly into Excel.

## Non-goals (later phases, see the feature list)

Selection totals, record panel, column profile, per-column filters, "add to query", totals row,
conditional formatting, number/date formats, remembering the layout across visits (#17, a database
decision), "chart this column", multi-sort, grouping.

## Anti-goals (would be a failure even if it works)

- Losing anything that works today: search, column show/hide, drag-to-reorder columns, click-to-sort
  (asc → desc → none), reset view, run time, the truncation warning, the empty states ("Query returned
  no columns.", no rows, no search matches), row drill-down, keyboard operation. (PRODUCT.md principle 4.)
- Numbers that look query-wide but only cover the loaded rows, without saying so.
- Anything that breaks on plain http (the hosted site is http; see constraints).
- A paid library, or a look that drifts from Zoning Map (no shadows at rest, zone colours only for
  meaning, violet only for query state).

## Constraints

- **Rows are client-side only.** The grid holds what the run returned: 1,000 by default
  (`maxRows: 1000` in `QueryEditorPage.tsx`, `RecordLimits.LegacyGridDefaultRows`), or fewer when a
  record limit applies (`result.truncated`, `result.rowLimit`). Sorting, search and copy act on those
  rows. Export and charts re-run on the server and ignore grid state. No server change in this phase.
- **Insecure context.** `navigator.clipboard`, `crypto.randomUUID` and `crypto.subtle` are undefined on
  http, and `client/tests/insecure-context.test.ts` rejects them anywhere in `client/src` (comments
  included). Copy must use the DOM `copy` event (`event.clipboardData.setData(...)`) for Ctrl+C and a
  `document.execCommand('copy')` path (e.g. a temporary selection whose `copy` event we fill) for the
  Copy button and menu items.
- **Design world:** `DESIGN.md` + `.impeccable/surfaces/client-src.md`. Density is a system switch
  (`[data-density]` on `<html>`, `state/density-store.ts`, tokens `--row-h` 44px / 32px compact);
  numbers in Barlow Condensed with tabular figures; Flat Map Rule (shadow only while dragging);
  Input Edge for input borders; WCAG 2.1 AA, visible focus, reduced motion respected.
- Known sharp edges from CLAUDE.md that apply here: Zustand selectors must not return new
  arrays/objects; `truncate` on a flex child needs `min-w-0`; Radix ScrollArea's inner table div (don't
  put the grid inside `ScrollArea`); Radix Select may call `onValueChange('')`.
- No new dependency beyond the two approved below; both MIT (checked: `@tanstack/react-table` 9.3.1 →
  `@tanstack/table-core`, `@tanstack/react-store`/`@tanstack/store`; `@tanstack/react-virtual` 3.14.x →
  `@tanstack/virtual-core`). Re-check the licences of the exact resolved versions at install time.

## Approaches considered

- **A. Keep the plain table, add only `@tanstack/react-virtual`.** Smallest addition, but we would
  hand-write widths, pin offsets, ordering, visibility and later filters/grouping.
- **B. `@tanstack/react-table` (headless) + `@tanstack/react-virtual`. CHOSEN.** The library keeps the
  column bookkeeping (sizing, pinning, order, visibility, sorting, and later filtering, grouping,
  aggregation); we render every pixel in Zoning Map. ~20 KB. Rewrites `ResultsTable` (~220 lines).
- **C. A complete grid.** AG Grid Community: the next-phase features (range selection, status bar, set
  filter, grouping) are in the paid Enterprise edition, and it is heavy and hard to theme. Glide Data
  Grid: canvas rendering, weaker accessibility and theming. Rejected.

## Decided behaviour

**Look: "Ledger"** (mockup option A). Every cell has a hairline box (`#e4e9e2`-ish, derive from tokens
so night theme works), header tinted with a solid 1px ink rule beneath, row-number column on the map
ground in Barlow, row hover a faint ground tint. Numeric columns right-aligned in Barlow tabular.
Type badge (ABC / 123 / DATE / Y/N) in each header, replacing or restyling `DataTypeIcon`.

**Toolbar** (left → right): search ("Search 1,000 rows…", count from the loaded rows), Columns
(show/hide, with visible/total count), separator, density segmented switch, Wrap text toggle,
separator, Copy, Reset view (only when the view differs from default), then right-aligned Barlow meta
"ROWS 1,000 · COLS 11 · RAN IN 184 ms" and the Full screen button. Wraps sensibly on narrow widths.

**Truncation warning:** yellow warning style (`zone-yellow` swatch, `zone-yellow-ink` text). Text says
only the first N are shown and that sorting, search and copy cover those rows only. An "Add a filter"
action switches the editor to the Build tab (the grid gets an optional callback; DrillPage may omit it).

1. **Scrolling.** All loaded rows in one scroll area, virtualised rows (and columns if needed for wide
   results); sticky header; sticky row-number column. Pages and the page-size picker go away.
2. **Pinning.** Header menu: Pin to left / Pin to right / Unpin. Several columns may be pinned. Pinned
   columns stick while scrolling horizontally; the boundary gets a solid 2px ink edge (no shadow) and
   the header shows a pin mark. The row-number column is always leftmost.
3. **Widths and wrap.** Drag the header's right edge to resize (keyboard alternative in the header
   menu or via arrow keys on a focused resize handle); double-click the edge or menu "Fit to content"
   auto-fits to the widest of header and loaded values (measure a sample, cap at a sensible maximum).
   Wrap text on: long text wraps, rows grow (virtualiser measures dynamic heights). Off: truncate with
   the full value in a tooltip/title.
4. **Density.** Defaults to the app's density (`density-store`); the grid's switch overrides it for
   this grid only (not written back to the app setting).
5. **Full screen.** In-app: the results panel covers the whole app window (fixed overlay with the
   toolbar and footer), Esc or the button exits, focus returns to the button. Not the browser
   Fullscreen API, so Radix menus/dialogs keep working.
6. **Selection and copy.** Click selects a cell (active cell, Survey Blue outline, blue wash on the
   range); shift-click or drag extends a rectangular range; arrow keys move, Shift+arrows extend,
   Ctrl/Cmd+A selects all loaded rows (after search/sort), Esc clears. Ctrl/Cmd+C and the Copy button
   copy the selection as tab-separated text with a header row (visible column names, in display
   order), rows in current display order; nulls as empty, booleans as TRUE/FALSE, numbers unformatted
   (no thousands separators) so Excel reads them as numbers; tabs/newlines inside values quoted per
   Excel's TSV rules. With no selection, Copy copies all loaded rows (visible columns). Also offer
   text/html on the clipboard event if cheap (optional). Toast confirms ("Copied 8 cells").
   Grid uses `role="grid"` semantics with `aria-rowcount`/`aria-colcount`/indexes so virtualised rows
   stay understandable to screen readers.
7. **Header menu** (button in each header, also opened by keyboard): Sort ascending, Sort descending,
   Clear sort; Pin left / Pin right / Unpin; Fit to content; Hide column; Copy column values. Disabled
   placeholders for later phases are optional (mockup showed "Profile this column", "Filter…"); if
   shown, they must not look broken. Clicking the header label still cycles sort asc → desc → none.
   Drag-to-reorder columns keeps working (`@dnd-kit`, as today), pinned columns reorder within their
   pinned group.
8. **Row drill-down.** Today `onRowClick` makes the whole row a button (saved aggregated queries
   only). Because clicks now select cells, a small "see rows" icon button appears in the row-number
   cell on row hover/focus (always visible to keyboard focus), labelled with `rowActionLabel`; Enter
   on a selected cell triggers it too. `DrillPage` uses the same grid without drill.
9. **Re-runs.** A new `result` with the same column names (same set, any order) keeps pins, widths,
   hidden columns, order, density override and wrap; sort and search reset to defaults only if their
   column vanished. Different columns → full reset (today's behaviour). Nothing persisted across
   visits (that is #17).
10. **Footer:** Barlow "1–40 of 1,000 visible" (rows in view), "Pinned: …" when any, and when a range
    is selected, its size ("8 cells selected"). Selection totals are phase 2; leave room.
11. **Empty states and errors** stay as today, restyled.

## Settled after the builder check (designer's calls unless marked)

- **Height.** The grid fills its host: an `h-full` flex column (toolbar, optional warning, scroll
  area `min-h-0 flex-1`, footer). Both hosts already give it a bounded box: the Results
  `TabsContent` is `min-h-0 flex-1 overflow-hidden`, DrillPage wraps it in `min-h-0 flex-1`. Toolbar
  and footer are always visible; only the cell area scrolls. Full screen = same component in a fixed
  overlay. Keep a min-height (~240px) so a short window still shows rows.
- **Row heights with wrap.** Rows have a fixed height (`--row-h`) when wrap is off. With wrap on, the
  virtualiser estimates (row height) and measures rendered rows (`measureElement`); wrapped cells are
  capped at 4 lines (`line-clamp`) with the full value in the title/tooltip, which keeps scrolling and
  scroll-to-active-cell stable. Small scrollbar adjustments as rows are measured are acceptable.
- **Wrap and fit by type.** Wrap applies to text columns only; numbers, dates and booleans never wrap.
  Fit to content applies to every column; width range 60px–480px (default width ~ fit of header +
  first 200 rows, capped at 320px).
- **Copy column values** copies that column's header plus its values for the rows currently
  displayed (after search, in current sort order), same serialisation as selection copy.
- **Old files.** `ResultsPagination.tsx` is deleted. `useResultsView.ts` may be rewritten or replaced
  freely (only `ResultsTable.tsx` imports it). `compare-cells.ts` stays, with its current behaviour
  (including null ordering) unchanged; it has tests in `client/tests/compare-cells.test.ts`.
- **Sorting** keeps today's semantics exactly: single column, header click cycles asc → desc → none,
  `compareCells` with the numeric flag for number columns.
- **Toast:** `sonner` is already used (`toast` from `sonner`, as in `QueryEditorPage.tsx`); use it.
  Also announce copy results through it (sonner toasts are announced to screen readers).
- **`ResultsTable` props stay compatible:** `result`, `onRowClick?`, `rowActionLabel?`, plus a new
  optional `onAddFilter?` for the warning's action. DrillPage passes only `result`.

## Left to the builder

- Component structure, file names, and how state is split (keep pure logic — copy/TSV serialisation,
  range maths, width measurement, re-run carry-over — in plain `.ts` files under
  `features/builder/results/` with `import type` + relative imports so `node --test` can cover them,
  as other pure model files do).
- Exact pixel values within the tokens, icons (lucide), the header-menu trigger affordance.
- Whether to virtualise columns as well as rows (decide from a wide-result test, e.g. 60+ columns).
- Tests: Node unit tests for the pure files; browser acceptance (Playwright) on localhost and on a
  fake-http origin (`context.route('http://qb.test/**', …)`) for copy; screenshot check in both themes
  and both densities.
- Build/ship steps per CLAUDE.md release workflow (client build, copy bundle to `wwwroot`). Version bump
  and NuGet only when the user asks; this is client-only, so a patch/minor bump of Editor (2.3.0
  suggested since it is a visible feature) is the user's call at release time.
