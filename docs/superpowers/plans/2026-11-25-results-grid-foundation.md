# Results Grid Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the paged Results table with a virtualised "Ledger" data grid: pinned columns, resize/auto-fit/wrap, density, in-app full screen, and cell selection that copies into Excel, without losing anything the grid does today.

**Architecture:** `@tanstack/react-table` keeps column state (order, visibility, pinning, sizing, sorting, search); `@tanstack/react-virtual` renders only the rows on screen. All non-trivial logic (TSV serialisation, selection maths, width fitting, carry-over across re-runs) lives in pure `.ts` files covered by `node --test`; React components only wire them. Client-only: no server change, no migration.

**Tech Stack:** React 19, TypeScript, Tailwind 4, Radix (via `radix-ui`), `@dnd-kit` (existing), `sonner` (existing), `@tanstack/react-table` **8.21.3**, `@tanstack/react-virtual` **3.x** (latest 3.14.x).

**Spec:** `docs/superpowers/specs/2026-11-25-results-grid-foundation-design.md` (read it first; its "Decided behaviour" and "Settled after the builder check" sections are binding). Look: `docs/superpowers/specs/assets/2026-11-25-results-grid-mockup.html`, option A.

## Global Constraints

- Only two new dependencies: `@tanstack/react-table@8.21.3` and `@tanstack/react-virtual@^3.14` (both MIT; v8 chosen over v9 because v9 is a fresh API rewrite from 2026-08). No other package.
- Never use `navigator.clipboard`, `crypto.randomUUID`, `crypto.subtle` anywhere in `client/src`, comments included (`client/tests/insecure-context.test.ts` fails otherwise). Copy works on plain http.
- Zoning Map rules (`DESIGN.md`): no shadow at rest (pinned edge = solid 2px ink, not a shadow; a shadow only on the column being dragged and on menus); numbers in `font-numeric` (Barlow Condensed) with `tabular-nums`; zone yellow only for the truncation warning; Survey Blue (`primary`) for selection and focus; no Query Violet in the grid; input borders on `--input`. Colours come from existing CSS variables so the night theme works; no hard-coded hex in components.
- Density from `useDensityStore` (`state/density-store.ts`); the grid's own switch overrides it locally and never calls `setDensity`.
- Zustand selectors return stable values only (select `s.density`, never a new object).
- `truncate` on a flex child needs `min-w-0`; never put the grid inside Radix `ScrollArea`.
- WCAG 2.1 AA: every control keyboard-operable with visible focus; grid exposes `role="grid"`, `aria-rowcount`, `aria-colcount`, `aria-rowindex`, `aria-colindex`; respect `prefers-reduced-motion` (no smooth-scroll animation when reduced).
- `ResultsTable` keeps its export name and props `result`, `onRowClick?`, `rowActionLabel?`; adds `onAddFilter?: () => void`.
- Values (from the spec): row height `var(--row-h)` (44px comfortable / 32px compact); widths clamped 60–480px; default width = fit of header + first 200 rows, capped at 320px; wrapped text cells clamp at 4 lines; grid min-height 240px; text-only wrap.
- Work on branch `feature/results-grid` (create from `main`). Commit locally per task; never push, never touch NuGet.

## Review Focus

1. **Cell text with tabs, newlines or double quotes** copied to Excel must stay in one cell — `toTsv` quotes such fields (Task 1 tests).
2. **Search or sort changes while cells are selected** must never copy rows that are no longer shown: the selection clears on any change to search, sort, column order, visibility, or result (Task 6 step; Task 2 `clampRange` test).
3. **Re-run where a pinned, hidden, sized or sorted column vanished** must not crash or keep ghost columns (Task 4 tests).
4. **Edge-sized results:** 0 columns, 0 rows, 1 row, 1 column, and 60+ columns must render, scroll and copy (Task 2 bounds tests; Task 8 acceptance).
5. **Plain-http origin:** Ctrl+C and the Copy button work when `isSecureContext` is false (Task 8 acceptance via `http://qb.test`).

