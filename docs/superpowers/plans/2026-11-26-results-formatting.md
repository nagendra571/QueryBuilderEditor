# Results grid phase 4: formats, highlights and a totals row — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per-column number/date formats, in-cell bars, colour scales, highlight rules and an optional totals row in the
Results grid, set from a new Format tab in the Details panel, held for the session only.

**Architecture:** Two new pure modules: `grid-format.ts` (formatting, rules, bars/scale maths, compiled per column and
cached like phase 3's filter matchers) and `grid-totals.ts`. Settings live in `GridView` (`formats`, `totals`) so phase 1's
carry-over, full screen and Reset view apply. UI: `GridCell` renders compiled text/decoration; a `FormatPanel` tab in
`DetailsPanel`; a sticky `TotalsRow` inside `ResultsGrid`'s scroll area.

**Tech Stack:** React 19, TypeScript, Tailwind 4, shadcn + `radix-ui`, `@tanstack/react-table` 8.21.3,
`@tanstack/react-virtual` 3.14.28, Node's built-in test runner.

**Spec:** `docs/superpowers/specs/2026-11-26-results-formatting-design.md` (mockup
`docs/superpowers/specs/assets/2026-11-26-results-formatting-mockup.html`).

## Global Constraints

- Client-only: no API, schema, package-version or dependency change.
- Pure logic in `client/src/features/builder/results/*.ts`; tests in `client/tests/*.test.ts`, run with `cd client && npm test`
  (`node --test "tests/**/*.test.ts"`). Pure files use `import type` for `@/` imports and relative `.ts` runtime imports.
- No `navigator.clipboard` / `crypto.*` anywhere in `client/src` (comments included; `tests/insecure-context.test.ts`).
- Zustand / `useSyncExternalStore` selectors never return newly built objects.
- Design: no shadows at rest; yellow (`bg-warning/20 text-foreground`) only for the cut-off note; `font-numeric` (Barlow)
  for numbers in the totals row and rule values; primary blue for focus/selection; selection styling stays on top of all
  formatting.
- Copy, sort, filters, search and Add to query keep using **raw** values. A cell whose formatted text differs from the raw
  text has `title` = raw text (plus the matching rule's label, see Task 1).
- Zoneless date text = database wall-clock → format with `timeZone: 'UTC'`. Text with `Z` or an offset = an instant → format
  in the viewer's zone. Parsing via the existing `parseDateValue` (`column-profile.ts`).
- Locale: `Intl` with `undefined` locale in product code; formatting functions take an optional `locale` parameter so tests
  pin `'en-GB'` / `'en-US'`.
- Percent: 42 means 42% (like charts' `format.ts`); `fraction: true` means 0.42 is 42%.
- Highlight rules: ≤ 5 per column; first match wins; colours exactly `red | amber | green | blue`.
- Cut-off note under the totals row, exact text: `Totals cover the first {N} loaded rows. Export and charts use all matching rows.`
  (N = `result.rowLimit.toLocaleString()`), shown only when `result.truncated`.
- Session-only: nothing persisted beyond `GridView`. `carryOver` keeps the whole view on the same column set and resets on
  a different set (unchanged rule). Reset view clears formats and totals.
- Phase 1–3 behaviour must not regress: keyboard navigation, Esc order (menus/popovers → panel → full screen), full screen,
  re-run carry-over, render isolation (store), http copy, filters, chips, Add to query.

## Review Focus

1. Number column that holds text cells (PostgreSQL `money`/huge `numeric`) → raw text shown, no bar/scale/rule, no NaN, totals
   skip it. Pinned in Task 1 (`text in a number column`) and Task 2 (`totals skip non-numbers`).
2. Zoneless `2026-11-03T00:00:00` under `TZ=America/New_York` must show 3 Nov, not 2 Nov; zoned `2026-11-03T23:30:00Z` shows
   the viewer's day. Pinned in Task 1 (run under both TZs).
3. A column whose every value is equal (or a single row) → scale and bars don't divide by zero (bar full, scale midpoint).
   Pinned in Task 1 (`flat column`).
4. Filters hide every row → totals row shows blank values / Count 0, no crash, note still correct. Pinned in Task 2
   (`empty rows`).
5. Float noise: Sum of 0.1 + 0.2 shows `0.3`, not `0.30000000000000004`. Pinned in Task 2 (`rounds like selection totals`).

---

### Task 1: `grid-format.ts` — formats, rules, bars and scales (pure)

**Files:**
- Create: `client/src/features/builder/results/grid-format.ts`
- Test: `client/tests/grid-format.test.ts`

**Interfaces:**
- Consumes: `parseDateValue(v: unknown): number | null` from `./column-profile.ts`; `ColumnDataType` from `@/types` (type only).
- Produces:
  ```ts
  export type NumberStyle = 'auto' | 'number' | 'compact' | 'currency' | 'percent'
  export interface NumberFormat { style: NumberStyle; decimals: number | null; currency: string; thousands: boolean; fraction: boolean }
  export type DatePattern = 'auto' | 'weekday' | 'medium' | 'short' | 'iso' | 'monthYear' | 'year'
  export interface DateFormat { pattern: DatePattern; showTime: boolean }
  export type RuleColor = 'red' | 'amber' | 'green' | 'blue'
  export type RuleOp = 'gt' | 'lt' | 'between' | 'eq' | 'is' | 'contains' | 'empty' | 'before' | 'after'
  export interface HighlightRule { id: string; op: RuleOp; value: string; value2: string; color: RuleColor }
  export type ScaleKind = 'blue' | 'redGreen'
  export interface ColumnFormat { number?: NumberFormat; date?: DateFormat; bars?: boolean; scale?: ScaleKind | null; rules: HighlightRule[] }
  export const MAX_RULES = 5
  export const defaultNumberFormat: NumberFormat   // { style:'auto', decimals:null, currency:'USD', thousands:true, fraction:false }
  export const defaultDateFormat: DateFormat       // { pattern:'auto', showTime:false }
  export function rulesFor(dataType: ColumnDataType): RuleOp[]
  export function formatNumber(n: number, f: NumberFormat, locale?: string): string
  export function formatDate(raw: unknown, dataType: 'date' | 'dateTime', f: DateFormat, locale?: string): string | null // null = unreadable
  export function isComplete(rule: HighlightRule): boolean
  export function ruleLabel(column: string, rule: HighlightRule, fmt: (v: string) => string): string  // e.g. "Amount > 15,000"
  export interface CellLook { text: string; title: string | null; bar: { from: number; to: number; negative: boolean } | null; scale: { kind: ScaleKind; t: number } | null; highlight: RuleColor | null }
  export interface CompiledFormat { formatted: boolean; look(value: unknown): CellLook; text(value: unknown): string }
  export function compileFormat(rows: readonly Record<string, unknown>[], column: { name: string; dataType: ColumnDataType }, f: ColumnFormat | undefined, locale?: string): CompiledFormat
  export function isFormatted(f: ColumnFormat | undefined): boolean
  ```
  `bar.from`/`bar.to` are fractions 0–1 of the cell width; `scale.t` is 0–1 (0.5 = midpoint). `text()` is the
  formatted display text (or `displayString(value)` when nothing applies / value unusable).

- [ ] **Step 1: Write the failing tests** in `client/tests/grid-format.test.ts` (all with `locale` `'en-GB'` unless named):
  - `formatNumber`: `auto` equals `(1234.5).toLocaleString('en-GB')`; `number` decimals 2 → `'1,234.50'`; thousands off →
    `'1234.50'`; `compact` 12400 → `'12.4K'`; `currency` USD decimals 2 → `'US$12,400.00'` under en-GB (assert it equals
    `Intl.NumberFormat('en-GB',{style:'currency',currency:'USD',minimumFractionDigits:2,maximumFractionDigits:2}).format(12400)`);
    bad currency `'US'` → same as `number`; `percent` 42 → `'42%'`; `percent` with `fraction` 0.42 → `'42%'`; negatives keep
    the sign; for each style the output equals `formatValue` in `visualizations/format.ts` with the same style/decimals when
    `thousands` is true and the locale is the default (run with default locale for this one assertion).
  - `formatDate` with `'en-GB'`: zoneless `'2026-11-03T00:00:00'` as `dateTime` → `medium` `'3 Nov 2026'`, `weekday`
    `'Tue 3 Nov 2026'` (assert equals the `Intl` output for `{weekday:'short',day:'numeric',month:'short',year:'numeric',timeZone:'UTC'}`),
    `short` `'03/11/2026'`, `iso` `'2026-11-03'`, `monthYear` `'Nov 2026'`, `year` `'2026'`; `showTime` on
    `'2026-11-03T14:05:00'` → contains `'14:05'`; zoned `'2026-11-03T23:30:00Z'` → equals the `Intl` output **without**
    `timeZone` (viewer's zone); `auto` returns the raw text unchanged; `'not a date'` → `null`; `'en-US'` `medium` → `'Nov 3, 2026'`.
  - **Review Focus 2:** the zoneless assertions pass under `TZ=America/New_York` and `TZ=Asia/Kolkata`.
  - Rules: `gt 15000` matches 15001 not 15000; `between 10,20` inclusive both ends, reversed bounds still work; `eq`;
    `is` exact text; `contains` case-insensitive; `empty` matches `null`, `undefined`, `''`, `'  '`; `before`/`after`/`between`
    compare the **displayed calendar day** (`'2026-11-03T23:30:00'` zoneless is 3 Nov); incomplete rule (empty value)
    never matches and `isComplete` is false; first matching rule wins (two rules, both match → first colour).
  - `rulesFor`: number → `['gt','lt','between','eq']`; text/guid/unknown → `['is','contains','empty']`; date/dateTime →
    `['before','after','between']`; boolean → `[]`.
  - Bars: values `[0, 50, 100]` → 100 gives `{from:0,to:1,negative:false}`, 50 gives `to:0.5`; with negatives
    `[-50, 0, 150]` → zero at 0.25, -50 gives `{from:0,to:0.25,negative:true}`, 150 gives `{from:0.25,to:1,negative:false}`.
  - Scale: `blue` min→0, max→1; `redGreen` median → 0.5, min → 0, max → 1.
  - **Review Focus 3** `flat column`: all values 7 → bar `to:1`, scale `t:0.5`, no NaN anywhere.
  - **Review Focus 1** `text in a number column`: value `'123456789012345678901234567890.5'` (string) in a `number` column with
    bars, scale and a `gt 0` rule → `look()` returns raw text, `bar`, `scale`, `highlight` all null; and it is excluded from
    the min/max used for other cells.
  - `look().title`: raw text when it differs from `text`, with `' · '` + `ruleLabel` appended when a rule matched; `null`
    when text equals raw and no rule matched.
  - `compileFormat` returns the same object for the same `(rows, column name, f)` triple (WeakMap on `rows`, then key).
  - `isFormatted`: undefined → false; `{rules:[]}` → false; any non-auto format, bars, scale or a rule → true.

- [ ] **Step 2: Run** `cd client && node --test tests/grid-format.test.ts` — expected: FAIL (module not found).

- [ ] **Step 3: Implement** `grid-format.ts`. Notes: zoneless detection must match `parseDateValue`'s (`HAS_TIME` without
  `HAS_ZONE` → UTC; date-only strings → UTC); bars/scale statistics are computed once per compile over numeric values of
  `rows[column]` only; rule values parse with `Number(value.trim())` (empty → incomplete) and dates with `parseDateValue`.
  Day comparison for date rules uses the same zone as display. `ruleLabel` operators: `>`, `<`, `between … and …`, `=`,
  `is`, `contains`, `is empty`, `before`, `after`.

- [ ] **Step 4: Run** `cd client && node --test tests/grid-format.test.ts`, then with `TZ=America/New_York` and
  `TZ=Asia/Kolkata` — expected: PASS in all three.

- [ ] **Step 5: Commit** `git add client/src/features/builder/results/grid-format.ts client/tests/grid-format.test.ts` —
  message "Results grid: cell formats, highlight rules, bars and scales (pure)".

### Task 2: `grid-totals.ts`, `GridView` fields and aggregate stamping (pure)

**Files:**
- Create: `client/src/features/builder/results/grid-totals.ts`
- Modify: `client/src/features/builder/results/grid-view.ts` (add `formats`, `totals`; `defaultView`, `carryOver`, `isDefaultView`)
- Test: `client/tests/grid-totals.test.ts`, `client/tests/grid-view.test.ts`

**Interfaces:**
- Consumes: Task 1 `ColumnFormat`, `compileFormat`/`formatNumber` for value text; `decimalsOf`, `formatTotal` from
  `./selection-totals.ts` for rounding; `parseDateValue`.
- Produces:
  ```ts
  // grid-view.ts
  export interface GridTotals { on: boolean; fns: Record<string, TotalFn> }
  // GridView gains: formats: Record<string, ColumnFormat>; totals: GridTotals   (defaults {} and {on:false,fns:{}})
  // grid-totals.ts
  export type TotalFn = 'sum' | 'avg' | 'count' | 'distinct' | 'min' | 'max' | 'earliest' | 'latest' | 'none'
  export const TOTAL_LABEL: Record<TotalFn, string>  // SUM AVG COUNT DISTINCT MIN MAX EARLIEST LATEST ''
  export function totalChoices(column: QueryResultColumnDto): TotalFn[]
  export function defaultTotal(column: QueryResultColumnDto): TotalFn
  export function totalValue(rows: readonly Record<string, unknown>[], column: QueryResultColumnDto, fn: TotalFn, text: (v: unknown) => string): string
  export function withAggregates(columns: QueryResultColumnDto[], definition: { columns: QueryColumn[] } | undefined): QueryResultColumnDto[]
  ```
  `TotalFn` is exported from `grid-totals.ts` and imported (type) by `grid-view.ts`.

- [ ] **Step 1: Write the failing tests.**
  - `totalChoices`/`defaultTotal`: number plain → choices `['sum','avg','count','min','max','none']`, default `'sum'`;
    number with `aggregate` `avg`/`min`/`max`/`countDistinct` → choices without `'sum'`, default `'none'`; number with
    `aggregate` `sum`/`count` → sum offered, default `'sum'`; date/dateTime → `['earliest','latest','count','none']`, default
    `'latest'`; text/guid/unknown/boolean → `['count','distinct','none']`, default `'count'`.
  - `totalValue`: sum/avg/min/max over numbers; count = non-empty values (null, undefined, `''` excluded); distinct; earliest/
    latest return the `text()` of the original value with the min/max `parseDateValue`; `'none'` → `''`.
  - **Review Focus 5** `rounds like selection totals`: sum of `[0.1, 0.2]` with `text = String` gives `'0.3'`; avg of
    `[1, 2]` gives `'1.5'`. Rounding uses `decimalsOf`/`formatTotal`, then the column's `text` formats the rounded number.
  - **Review Focus 1** `totals skip non-numbers`: sum over `[10, '9999999999999999999999.5', null, 5]` → `'15'`.
  - **Review Focus 4** `empty rows`: every fn over `[]` → `''` except count/distinct → `'0'`.
  - `withAggregates`: result column `Total` maps to the visible query column whose `alias || columnName` is `Total` and
    copies its `aggregate`; unknown or duplicated names → `aggregate` unchanged; `definition` undefined → columns returned
    unchanged (same array).
  - `grid-view.test.ts`: `defaultView` has `formats: {}` and `totals: {on:false,fns:{}}`; `carryOver` with the same column
    set deep-copies both (mutating the copy's rule array doesn't touch the original); a different set resets them;
    `isDefaultView` is false when `totals.on`, or any format is present, and true for `formats: {}`.

- [ ] **Step 2: Run** `cd client && node --test tests/grid-totals.test.ts tests/grid-view.test.ts` — expected: FAIL.

- [ ] **Step 3: Implement** `grid-totals.ts` and the `grid-view.ts` changes. Keep every existing `grid-view` test passing.

- [ ] **Step 4: Run** `cd client && npm test` — expected: all pass.

- [ ] **Step 5: Commit** — message "Results grid: totals maths, view fields for formats and totals".

### Task 3: Formatted cells, header mark and Format… menu item

**Files:**
- Modify: `client/src/features/builder/results/useResultsGrid.ts` (format actions, compiled formats)
- Modify: `GridCell.tsx`, `GridRow.tsx`, `ResultsGrid.tsx` (auto-fit text at ~line 270), `useResultsGrid.ts:75` (fit sample)
- Modify: `RowDetails.tsx:119`, `ColumnProfileView.tsx` (min/max/average/top values through the column's `text`)
- Modify: `GridHeaderCell.tsx` (blue dot when `isFormatted`; menu item "Format…"), `header-width.ts` (+ dot width)
- Modify: `client/src/features/builder/ResultsTable.tsx` (pass `withAggregates(result.columns, queryDefinition)` to the grid)
- Test: `client/tests/header-width.test.ts`

**Interfaces:**
- Consumes: Task 1 `compileFormat`, `isFormatted`, `CellLook`; Task 2 `GridView.formats`, `withAggregates`.
- Produces (returned from `useResultsGrid`):
  ```ts
  formats: Record<string, ColumnFormat>
  setColumnFormat(column: string, next: ColumnFormat | null): void   // null removes the entry
  compiled(column: string): CompiledFormat                            // compiled over the grid's filtered rows (`filterRows`), memoised per (rows, formats)
  ```
  `GridHeaderCell` actions gain `format(): void` (opens the panel on the Format tab for this column — Task 4 wires it; in
  this task it may be an optional prop that is not rendered when absent).

- [ ] **Step 1: Write the failing test** in `header-width.test.ts`: a formatted header is wider than the same unformatted
  header by exactly the dot's width constant exported from `header-width.ts` (`FORMAT_MARK_WIDTH`).
- [ ] **Step 2: Run** `cd client && node --test tests/header-width.test.ts` — expected: FAIL.
- [ ] **Step 3: Implement.** `GridCell` takes `look: CellLook` instead of computing text itself (booleans and `null` keep
  today's rendering). Bar: absolutely positioned behind the text, `bg-primary/20` (negative `bg-destructive/20`), inset
  `left: from*100%`, `width: (to-from)*100%`, vertical inset 6px. Scale: background via
  `color-mix(in oklab, var(--primary) {t*45}%, transparent)` for `blue`; `redGreen` below 0.5 mixes `var(--destructive)`
  at `(0.5-t)*90`%, above mixes `var(--success)` at `(t-0.5)*90`% (cap 45%). Highlights: `red` `bg-destructive/20`,
  `amber` `bg-warning/30`, `green` `bg-success/20`, `blue` `bg-primary/20`, text stays `text-foreground`. Selected cells:
  selection class wins (`GridRow`'s `SELECTED` replaces the background; bar stays). `title` from `look.title`.
  Only compute `look` for rendered cells (virtualised), via `compiled(col.name).look(value)`.
- [ ] **Step 4: Run** `cd client && npm test && npx tsc -b` — expected: pass, no type errors.
- [ ] **Step 5: Commit** — message "Results grid: render formats, header mark, formatted details and profile".

### Task 4: Format tab in the Details panel

**Files:**
- Modify: `panel-layout.ts` (`PanelTab = 'row' | 'column' | 'format'`), `DetailsPanel.tsx` (third tab "Format", prop `format: React.ReactNode`)
- Create: `client/src/features/builder/results/FormatPanel.tsx`, `client/src/features/builder/results/RuleEditor.tsx`
- Modify: `client/src/features/builder/ResultsTable.tsx` (render `FormatPanel`; header "Format…" → `setPanel({open:true, tab:'format'})` and moves the active cell to that column, as panel filters already do)
- Test: `client/tests/panel-layout.test.ts` (tab type only if a helper changes)

**Interfaces:**
- Consumes: Task 1 types, `rulesFor`, `MAX_RULES`, `formatNumber`, `formatDate`, `defaultNumberFormat`, `defaultDateFormat`;
  Task 2 `totalChoices`, `defaultTotal`, `GridTotals`; Task 3 `setColumnFormat`, `formats`.
- Produces: `FormatPanel({ column, format, onChange, totals, onTotalChange, sample, onPrev, onNext })` where `column` is the
  active `QueryResultColumnDto` (follows the active column like `ColumnProfileView`), `sample` is the active cell's value or
  the first non-empty value, `onTotalChange(fn: TotalFn)`.

- [ ] **Step 1: Implement** the tab per spec "Format tab": header (type badge, name, ‹ › previous/next visible column);
  sections Number format (Style select; Currency code input shown for currency, 3 letters, invalid → inline
  "Use a three-letter code, like USD" and ignored; Decimals segmented Auto/0/1/2/3; Thousands separator switch; "Values
  are fractions (0.42 = 42%)" switch for percent; "Looks like **{formatted sample}**"), Date format (select whose option
  labels are the sample formatted in each pattern; "Show time" for dateTime), Bars in cells (switch), Colour scale
  (switch + two palette swatches), Highlight rules (list of `RuleEditor`, "+ Add rule" disabled at `MAX_RULES` with title
  "Up to 5 rules per column", hint "First match wins"), Total (select of `totalChoices`, only while `totals.on`, else the
  line "Turn on Σ Totals to show totals"). Each section has a Reset link; footer button "Clear formatting for this column".
  Booleans: "Nothing to format for Yes/No columns" + Total. No Apply button: every change calls `onChange` immediately.
  `RuleEditor`: condition select (`rulesFor`), value input(s) (`type="number"`-like text with `font-numeric` for numbers,
  `type="date"` for dates, none for `empty`), colour select (four swatches with names), remove ✕; "Incomplete" muted
  label when `!isComplete`; with `fraction` on, show "= {formatted}" beside the number input. Rule ids: a module counter,
  never `crypto`.
- [ ] **Step 2: Run** `cd client && npm test && npx tsc -b` — expected: pass.
- [ ] **Step 3: Check in the browser** (sample app, `dotnet run --project src/QueryBuilder.Api --no-launch-profile --urls http://localhost:5080`):
  Format tab follows the active column; header "Format…" opens it; a currency format, bars and a rule show in the grid;
  Esc from inside a select closes the select first, then the panel.
- [ ] **Step 4: Commit** — message "Results grid: Format tab in the details panel".

### Task 5: Totals row

**Files:**
- Create: `client/src/features/builder/results/TotalsRow.tsx`
- Modify: `ResultsToolbar.tsx` ("Σ Totals" toggle, `aria-pressed`), `ResultsGrid.tsx` (render `TotalsRow` sticky at the bottom of the scroll area, same column layouts/pinning as `GridRow`, horizontal scroll shared), `useResultsGrid.ts` (`setTotals(on: boolean)`, `setTotalFn(column, fn)`), `ResultsTable.tsx` (cut-off note)

**Interfaces:**
- Consumes: Task 2 `totalValue`, `defaultTotal`, `totalChoices`, `TOTAL_LABEL`; Task 3 `compiled(column).text`.
- Produces: `setTotals(on: boolean): void`, `setTotalFn(column: string, fn: TotalFn): void` on `useResultsGrid`'s return.

- [ ] **Step 1: Implement.** Totals computed over the grid's displayed rows (`rows`, search + filters, same as the footer
  count), memoised on (rows, columns, totals, formats). Row-number cell shows `Σ`. Each cell: small uppercase label
  (`TOTAL_LABEL`) then the value in `font-numeric`, right-aligned for numbers; it is a `button` with `tabIndex={-1}`,
  `aria-haspopup="menu"`, opening a DropdownMenu of `totalChoices` (current one checked). Totals cells are outside the
  selection and copy. Column virtualisation: render the same centre slice as rows. The row has a top border in
  `border-foreground`, background `bg-muted`, no shadow. Cut-off note directly below the grid when `totals.on &&
  result.truncated`, exact text from Global Constraints, `bg-warning/20 text-foreground`.
- [ ] **Step 2: Run** `cd client && npm test && npx tsc -b` — expected: pass.
- [ ] **Step 3: Check in the browser:** totals follow a filter; pinned left/right columns stay aligned while scrolling
  sideways; with 64 columns the totals row stays aligned; a totals cell menu changes the function; an `avg` summary column
  doesn't offer Sum; full screen keeps the row; Reset view turns it off.
- [ ] **Step 4: Commit** — message "Results grid: optional totals row".

### Task 6: Docs, tracker and bundle

**Files:**
- Modify: `README.md`, `src/QueryBuilder.Editor/README.md` ("Working with query results": formats, highlights, bars,
  scales, totals row; session-only; totals cover loaded rows)
- Modify: `docs/superpowers/specs/2026-11-25-results-panel-features.md` (#9, #14, #15 → "Done (unreleased, branch feature/results-formatting)")
- Modify: `src/QueryBuilder.Editor/wwwroot` (fresh bundle)

- [ ] **Step 1:** Update both READMEs and the tracker.
- [ ] **Step 2:** `cd client && npm test && npm run build`, then
  `rm -rf ../src/QueryBuilder.Editor/wwwroot/assets && cp -r dist/assets ../src/QueryBuilder.Editor/wwwroot/assets && cp dist/index.html ../src/QueryBuilder.Editor/wwwroot/index.html`.
  Expected: tests pass; `wwwroot/index.html` names the new `index-*.js`.
- [ ] **Step 3: Commit** with `git add` of exactly the READMEs, tracker and `src/QueryBuilder.Editor/wwwroot` (check
  `git status` first; never `git add -A`) — message "Results grid: ship formatting bundle and docs".
