# Results Details Panel and Selection Totals Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a docked Details panel (Row and Column tabs that follow the active cell) and footer selection totals to the query Results grid.

**Architecture:**
- **Pure logic** lives in `.ts` files under `client/src/features/builder/results/`, tested with `node --test`:
  - column profiles;
  - selection totals;
  - the selection store;
  - panel width and mode;
  - record copy text.
- **Data flow.** The grid publishes its selection range, displayed rows and visible ordered columns into a small external store. `ResultsTable` creates the store once. The panel and the footer read it with `useSyncExternalStore`, so moving the cursor never re-renders `ResultsTable` or `ResultsGrid`.
- **Prev/Next** drive the grid through an imperative `gridRef` handle.

**Tech Stack:** React 19, TypeScript, Tailwind 4, shadcn/Radix (`radix-ui` Tabs), lucide, sonner, @tanstack/react-table 8.21.3, @tanstack/react-virtual 3.14.28. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-11-26-results-details-panel-design.md` (approved). Phase 1 spec `docs/superpowers/specs/2026-11-25-results-grid-foundation-design.md` still binds.

## Global Constraints

- **No new npm dependency.** Never touch `package.json` or the lockfile.
- **No `navigator.clipboard`, `crypto.randomUUID` or `crypto.subtle` anywhere in `client/src`, comments included.** `tests/insecure-context.test.ts` enforces this. Copy goes through `copyText` (`results/copy-text.ts`).
- **Pure files** use `import type` for `@/…` types and relative `.ts` extensions for runtime imports (e.g. `import { toNumber } from '../../visualizations/chart-data.ts'`), so `node --test` can load them.
- **Tests** go in `client/tests/<name>.test.ts`, using `node:test` and `node:assert/strict`. Run `cd client && npm test`.
- **Zoning Map rules:**
  - no shadows (Flat Map Rule);
  - numbers in `font-numeric tabular-nums` (Barlow);
  - yellow (`bg-warning/20 text-foreground`, `bg-warning` swatch) only for the truncation note;
  - primary blue for active/selected marks;
  - no gradients or animation on histograms;
  - respect `prefers-reduced-motion`.
- **Zustand/`useSyncExternalStore` selectors** must return the stored snapshot object or primitives, never a newly built object or array.
- **Comments** explain only *why*, and only for non-obvious constraints.
- **Panel size:**
  - width default 340px, min 280px, max 560px, and never more than 50% of the grid area's width;
  - keyboard resize step 16px;
  - overlay (not docked) when the grid area is narrower than 900px.
- **Histograms:** about 20 equal-width bins; a single bin when min = max.
- **Text profile:** top 10 values (ties alphabetical) plus an "Other values" remainder.
- **Totals:**
  - shown only for 2+ selected cells;
  - Count = non-empty cells;
  - Sum/Average/Min/Max over number-column cells whose value parses as a finite number;
  - Average capped at 4 decimals;
  - above 1,000,000 selected cells show Count only, with title "Totals skipped for very large selections".
- **Copy strings (exact):**
  - toast "Copied record";
  - toast "Copied Sum" / "Copied Average" / "Copied Min" / "Copied Max" / "Copied Count";
  - hints "Select a cell to see its row." and "Select a cell to profile its column.";
  - coverage "Based on all {n} rows" when not truncated;
  - truncated note "Based on the first {n} rows. The query has more.";
  - placeholder "Calculate for all rows · coming later";
  - row label "ROW {i} OF {n}" (n = displayed rows).
- **Profile scope** is all loaded rows (`result.rows`), ignoring search and sort.

## Review Focus

1. **Cursor moves must not re-render the whole grid.** After an arrow press only the two affected `GridRow`s, the panel and the footer re-render. Check with a render counter or the React Profiler in acceptance. A store snapshot built fresh on every `get()` would loop forever (React #185).
2. **Selection cleared by search, sort or an empty search.** The panel shows the hint, never a stale row. The footer shows no totals once the grid unmounts.
3. **Number columns holding numeric strings** (PostgreSQL numeric text such as `"12.50"`) count as numbers via `toNumber`. Non-numeric text (`"$1,234"`) counts as unreadable and never as NaN in Sum or Average.
4. **Esc ordering in full screen.**
   - Esc in the panel closes only the panel.
   - Esc in the grid with a selection clears the selection.
   - The next Esc exits full screen.
5. **A 100,000-row result:** profiles and Ctrl+A totals must not freeze the tab. Compute the profile for the active column only, memoised per (column, rows) pair. Totals are one pass.

---

### Task 1: Column profile maths

**Files:**
- Create: `client/src/features/builder/results/column-profile.ts`
- Test: `client/tests/column-profile.test.ts`

**Interfaces:**
- Consumes:
  - `toNumber(value): number | null | 'bad'` from `../../visualizations/chart-data.ts`;
  - types `QueryResultColumnDto`, `ColumnDataType` from `@/types`.
- Produces:

```ts
export type ProfileKind = 'number' | 'date' | 'text' | 'boolean'
export function profileKind(dataType: ColumnDataType): ProfileKind // number→number; date,dateTime→date; boolean→boolean; text,guid,unknown→text
export interface Bin { from: number; to: number; count: number }
export function histogram(values: number[], binCount?: number): Bin[] // default 20; [] for no values; one bin {from:v,to:v,count:n} when min===max
interface ProfileBase { rows: number; empty: number; distinct: number; unreadable: number }
export interface NumberProfile extends ProfileBase { kind: 'number'; min: number | null; max: number | null; mean: number | null; median: number | null; sum: number | null; showSum: boolean; bins: Bin[] }
export interface DateProfile extends ProfileBase { kind: 'date'; earliest: number | null; latest: number | null; bins: Bin[] } // epoch ms
export interface TextProfile extends ProfileBase { kind: 'text'; shortest: number | null; longest: number | null; top: { value: string; count: number }[]; other: number }
export interface BooleanProfile extends ProfileBase { kind: 'boolean'; yes: number; no: number }
export type ColumnProfile = NumberProfile | DateProfile | TextProfile | BooleanProfile
export function profileColumn(rows: Record<string, unknown>[], column: QueryResultColumnDto, binCount?: number): ColumnProfile
```

**Rules:**

- **Empty:** `null`/`undefined` for every kind, plus `''` for text.
- **Numbers:** `toNumber(v)` gives a number → numeric; `'bad'` → unreadable.
- **Dates:**
  - a `Date` or a string where `Date.parse` is not NaN → readable;
  - anything else non-empty → unreadable;
  - distinct is keyed by the epoch ms.
- **Boolean:** `true`/`false` count yes/no; any other non-empty value is unreadable.
- **Distinct** counts readable non-empty values only. Numbers are keyed by numeric value and text by `String(v)`.
- **`showSum`** is false when `column.aggregate` is `avg`, `min`, `max` or `countDistinct`; then `sum` is `null`.
- **Median:** sort a copy of the numbers; with an even count, take the mean of the middle two.
- **Text top values** are sorted by count desc, then value asc (`localeCompare`), and cut to 10. `other` = non-empty readable count − sum of the top counts.
- **Histogram:**
  - bin width = (max − min) / binCount;
  - the last bin includes max;
  - bins carry `from`/`to` bounds.

- [ ] **Step 1: Write failing tests** in `client/tests/column-profile.test.ts`, covering at least:
  - `profileKind`: `'dateTime'` → `'date'`; `'guid'` → `'text'`; `'unknown'` → `'text'`.
  - `histogram([], 20)` → `[]`.
  - `histogram([5,5,5])` → `[{from:5,to:5,count:3}]`.
  - `histogram([0,10], 2)` → `[{from:0,to:5,count:1},{from:5,to:10,count:1}]`.
  - Number column, values `[1, 2, null, '3.5', '$4', 10]`, gives:
    - `rows: 6, empty: 1, unreadable: 1, distinct: 4`;
    - `min: 1, max: 10, sum: 16.5, mean: 4.125, median: 2.75`.
  - Same column with `aggregate: 'avg'` → `showSum: false`, `sum: null`.
  - Number column, all null → `min/max/mean/median/sum` null, `bins: []`.
  - Date column `['2025-01-01', '2025-03-01', 'not a date', null]` → earliest = `Date.parse('2025-01-01')`, latest = `Date.parse('2025-03-01')`, `unreadable: 1`, `empty: 1`.
  - Text column `['b','a','a','',null,'ccc','b']` → `empty: 2`, `distinct: 3`, `top: [{value:'a',count:2},{value:'b',count:2},{value:'ccc',count:1}]`, `other: 0`, `shortest: 1`, `longest: 3`.
  - Text column with 12 distinct values `'v01'..'v12'` (one each) → `top.length === 10`, `other === 2`.
  - Boolean column `[true, false, true, null, 'x']` → `yes: 2, no: 1, empty: 1, unreadable: 1, distinct: 2`.
- [ ] **Step 2:** Run `cd client && npm test`. Expected: the new tests FAIL with a module-not-found error.
- [ ] **Step 3:** Implement `column-profile.ts` to the interfaces and rules above.
- [ ] **Step 4:** Run `cd client && npm test`. Expected: all pass.
- [ ] **Step 5:** Commit with `git add client/src/features/builder/results/column-profile.ts client/tests/column-profile.test.ts` and `git commit -m "Results grid: column profile maths"`.

### Task 2: Selection totals and record text

**Files:**
- Create: `client/src/features/builder/results/selection-totals.ts`
- Modify: `client/src/features/builder/results/copy-selection.ts` (add `buildRecordText`)
- Test: `client/tests/selection-totals.test.ts`, `client/tests/copy-selection.test.ts` (add cases)

**Interfaces:**
- Consumes:
  - `toNumber` (chart-data.ts);
  - `normalize`'s rectangle shape `{top,bottom,left,right}` from `selection.ts`;
  - `cellToClipboardText`, `toTsv` from `clipboard-tsv.ts`;
  - `QueryResultColumnDto`.
- Produces:

```ts
export const MAX_TOTAL_CELLS = 1_000_000
export interface SelectionTotals { cells: number; count: number; numeric: { sum: number; avg: number; min: number; max: number; decimals: number } | null; skipped: boolean }
export function selectionTotals(rows: Record<string, unknown>[], columns: QueryResultColumnDto[], rect: { top: number; bottom: number; left: number; right: number }): SelectionTotals
export function formatTotal(n: number, decimals: number): string // toLocaleString(undefined, { maximumFractionDigits: decimals })
export function decimalsOf(n: number): number // digits after '.', from String(n); 0 for integers; exponent forms count as 0
// in copy-selection.ts:
export function buildRecordText(row: Record<string, unknown>, columns: QueryResultColumnDto[]): string // header Column\tValue, one line per column, values via cellToClipboardText, joined via toTsv
```

**Rules:**

- `cells` is the rectangle size.
- When `cells > MAX_TOTAL_CELLS`:
  - return `skipped: true` and `numeric: null`;
  - still compute `count`, the non-empty cells. `null`, `undefined` and `''` are empty. Counting has no parsing cost.
- Numeric aggregates use only cells in columns where `profileKind(dataType) === 'number'` (import from `column-profile.ts`) and where `toNumber` gives a number.
- `decimals` = max `decimalsOf` over the numeric inputs. Average is displayed with `Math.min(4, Math.max(decimals, 2))` (my ruling: an average of integers still shows up to 2 decimals).

- [ ] **Step 1: Write failing tests.**
  - Rows `[{a:1,b:'x'},{a:'2.5',b:null},{a:null,b:'y'}]`, columns a (number) and b (text), rect over all of them → `cells: 6, count: 4, numeric: {sum: 3.5, avg: 1.75, min: 1, max: 2.5, decimals: 1}, skipped: false`.
  - Text-only rect (column b) → `numeric: null`, `count: 2`.
  - All-empty → `count: 0`, `numeric: null`.
  - The large-range skip: build a rect with `bottom` such that cells > 1,000,000 over a small rows array, using a sparse rows array of length 1,000,001 filled with `{a:1}` → `skipped: true`, `numeric: null`.
  - `formatTotal(1234.5, 1)` → `(1234.5).toLocaleString(undefined,{maximumFractionDigits:1})`.
  - `decimalsOf(2.25) === 2`; `decimalsOf(3) === 0`.
  - In `copy-selection.test.ts`: `buildRecordText({Name:'A\tB', Fee: 12.5, Active: true, Note: null}, cols)` equals the `toTsv(['Column','Value'], [['Name','A\tB'],['Fee','12.5'],['Active','TRUE'],['Note','']])` output.
- [ ] **Step 2:** Run `npm test`. Expected: FAIL (missing exports).
- [ ] **Step 3:** Implement `selection-totals.ts` and `buildRecordText`.
- [ ] **Step 4:** Run `npm test`. Expected: all pass.
- [ ] **Step 5:** Commit: "Results grid: selection totals and record copy text".

### Task 3: Selection store and grid handle

**Files:**
- Create: `client/src/features/builder/results/selection-store.ts`
- Modify:
  - `client/src/features/builder/results/useGridSelection.ts`;
  - `client/src/features/builder/results/ResultsGrid.tsx`;
  - `client/src/features/builder/ResultsTable.tsx`;
  - `client/src/features/builder/results/ResultsFooter.tsx`.
- Test: `client/tests/selection-store.test.ts`

**Interfaces:**
- Produces:

```ts
// selection-store.ts
export interface GridSnapshot { range: CellRange | null; rows: Record<string, unknown>[]; columns: QueryResultColumnDto[] }
export interface SelectionStore { get(): GridSnapshot; publish(next: Partial<GridSnapshot>): void; subscribe(listener: () => void): () => void }
export function createSelectionStore(): SelectionStore
// ResultsGrid prop replacing onSelectionChange + copyRef:
export interface GridHandle { copy(): void; moveTo(pos: CellPos): void; focus(): void }
// ResultsGrid props: store?: SelectionStore; gridRef?: React.Ref<GridHandle>
```

**Rules:**

- **`publish`:**
  - It replaces the snapshot object only when some given field differs by identity (`!==`) from the current one, and notifies listeners only then.
  - For `range`, compare by value: anchor and focus row/col. An unchanged range must not notify.
  - `get()` returns the same object until the next real change.
- **Publishing from `ResultsGrid`:**
  - `rows` is published as a `useMemo(() => rows.map(r => r.original), [rows])` array.
  - `columns` is the grid's visible ordered `columns`.
  - Publish in a `useEffect` whenever these change.
  - Publish the clamped `range` from `useGridSelection` in a `useEffect`.
  - On unmount, publish `{ range: null }`. This replaces the "report 0 on unmount" logic.
- **Remove `onSelectionChange` and the `selectedCells` state** from `ResultsTable`, `ResultsGrid` and `useGridSelection`.
  - `ResultsFooter` takes `store` and derives the selected-cell count from `useSyncExternalStore(store.subscribe, store.get)` (`rangeSize`).
  - When `noRows`, `ResultsTable` must still show 0, because the grid is unmounted and published `null`.
- **`moveTo(pos)`:**
  - add `moveTo` to `useGridSelection`'s return value;
  - it clamps pos to bounds, sets the range to `{anchor: pos, focus: pos}` and calls `reveal(pos)`;
  - no focus change.
- **`focus()`** focuses the `[role="grid"]` element with `preventScroll`.
- **`copyRef` becomes `gridRef`**, and the toolbar's Copy uses `gridRef.current?.copy()`.
- **`ResultsTable`** creates `const [store] = useState(createSelectionStore)` beside `memory`, so it survives full screen.

- [ ] **Step 1: Write failing tests** in `selection-store.test.ts`:
  - `get()` is stable across calls;
  - `publish({range: sameValueNewObject})` does not notify and keeps the same `get()` object;
  - `publish({range: different})` notifies once and changes `get()`;
  - `publish({rows: sameArray})` does not notify;
  - unsubscribe stops notifications.
- [ ] **Step 2:** Run `npm test`. Expected: FAIL.
- [ ] **Step 3:** Implement `selection-store.ts`, then wire up the grid, table and footer as described.
- [ ] **Step 4:** Run `npm test` and `npx tsc -b --noEmit` (or the project's `npm run build` type step). Expected: tests pass and there are no type errors.
- [ ] **Step 5: Manual check in the report.** Describe how the "8 cells selected" footer still appears and clears on search-to-empty. The controller verifies this in the browser.
- [ ] **Step 6:** Commit: "Results grid: selection store and grid handle".

### Task 4: Footer selection totals

**Files:**
- Create: `client/src/features/builder/results/SelectionTotals.tsx`
- Modify: `client/src/features/builder/results/ResultsFooter.tsx`

**Interfaces:**
- Consumes:
  - `SelectionStore` (Task 3);
  - `selectionTotals`, `formatTotal`, `MAX_TOTAL_CELLS` (Task 2);
  - `normalize`, `rangeSize` (`selection.ts`);
  - `copyText`;
  - `toast` from sonner.
- Produces: `export function SelectionTotals({ store }: { store: SelectionStore }): JSX.Element | null`

**Rules:**

- Render nothing when the selection is fewer than 2 cells.
- Otherwise, right-aligned in the footer (`ml-auto`), show Barlow items in the order Count, Sum, Average, Min, Max. Each is a small text button: label muted, value in `text-foreground`.
- Sum through Max appear only when `numeric` is non-null.
- When `skipped`, show Count only, with `title="Totals skipped for very large selections"`.
- **Click** → `copyText(String(rawNumber))`, then toast `Copied <Label>`. On failure: toast.error "Copy failed.".
- **Values:**
  - Sum, Min and Max use `formatTotal(v, decimals)`.
  - Average uses `formatTotal(avg, Math.min(4, Math.max(decimals, 2)))`.
  - Count uses `toLocaleString()`.
- Memoise `selectionTotals` on `(snapshot.rows, snapshot.columns, range)`.
- Buttons have `aria-label` like "Sum 4,210, copy".
- The existing "N cells selected" text stays before the totals.

- [ ] **Step 1:** Implement `SelectionTotals.tsx` and render it inside `ResultsFooter`.
- [ ] **Step 2:** Run `npm test` and the type check. Expected: pass.
- [ ] **Step 3:** Commit: "Results grid: selection totals in the footer".

### Task 5: Details panel shell

**Files:**
- Create:
  - `client/src/features/builder/results/panel-layout.ts`;
  - `client/src/features/builder/results/DetailsPanel.tsx`;
  - `client/src/features/builder/results/type-badge.ts` (move the badge map out of `GridHeaderCell.tsx` and import it back there).
- Modify:
  - `client/src/features/builder/ResultsTable.tsx`;
  - `client/src/features/builder/results/ResultsToolbar.tsx`.
- Test: `client/tests/panel-layout.test.ts`

**Interfaces:**
- Produces:

```ts
// panel-layout.ts
export const PANEL_DEFAULT = 340, PANEL_MIN = 280, PANEL_MAX = 560, PANEL_STEP = 16, OVERLAY_BELOW = 900
export type PanelTab = 'row' | 'column'
export interface PanelState { open: boolean; tab: PanelTab; width: number }
export const initialPanel: PanelState // { open: false, tab: 'row', width: 340 }
export function clampPanelWidth(width: number, areaWidth: number): number // max(PANEL_MIN, min(width, PANEL_MAX, floor(areaWidth / 2))); PANEL_MIN wins if area is tiny
export function panelMode(areaWidth: number): 'dock' | 'overlay' // < 900 → overlay
// type-badge.ts
export const TYPE_BADGE: Record<ColumnDataType, string>
// DetailsPanel.tsx
export function DetailsPanel(props: { state: PanelState; onChange: (s: PanelState) => void; onClose: () => void; mode: 'dock' | 'overlay'; areaWidth: number; row: React.ReactNode; column: React.ReactNode }): JSX.Element
```

**Rules:**

- **Toolbar:** a "Details" toggle after Wrap text, with the lucide `PanelRight` icon, `aria-pressed={open}` and the same button style as Wrap. New props: `detailsOpen: boolean` and `onToggleDetails: () => void`.
- **`ResultsTable` layout:**
  - `const [panel, setPanel] = useState(initialPanel)`.
  - Under the toolbar and banner, the band becomes `<div className="relative flex min-h-0 flex-1">`, holding the grid wrapper (`min-w-0 flex-1`) and, when open, the panel.
  - Measure the band's width with a `ResizeObserver` to get `areaWidth`.
  - **dock** → the panel is a flex sibling with `border-l`.
  - **overlay** → the panel is `absolute inset-y-0 right-0 z-10 border-l bg-background`, with no shadow.
  - The panel also shows when `noRows` (its hints appear).
  - The footer stays full width.
- **Panel structure:** `<section aria-label="Details">`. Its header row holds the Radix Tabs list ("Row", "Column", bound to `state.tab`) and a ✕ icon button "Close details". The body is the active tab's content and scrolls (`overflow-y-auto`).
- **Resize handle:**
  - a 6px handle on the left edge, `role="separator"`, `aria-orientation="vertical"`, `aria-valuenow` = width, `aria-valuemin`/`aria-valuemax`, `aria-label="Resize details panel"`, focusable;
  - pointer drag changes the width;
  - ←/→ change it by `PANEL_STEP` (← widens, because the handle is on the left);
  - always pass through `clampPanelWidth`.
- **Esc** inside the panel (on the section's `onKeyDown`):
  - Skip it when the event target is inside `[role="menu"],[role="listbox"]`.
  - Otherwise call `e.preventDefault()` and `onClose()`.
  - Then focus returns: `gridRef.current?.focus()` if a range exists in the store, else the toolbar Details button (pass a ref, as for full screen).
  - The full-screen document listener already ignores `defaultPrevented`.
- **Lifetime:** panel state lives in `ResultsTable`, so it survives full screen and re-runs. "Reset view" does not touch it.
- For this task, `row` and `column` are placeholders that render the hints "Select a cell to see its row." and "Select a cell to profile its column.". Tasks 6 and 7 replace them.

- [ ] **Step 1: Write failing tests** for `clampPanelWidth`:
  - `(340, 1200) → 340`; `(900, 1200) → 560`; `(600, 800) → 400`; `(100, 1200) → 280`; `(340, 400) → 280`.
  - `panelMode(899) → 'overlay'`, `panelMode(900) → 'dock'`.
- [ ] **Step 2:** Run `npm test`. Expected: FAIL.
- [ ] **Step 3:** Implement `panel-layout.ts`, `type-badge.ts`, `DetailsPanel.tsx`, and the toolbar and table wiring.
- [ ] **Step 4:** Run `npm test` and the type check. Expected: pass.
- [ ] **Step 5:** Commit: "Results grid: details panel shell".

### Task 6: Row tab

**Files:**
- Create: `client/src/features/builder/results/RowDetails.tsx`
- Modify: `client/src/features/builder/ResultsTable.tsx` (pass `row={<RowDetails …/>}`)

**Interfaces:**
- Consumes:
  - `SelectionStore` and `GridHandle` (Task 3);
  - `buildRecordText` (Task 2);
  - `TYPE_BADGE` (Task 5);
  - `displayString` (`grid-columns.ts`);
  - `profileKind` (Task 1);
  - `copyText`, `toast`.
- Produces: `export function RowDetails(props: { store: SelectionStore; gridRef: React.RefObject<GridHandle | null>; onRowClick?: (row: Record<string, unknown>) => void; rowActionLabel?: string }): JSX.Element`

**Rules:**

- Read the snapshot with `useSyncExternalStore`.
- **No range** → the hint "Select a cell to see its row.".
- **Header:**
  - the label `ROW {focus.row+1} OF {rows.length}` in Barlow, using `toLocaleString` for both numbers;
  - Previous and Next icon buttons (`ChevronUp`/`ChevronDown`, `aria-label` "Previous row"/"Next row"), which call `gridRef.current?.moveTo({row: focus.row ∓ 1, col: focus.col})` and are disabled at the ends;
  - a "Copy record" button, which runs `copyText(buildRecordText(rows[focus.row], columns))`, then toast "Copied record" (on failure, toast.error "Copy failed.");
  - when `onRowClick` exists, a button labelled `rowActionLabel ?? 'See these rows'` that calls `onRowClick(rows[focus.row])`.
- **Body:** a `<dl>`, with one entry per column in `columns` order.
  - `<dt>` is the name plus the type badge.
  - `<dd>` is the value.
  - Number kind → right-aligned `font-numeric tabular-nums`.
  - Text → `whitespace-pre-wrap break-words`, with no clamp.
  - null/undefined/'' → a muted italic "empty".
  - Values otherwise use `displayString`. Booleans use the grid's representation (check `GridCell.tsx` and match it).
  - The entry at `focus.col` gets the selection tint (`bg-primary/10`) and `aria-current="true"`.

- [ ] **Step 1:** Implement `RowDetails.tsx` and wire it in.
- [ ] **Step 2:** Run `npm test` and the type check. Expected: pass.
- [ ] **Step 3:** Commit: "Results grid: row details tab".

### Task 7: Column tab

**Files:**
- Create:
  - `client/src/features/builder/results/ColumnProfileView.tsx`;
  - `client/src/features/builder/results/Histogram.tsx`.
- Modify: `client/src/features/builder/ResultsTable.tsx` (pass `column={<ColumnProfileView …/>}`)

**Interfaces:**
- Consumes:
  - `profileColumn`, `ColumnProfile`, `Bin` (Task 1);
  - `formatTotal` (Task 2);
  - `TYPE_BADGE`;
  - `SelectionStore`.
- Produces:

```ts
export function ColumnProfileView(props: { store: SelectionStore; result: QueryResultDto }): JSX.Element
export function Histogram(props: { bins: Bin[]; label: string; formatBound: (n: number) => string }): JSX.Element
```

**Rules:**

- **Active column** = `snapshot.columns[range.focus.col]`. With no range, show the hint "Select a cell to profile its column.".
- **Profile** = `useMemo(() => profileColumn(result.rows, column), [result.rows, column])`. It uses all loaded rows, not the snapshot rows.
- **Header:** the column name, the type badge, and the coverage line.
  - When `result.truncated`, show the yellow note "Based on the first {result.rows.length} rows. The query has more." with the `bg-warning` swatch, as in the phase 1 banner. Below it, a disabled dashed-border button "Calculate for all rows · coming later".
  - Otherwise, muted text "Based on all {n} rows".
- **Every kind:**
  - an Empty row: count, % of rows, and a meter (a plain div bar, `bg-muted` track, `bg-primary/60` fill);
  - Distinct;
  - when `unreadable > 0`, an "Unreadable" count line.
- **Number:**
  - stat tiles in a 3-column grid: Min, Average, Max, then Sum (only if `showSum`) and Median;
  - Barlow values via `formatTotal` with up to 4 decimals;
  - then the Histogram, with bounds formatted by `formatTotal`.
- **Date:**
  - tiles Earliest and Latest, formatted with `new Date(ms).toISOString().slice(0,10)` for date and `toISOString().replace('T',' ').slice(0,16)` for dateTime;
  - the Histogram, with bounds formatted the same way.
- **Text:**
  - tiles Distinct, Shortest, Longest (lengths in characters);
  - then the top-values list: value (truncated, with `title`), a bar (width = count / rows), and the count;
  - then "Other values: {other}" when `other > 0`.
- **Boolean:** three bars, Yes / No / Empty, with counts.
- **`Histogram`:**
  - a flex row of div bars, 64px tall, with heights proportional to `count/maxCount` and `bg-primary/60`;
  - each bar has `title="{from}–{to}: {count} rows"`;
  - the wrapper is `role="img"` with `aria-label` = `label`, e.g. `Histogram of FeePaid, 20 bins, peak 12.40–24.80 (93 rows)`;
  - no animation.

- [ ] **Step 1:** Implement `Histogram.tsx` and `ColumnProfileView.tsx` and wire them in.
- [ ] **Step 2:** Run `npm test` and the type check. Expected: pass.
- [ ] **Step 3:** Commit: "Results grid: column profile tab".

### Task 8: Docs, tracker, bundle

**Files:**
- Modify:
  - `docs/superpowers/specs/2026-11-25-results-panel-features.md`: items 6, 7 and 8 become "Done (unreleased, branch feature/results-panels)".
  - `README.md` and `src/QueryBuilder.Editor/README.md`, section "Working with query results": add the Details panel (Row/Column, follows the cell, loaded-rows note) and footer totals (click to copy).
  - `src/QueryBuilder.Editor/wwwroot/` (rebuilt bundle).

- [ ] **Step 1:** Edit the docs.
- [ ] **Step 2:** Run `cd client && npm test && npm run build`. Expected: all pass, and the build succeeds.
- [ ] **Step 3:** Run `rm -rf src/QueryBuilder.Editor/wwwroot/assets && cp -r client/dist/assets src/QueryBuilder.Editor/wwwroot/assets && cp client/dist/index.html src/QueryBuilder.Editor/wwwroot/index.html`.
- [ ] **Step 4:** Commit the docs and `wwwroot` changes (check `git status`; never `git add -A`): "Results grid: ship details panel bundle and docs".

### Acceptance (controller, Opus, after the final review)

Recreate the temporary views from the phase 1 scratchpad SQL (`vw_GridAcceptance`, `vw_GridWide`) and add them to the allowlist. Run the sample app at `:5080` and check, with Playwright:

- **Following the cell:** the panel follows the cell on clicks and arrow presses.
- **Render cost:** a render counter shows no full-grid re-render per arrow press.
- **Row tab:** Prev/Next work at the ends.
- **Copy on http:** Copy record and copying a total both work on `http://qb.test`.
- **Esc:** Esc ordering is correct in full screen.
- **Docked/overlay:** the panel docks at 1280px and overlays at 800px.
- **Resizing:** keyboard resize works.
- **Coverage note:** truncated vs not truncated.
- **Profiles:** number, date, text and boolean.
- **Footer totals:** correct values.
- **Looks:** both themes and both densities.
- **Scroll speed:** 64-column scroll timing with the panel open is about the same as phase 1 (~26 ms/frame).

Afterwards, drop the views, restore the allowlist and delete the temporary saved queries.