---

### Task 0: Branch, dependencies, docs

**Files:**
- Modify: `client/package.json`, `client/package-lock.json`
- Add to git: `docs/superpowers/specs/2026-11-25-results-panel-features.md`, `docs/superpowers/specs/2026-11-25-results-grid-foundation-design.md`, `docs/superpowers/specs/assets/2026-11-25-results-grid-mockup.html`, this plan, and the modified `CLAUDE.md`

- [ ] **Step 1:** `git switch -c feature/results-grid` (from `main` at `94f6102`).
- [ ] **Step 2:** `cd client && npm install @tanstack/react-table@8.21.3 @tanstack/react-virtual@^3.14`.
- [ ] **Step 3:** Check licences of what was resolved: `npm ls @tanstack/react-table @tanstack/react-virtual @tanstack/table-core @tanstack/virtual-core` and `npm view <each>@<resolved> license` → all `MIT`. Stop and report if not.
- [ ] **Step 4:** `npm test` → all existing tests pass; `npm run build` → succeeds.
- [ ] **Step 5:** Commit: `git add client/package.json client/package-lock.json docs/superpowers CLAUDE.md` → message "Results grid: design, plan and TanStack Table/Virtual (MIT)".

---

### Task 1: TSV serialisation and copy helper

**Files:**
- Create: `client/src/features/builder/results/clipboard-tsv.ts` (pure)
- Create: `client/src/features/builder/results/copy-text.ts` (DOM)
- Test: `client/tests/clipboard-tsv.test.ts`

**Interfaces:**
- Produces: `cellToClipboardText(value: unknown, dataType: ColumnDataType): string`; `toTsv(headers: string[], rows: string[][]): string`; `copyText(text: string): boolean` (DOM; returns false when the browser refused).

- [ ] **Step 1: Write the failing tests** (`clipboard-tsv.test.ts`, `import type` + relative `.ts` imports like `compare-cells.test.ts`):
  - `cellToClipboardText(null, 'text') === ''`, `(undefined,'number') === ''`
  - `cellToClipboardText(true,'boolean') === 'TRUE'`, `(false,'boolean') === 'FALSE'`
  - `cellToClipboardText(1234567.5,'number') === '1234567.5'` (no thousands separators)
  - `cellToClipboardText('2026-01-14','date') === '2026-01-14'`
  - `toTsv(['A','B'], [['1','x'],['2','y']]) === 'A\tB\r\n1\tx\r\n2\ty'`
  - field containing `\t`, `\n` or `"` is wrapped in quotes with inner quotes doubled: `toTsv(['N'], [['say "hi"\nthere']]) === 'N\r\n"say ""hi""\nthere"'`
  - plain field with no special chars is not quoted; empty rows array → header line only.
- [ ] **Step 2:** `npm test` → new tests FAIL (module not found).
- [ ] **Step 3:** Implement both functions in `clipboard-tsv.ts`. Lines joined with `\r\n` (Excel's clipboard convention), no trailing newline.
- [ ] **Step 4:** Implement `copyText` in `copy-text.ts`: add a one-shot `copy` listener on `document` that calls `event.clipboardData.setData('text/plain', text)` and `preventDefault()`, then `document.execCommand('copy')`, remove the listener in `finally`; return the `execCommand` result. A short comment says why (no Clipboard API on http).
- [ ] **Step 5:** `npm test` → PASS, including `insecure-context.test.ts`.
- [ ] **Step 6:** Commit "Results grid: TSV serialisation and http-safe copy".

---

### Task 2: Selection model

**Files:**
- Create: `client/src/features/builder/results/selection.ts` (pure)
- Test: `client/tests/grid-selection.test.ts`

**Interfaces:**
- Produces:
  ```ts
  export interface CellPos { row: number; col: number }          // display indexes
  export interface CellRange { anchor: CellPos; focus: CellPos }
  export interface GridBounds { rows: number; cols: number }
  export type MoveKey = 'up' | 'down' | 'left' | 'right' | 'home' | 'end' | 'pageUp' | 'pageDown' | 'ctrlHome' | 'ctrlEnd'
  export function normalize(r: CellRange): { top: number; bottom: number; left: number; right: number }
  export function moveFocus(p: CellPos, key: MoveKey, b: GridBounds, pageRows: number): CellPos
  export function selectAll(b: GridBounds): CellRange | null      // null when rows or cols is 0
  export function clampRange(r: CellRange, b: GridBounds): CellRange | null
  export function rangeContains(r: CellRange, p: CellPos): boolean
  export function rangeSize(r: CellRange): number
  ```
- [ ] **Step 1: Write failing tests:** `normalize` of anchor (5,3) focus (2,1) → top 2 bottom 5 left 1 right 3; `moveFocus` clamps at 0 and at `rows-1`/`cols-1`; `pageDown` moves by `pageRows` and clamps; `home` → col 0 same row, `ctrlEnd` → last row and col; `selectAll({rows:0,cols:3}) === null`, `selectAll({rows:2,cols:3})` covers (0,0)–(1,2); `clampRange` shrinks a range past the new bounds and returns null for empty bounds; `rangeSize` of a 4×2 range = 8; `rangeContains` true for corners, false just outside.
- [ ] **Step 2:** `npm test` → FAIL.
- [ ] **Step 3:** Implement in `selection.ts`.
- [ ] **Step 4:** `npm test` → PASS.
- [ ] **Step 5:** Commit "Results grid: cell selection model".

---

### Task 3: Column width fitting

**Files:**
- Create: `client/src/features/builder/results/column-widths.ts` (pure)
- Test: `client/tests/column-widths.test.ts`

**Interfaces:**
- Produces:
  ```ts
  export const MIN_COL_WIDTH = 60
  export const MAX_COL_WIDTH = 480
  export const DEFAULT_COL_CAP = 320
  export const FIT_SAMPLE_ROWS = 200
  export function fitWidth(header: string, values: string[], measure: (text: string, kind: 'header' | 'cell') => number, extra: number, cap?: number): number
  ```
  `extra` = horizontal padding + header affordances (type badge, sort mark, menu button) in px, added to the header measurement; cells get padding only (builder decides the split, document it in a const). Result = `clamp(max(measuredHeader+extra, max(measuredCells)+padding), MIN, cap ?? MAX)`; rounds up to whole px. Only the first `FIT_SAMPLE_ROWS` values are measured.
- [ ] **Step 1: Write failing tests** with a fake `measure = (t) => t.length * 7`: short header and values → 60 (min); 100-char value with `cap = DEFAULT_COL_CAP` → 320; the same with `cap` omitted → 480 (MAX); header wider than every value wins; only first 200 of 1,000 values are measured (put a huge value at index 500 → ignored).
- [ ] **Step 2:** `npm test` → FAIL.
- [ ] **Step 3:** Implement. Also export `createCanvasMeasure(font: { header: string; cell: string }): (text, kind) => number` in the same file that uses an offscreen `<canvas>` 2D context `measureText` (not unit-tested; guarded so the module still imports under Node: create the canvas lazily on first call).
- [ ] **Step 4:** `npm test` → PASS.
- [ ] **Step 5:** Commit "Results grid: column width fitting".

---

### Task 4: View state and carry-over across re-runs

**Files:**
- Create: `client/src/features/builder/results/grid-view.ts` (pure)
- Test: `client/tests/grid-view.test.ts`

**Interfaces:**
- Produces:
  ```ts
  import type { Density } from '../../../state/density-store.ts'   // type-only
  export interface GridSort { column: string; direction: 'asc' | 'desc' }
  export interface GridView {
    order: string[]                 // all column names in display order (incl. hidden)
    hidden: string[]
    pinnedLeft: string[]
    pinnedRight: string[]
    widths: Record<string, number>  // only user-set or fitted widths
    sort: GridSort | null
    search: string
    density: Density | null         // null = follow the app setting
    wrap: boolean
  }
  export function defaultView(columns: string[]): GridView
  export function carryOver(prev: GridView, prevColumns: string[], nextColumns: string[]): GridView
  export function isDefaultView(v: GridView, columns: string[]): boolean
  export function nextSort(current: GridSort | null, column: string): GridSort | null   // asc → desc → none, other column → asc
  ```
- [ ] **Step 1: Write failing tests:**
  - `carryOver` with the same set of names in a different order keeps `order`, `hidden`, pins, `widths`, `sort`, `search`, `density`, `wrap` unchanged.
  - with a different set → equals `defaultView(nextColumns)` (full reset), even if most names overlap.
  - `isDefaultView(defaultView(c), c) === true`; false after any of: hide, pin, width, sort, search, density override, wrap, reorder.
  - `nextSort(null,'A')` → A asc; A asc → A desc; A desc → null; A asc then `'B'` → B asc.
  - (Review Focus 3) `carryOver` where `prev` references a column missing from `nextColumns` → full reset, and the result contains no name outside `nextColumns`.
- [ ] **Step 2:** `npm test` → FAIL.
- [ ] **Step 3:** Implement. "Same set" = equal size and every name present (order ignored); spec settles that sort/search are kept on a same-set re-run.
- [ ] **Step 4:** `npm test` → PASS.
- [ ] **Step 5:** Commit "Results grid: view state and re-run carry-over".

---

### Task 5: Table hook

**Files:**
- Create: `client/src/features/builder/results/useResultsGrid.ts` (the old `useResultsView.ts` stays until Task 7 deletes it)

**Interfaces:**
- Consumes: `GridView`, `defaultView`, `carryOver`, `nextSort`, `isDefaultView` (Task 4); `compareCells` (existing, unchanged); `useDensityStore`.
- Produces:
  ```ts
  export function useResultsGrid(result: QueryResultDto): {
    table: Table<Record<string, unknown>>   // @tanstack/react-table instance, state driven by `view`
    view: GridView
    setView: (update: (v: GridView) => GridView) => void
    rows: Row<Record<string, unknown>>[]    // after search + sort, display order
    columns: QueryResultColumnDto[]         // visible, in display order: left-pinned, centre, right-pinned
    density: Density                        // view.density ?? app density
    isDefault: boolean
    reset: () => void
    totalRaw: number
  }
  ```
- [ ] **Step 1:** Implement with `useReactTable` + `getCoreRowModel`, `getSortedRowModel`, `getFilteredRowModel`. Map `GridView` → TanStack state (`columnOrder`, `columnVisibility`, `columnPinning`, `columnSizing`, `sorting`, `globalFilter`) as derived values; write changes back through `setView` only (single source of truth = `GridView`). Sorting uses a custom `sortingFn` calling `compareCells(a, b, dataType === 'number')`, and must reproduce today's order exactly: today's `[...rows].sort(direction * compareCells)` puts nulls last ascending and *first* descending (descending = negated comparator). Don't let TanStack's `sortUndefined` or `invertSorting` change that. Global filter = today's rule: case-insensitive substring over **visible** columns, `String(value ?? '')`. Default column size from `fitWidth(..., DEFAULT_COL_CAP)` computed once per result with `createCanvasMeasure`, memoised by `result`.
- [ ] **Step 2:** On a new `result` reference, replace `view` with `carryOver(prev, prevNames, nextNames)` (keep prev names in a ref).
- [ ] **Step 3:** `npx tsc -b` from `client/` → no errors.
- [ ] **Step 4:** Commit "Results grid: table hook over TanStack Table".

---

### Task 6: Grid body: Ledger rendering, virtual scroll, pinning, resize, header menu, selection, copy

**Files:**
- Create: `client/src/features/builder/results/ResultsGrid.tsx` (scroll container, header row, virtual rows)
- Create: `client/src/features/builder/results/GridHeaderCell.tsx` (label/sort, type badge, menu, resize handle, dnd sortable)
- Create: `client/src/features/builder/results/GridCell.tsx` (value rendering: null/boolean/number/text, wrap clamp)
- Create: `client/src/features/builder/results/useGridSelection.ts` (selection state + keyboard + copy wiring)

**Interfaces:**
- Consumes: Task 1 `copyText`, `toTsv`, `cellToClipboardText`; Task 2 everything; Task 3 `fitWidth`, `createCanvasMeasure`, `MIN_COL_WIDTH`, `MAX_COL_WIDTH`; Task 5 hook output.
- Produces:
  ```ts
  export function ResultsGrid(props: {
    grid: ReturnType<typeof useResultsGrid>
    onRowClick?: (row: Record<string, unknown>) => void
    rowActionLabel?: string
    onVisibleRangeChange?: (first: number, last: number) => void    // 1-based display rows in view, for the footer
    onSelectionChange?: (cells: number) => void                      // 0 when none
    copyRef?: React.Ref<{ copy: () => void }>                         // toolbar Copy button calls this
  }): JSX.Element
  ```
- [ ] **Step 1: Layout.** One scroll container (`role="grid"`, `tabIndex=0`, `overflow:auto`, fills parent, `min-h-60`). CSS grid or absolutely positioned rows (builder's choice) with `useVirtualizer` over rows (`estimateSize` = current row height px; `measureElement` only when `view.wrap`). Sticky header row (`top:0`); sticky row-number column (`left:0`); left-pinned columns `position:sticky` with cumulative `left`, right-pinned with cumulative `right`; 2px solid `--foreground` edge on the last left-pinned / first right-pinned column; pin glyph in its header. Ledger look per mockup A: hairline cell borders (`--border` at reduced strength), tinted header with 1px solid ink bottom rule, row-number column on `--background` (map ground) in `font-numeric`, row hover tint, numbers right-aligned `font-numeric tabular-nums`. Decide column virtualisation from a 60-column test (spec leaves it to you); record the decision in a one-line comment.
- [ ] **Step 2: Header cell.** Label button cycles sort via `nextSort` (sort mark ▲/▼, faint ⇅ when none, `aria-sort`). Type badge text from `dataType`: text→`ABC`, number→`123`, date→`DATE`, dateTime→`TIME`, boolean→`Y/N`, guid→`ID`, unknown→`?`. Menu (Radix `DropdownMenu`, trigger visible on header hover/focus and always reachable by Tab): Sort ascending · Sort descending · Clear sort | Pin to left · Pin to right · Unpin (show only the applicable ones) | Fit to content · Hide column | Copy column values. Drag handle reorders via `@dnd-kit` sortable as today, horizontal only, within the column's pinned group. Resize handle on the right edge: pointer drag sets `widths[name]` clamped 60–480; double-click = fit with `fitWidth(..., MAX)` on all displayed rows' first 200; keyboard: focused handle + ArrowLeft/ArrowRight ±16px (`aria-label="Resize <name>"`, `role="separator"`, `aria-valuenow`).
- [ ] **Step 3: Cells.** `GridCell`: null/undefined → muted italic `null` (as today); boolean → check / x icons (as today); number → `toLocaleString()` in `font-numeric`; else text. Wrap off: single line, `truncate`, `title` = full text. Wrap on and `dataType === 'text'`: `whitespace-normal` + `line-clamp-4`, `title` = full text.
- [ ] **Step 4: Selection** (`useGridSelection`): state `CellRange | null`. Mouse: pointerdown on a cell sets anchor=focus (shift extends focus), drag across cells extends, pointerup ends. Keyboard on the grid: arrows/Home/End/PageUp/PageDown/Ctrl+Home/Ctrl+End via `moveFocus` (Shift extends), Ctrl/Cmd+A → `selectAll`, Esc clears; the virtualiser scrolls the focus cell into view (`scrollToIndex`, `behavior:'auto'`). Active range: `bg-primary/10` wash, focus cell `outline-2 outline-primary -outline-offset-2`; `aria-selected` on selected cells. (Review Focus 2) Clear the selection whenever search, sort, order, visibility, pinning or `result` changes; when only bounds shrink, use `clampRange`.
- [ ] **Step 5: Copy.** A `copy` listener on the grid element: if a range exists, build `toTsv(headers of the range's visible columns in display order, rows in display order mapped with cellToClipboardText)`, set `text/plain`, `preventDefault`, toast `Copied N cells` (N = `rangeSize`). `copyRef.copy()` (toolbar): same via `copyText`, and with no selection copies all displayed rows × visible columns (toast "Copied N rows"); failure → `toast.error('Copy failed. Select cells and press Ctrl+C.')`. "Copy column values" = header + that column for all displayed rows via `copyText`.
- [ ] **Step 6: Row drill.** When `onRowClick` is set, the row-number cell shows a small icon button (lucide `ListTree` or similar) on row hover and whenever the row holds the focus cell, `aria-label={rowActionLabel}`; click calls `onRowClick(row.original)`. Enter on the grid with a focus cell calls it for that row. Without `onRowClick`, Enter does nothing.
- [ ] **Step 7:** `npx tsc -b` → clean; `npm run lint` → no new errors; `npm test` → PASS.
- [ ] **Step 8:** Commit "Results grid: Ledger grid with virtual rows, pinning, resize, selection and copy".

---

### Task 7: Toolbar, warning, footer, density, wrap, full screen; switch `ResultsTable` over

**Files:**
- Rewrite: `client/src/features/builder/ResultsTable.tsx`
- Rewrite: `client/src/features/builder/results/ResultsToolbar.tsx`
- Create: `client/src/features/builder/results/ResultsFooter.tsx`
- Delete: `client/src/features/builder/results/useResultsView.ts`, `client/src/features/builder/results/ResultsPagination.tsx`
- Modify: `client/src/pages/QueryEditorPage.tsx:545-549` (pass `onAddFilter={() => setActiveTab('build')}`)

**Interfaces:**
- Consumes: Task 5 hook, Task 6 `ResultsGrid`.
- Produces: `ResultsTable({ result, onRowClick?, rowActionLabel?, onAddFilter? })`.

- [ ] **Step 1: Toolbar** (left → right, wraps on narrow widths): search input ("Search {N} rows…", N = `result.rows.length.toLocaleString()`, `h-7`, Input Edge border); Columns menu (checkbox per column with type icon, badge `visible/total`, as today); separator; density segmented control (`role="radiogroup"`, "Comfortable"/"Compact", reflects `grid.density`, sets `view.density`); Wrap text toggle (`aria-pressed`); separator; Copy (calls `copyRef.current.copy()`); Reset view (only when `!grid.isDefault`, tooltip "Reset columns, sort, search and layout"); right side in `font-numeric` muted: `ROWS {rowCount}` · `COLS {visible}` · `RAN IN {executionTimeMs} ms`; Full screen button (`aria-label` "Full screen" / "Exit full screen", `aria-pressed`).
- [ ] **Step 2: Warning** when `result.truncated`: yellow warning row (existing `warning` token styling, zone-yellow swatch), text: `More than {rowLimit} rows matched. You're seeing the first {rowLimit}, so sorting, search and copy here cover those rows only.` plus an "Add a filter" text button when `onAddFilter` is set.
- [ ] **Step 3: Footer** (`font-numeric`, map ground, top border): `{first}–{last} of {totalFiltered} visible` from `onVisibleRangeChange`; `Pinned: a, b` when any; `{n} cells selected` when `onSelectionChange` reports > 0. Search narrowing shows `of {filtered} (filtered from {raw})`.
- [ ] **Step 4: Density** applied by setting `data-density` on the `ResultsTable` root element to `grid.density` (the CSS variables cascade, so `--row-h` and `--text-body` follow for the grid only).
- [ ] **Step 5: Full screen:** state in `ResultsTable`; when on, render the same root inside a `fixed inset-0 z-50 bg-background` container (portal to `document.body` so it covers the app rail), Esc exits (unless a menu is open — Radix stops propagation; check), focus returns to the Full screen button on exit; body scroll locked while on.
- [ ] **Step 6: Empty states** unchanged in wording: no columns → "Query returned no columns."; zero rows → "No rows matched your filters. Try widening a condition."; search with no matches → "No rows match your search." (toolbar stays visible so the search can be cleared).
- [ ] **Step 7:** Delete the two old files; `QueryEditorPage` passes `onAddFilter`; DrillPage unchanged (passes `result` only).
- [ ] **Step 8:** `npx tsc -b`, `npm run lint`, `npm test` → all clean/PASS.
- [ ] **Step 9:** Commit "Results grid: toolbar, footer, density, wrap and full screen; ResultsTable uses the new grid".

---

### Task 8: Bundle, acceptance, docs

**Files:**
- Modify: `src/QueryBuilder.Editor/wwwroot/index.html`, `src/QueryBuilder.Editor/wwwroot/assets/*`
- Modify: `docs/superpowers/specs/2026-11-25-results-panel-features.md` (items #1–5, #16 → `Done (unreleased)`)
- Modify: `README.md`, `src/QueryBuilder.Editor/README.md` (results grid features, one short section each; keep "What's included" accurate)

- [ ] **Step 1:** `cd client && npm run build`, then copy the bundle per CLAUDE.md step 2 (`rm -rf src/QueryBuilder.Editor/wwwroot/assets`, copy `client/dist/assets` and `client/dist/index.html`).
- [ ] **Step 2: Browser acceptance (Opus runs this, Playwright, sample app at `http://localhost:5080/querybuilder` with the DevPersonas block re-added temporarily per CLAUDE.md, reverted after).** Checklist — each must pass:
  - run a query with > 1,000 rows: warning shows; "Add a filter" opens Build; scroll top → bottom smoothly; row 1000 reachable; header and row numbers stay fixed.
  - pin a column left and another right; scroll horizontally; both stay; ink edge visible; unpin.
  - drag-resize, double-click fit, keyboard resize; wrap on → long text wraps to ≤ 4 lines, numbers don't.
  - density switch changes row height of the grid only; app density toggle unaffected.
  - full screen covers the rail; Esc exits; focus returns.
  - select a 4×2 range by drag and by shift+arrows; Ctrl+C; paste into a textarea → TSV with header row; a value with a newline stays quoted. Repeat on `http://qb.test` (route to localhost:5080, `isSecureContext === false`) for Ctrl+C and the Copy button.
  - sort cycles asc → desc → none on header click; search filters; selection clears on sort/search.
  - reorder by drag still works; Columns menu hides/shows; Reset view restores everything.
  - re-run with the same columns keeps pins/widths/wrap; change the columns in Build and re-run → reset.
  - aggregated saved query: hover row number → drill button opens the drill page; Enter on a selected cell does the same; the drill page shows the new grid.
  - 60+ column result scrolls horizontally without lag; 0-row and 1-row results render.
  - light and night themes, both densities: screenshot check (wait ~1 s after render); no shadow at rest; keyboard Tab reaches search, toolbar, header menus, resize handles, grid.
- [ ] **Step 3:** Fix anything found (one batch), re-run the failing checks once.
- [ ] **Step 4:** Update the feature list statuses and both READMEs.
- [ ] **Step 5:** Commit "Results grid: bundle, docs and feature list" (stage `wwwroot` via `git status` review — hashed asset names churn; never `git add -A`).
