# Results Grid Filters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-column filters, a right-click Show only / Hide menu, filter chips and "Add to query" to the query Results grid.

**Architecture:**
- **Filter state.** Filters are `GridFilter[]` in `GridView`, beside search, sort and layout, so phase 1's re-run
  carry-over, full-screen memory and Reset view cover them.
- **Applying filters.** TanStack `columnFilters`, built from the view, apply them through a custom `filterFn` in the same
  filtered row model the search uses.
- **Pure logic.** Matching, chip text, value lists and conversion to builder conditions live in `.ts` files with
  `node --test`.
- **Add to query.** It appends flat conditions to the builder through new store actions, then re-runs with the fresh
  definition.

**Tech Stack:** React 19, TypeScript, Tailwind 4, shadcn + `radix-ui` (Popover, DropdownMenu, ContextMenu, Dialog), zustand, sonner, @tanstack/react-table 8.21.3. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-11-26-results-filters-design.md` (approved). The phase 1 spec
`2026-11-25-results-grid-foundation-design.md` and phase 2 spec `2026-11-26-results-details-panel-design.md` still bind.

## Global Constraints

- **Dependencies.** No new npm dependency; never touch `package.json` or the lockfile. `radix-ui` already exports
  `ContextMenu`, `Popover`, `Dialog` and `DropdownMenu`. A new shadcn-style wrapper `components/ui/context-menu.tsx` is
  allowed.
- **Plain http.** No `navigator.clipboard`, `crypto.randomUUID` or `crypto.subtle` anywhere in `client/src`, comments
  included. Copy goes through `results/copy-text.ts`.
- **Pure files.** Use `import type` for `@/…` types and relative `.ts` runtime imports so `node --test` can load them.
  Tests live in `client/tests/*.test.ts` (`node:test`, `node:assert/strict`). Run `cd client && npm test`; type check with
  `cd client && npx tsc -b`.
- **Selectors.** Zustand/`useSyncExternalStore` selectors never return newly built objects or arrays.
- **Zoning Map.**
  - No shadows on surfaces at rest; popovers and menus use the app's existing overlay components and styles.
  - Barlow `font-numeric tabular-nums` for numbers.
  - Yellow (`bg-warning/20 text-foreground`, `bg-warning` swatch) only for the cut-off note.
  - Primary blue for active marks and chips.
  - Visible focus; WCAG 2.1 AA.
- **Comments.** Only for non-obvious *why*.
- **Exact strings:**
  - Chips bar label: "FILTERS".
  - Cut-off note: "Filtering the first {N} rows" (N = `result.rows.length`, `toLocaleString`).
  - Buttons: "Clear all", "Add to query".
  - Right-click menu: `Show only "{X}"`, `Hide "{X}"`, "Copy cell", "Open record". X is cut to 40 characters + "…"; an empty
    value shows as "(empty)".
  - Filter mark `aria-label`: "Filter {column}" / "Filter {column} (active)".
  - Header menu items: "Filter…", "Clear filter".
  - Chip remove `aria-label`: "Remove filter on {column}".
  - Value list cap message: "Showing the first 1,000 values — search to find others".
  - Profile coverage with filters: "Based on {n} filtered rows" (not cut off); "Based on {n} filtered rows of the first {m}.
    The query has more." (cut off).
  - Dialog title: "Add {k} filter(s) to the query?"; buttons "Add and run" / "Cancel".
  - Toast: "Added {k} filter(s) to the query" (singular "filter" for 1).
  - Disabled title while a run is pending: "Wait for the run to finish".
- **Grid matching.** Exact and case-sensitive.
  - Values: numbers compare via `toNumber`; booleans as booleans; `null`/`undefined`/`''` are empty (`null`); everything else
    `String(v)`.
  - Ranges: inclusive; dates via `parseDateValue` (phase 2), with a dateTime To covering the whole day.
  - Unreadable values never match a range.
- **Value lists.** Cascading: computed from rows passing every *other* column's filter, ignoring search. Sorted by count
  desc, then value. Capped at 1,000.

## Review Focus

1. **A filter on a column that vanishes after a re-run.** A new column set resets the view, so there must be no crash and
   no stale chip.
2. **Add to query with a stale closure.** The re-run after "Add and run" must send the definition *including* the appended
   conditions, not the render-time one.
3. **Numeric strings and money text in number columns.**
   - `"12.50"` matches a range like `12.5`, and Show only "12.50" matches 12.5.
   - `"$1,234"` never matches a range and is an ordinary value in Show only.
4. **Summary queries.** A filter on an aggregated result column must go to HAVING, with `aggregate` set and
   `dataType = aggregateResultDataType(...)`. A filter on a group-key column must go to WHERE.
5. **Selection, Esc and full screen.** Filter changes must clear the selection, as search does. The popover, the context
   menu and the dialog must consume Esc without exiting full screen or closing the Details panel.

---

### Task 1: Filter model and view state

**Files:**
- Create: `client/src/features/builder/results/grid-filters.ts`
- Modify: `client/src/features/builder/results/grid-view.ts`
- Test: `client/tests/grid-filters.test.ts`, `client/tests/grid-view.test.ts` (extend)

**Interfaces:**
- Consumes:
  - `toNumber` (`../../visualizations/chart-data.ts`);
  - `parseDateValue`, `profileKind` (`./column-profile.ts`);
  - type `QueryResultColumnDto`.
- Produces:

```ts
export type FilterValue = string | number | boolean | null
export type GridFilter =
  | { kind: 'values'; column: string; mode: 'include' | 'exclude'; values: FilterValue[] }
  | { kind: 'range'; column: string; min: number | null; max: number | null; includeEmpty: boolean }
export type FilterUi = 'values' | 'range'
export function filterUi(dataType: ColumnDataType): FilterUi            // number/date/dateTime → range; else values
export function valueKey(raw: unknown, dataType: ColumnDataType): FilterValue   // per Global matching rules; number columns: toNumber number if parseable, else String(raw)
export function matchesFilter(raw: unknown, filter: GridFilter, dataType: ColumnDataType): boolean
export function isEmptyFilter(f: GridFilter): boolean                   // values+include with [] ; range with min=max=null && !includeEmpty
export function setFilter(filters: GridFilter[], next: GridFilter): GridFilter[]   // replaces same column in place, else appends; drops it if isEmptyFilter(next)
export function removeFilter(filters: GridFilter[], column: string): GridFilter[]
export function showOnly(filters: GridFilter[], column: string, value: FilterValue): GridFilter[]  // values/include [value]
export function hideValue(filters: GridFilter[], column: string, value: FilterValue): GridFilter[] // adds to an existing exclude filter, else new exclude [value]
export interface ValueOption { value: FilterValue; label: string; count: number }
export function valueOptions(rows: Record<string, unknown>[], column: QueryResultColumnDto, cap?: number): { options: ValueOption[]; truncated: boolean } // cap default 1000; label '(empty)' for null
export function rowsPassing(rows: Record<string, unknown>[], filters: GridFilter[], columns: QueryResultColumnDto[], exceptColumn?: string): Record<string, unknown>[]
export function chipText(f: GridFilter, dataType: ColumnDataType): { column: string; text: string; title: string }
// grid-view.ts
GridView gains `filters: GridFilter[]` (defaultView → []; carryOver copies it on the same column set; isDefaultView requires it empty)
```

**Rules:**
- **Range on a dateTime column.** `max` holds the end of the chosen day (`Date.UTC(y,m,d) + 86_399_999`). On a `date` column
  `max` is midnight UTC of the chosen day.
- **`chipText`:**
  - Values/include: "is A, B" with "+N more" after two values.
  - Values/exclude: "is not A".
  - Ranges: "100 – 500", "≥ 100", "≤ 500", with "+ empty" when `includeEmpty`.
  - Date bounds print as `YYYY-MM-DD`. For a dateTime max, print the chosen day, not the end-of-day instant.
  - `title` is the full, untruncated text.

- [ ] **Step 1: Write the failing tests.**
  - `valueKey` and `matchesFilter`:
    - `"12.50"` matches range {min 12.5, max 12.5} on a number column;
    - `"$1,234"` does not match any range;
    - `null` matches values/include [null] and matches a range only with `includeEmpty`;
    - `"2025-01-01T00:00:00"` matches a dateTime range with min/max `Date.UTC(2025,0,1)` and max end-of-day `Date.UTC(2025,0,1)+86_399_999`;
    - `''` equals `null` for text;
    - `true` matches values [true] on a boolean column;
    - values match exactly: 'Acme' ≠ 'acme'.
  - `setFilter` replaces the same column in place, and `setFilter` with an empty filter removes it.
  - `hideValue` twice gives exclude [a, b].
  - `showOnly` replaces a range filter on the same column.
  - `valueOptions`:
    - counts, ordering (count desc, then label asc), `(empty)` label;
    - with 1,005 distinct values and cap 1000 → `options.length === 1000`, `truncated === true`.
  - `rowsPassing` with `exceptColumn` ignores that column's filter.
  - `chipText`:
    - ['A','B','C','D'] → `is A, B +2 more`;
    - range {100, null} → `≥ 100`;
    - dateTime range shows `2025-01-01 – 2025-03-31` for max = end of 2025-03-31.
  - `grid-view`: `defaultView(...).filters` is `[]`; `carryOver` keeps filters on the same set and drops them on a different
    set; `isDefaultView` is false when filters are non-empty.
- [ ] **Step 2:** Run `cd client && npm test`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement `grid-filters.ts` and the `grid-view.ts` changes.
- [ ] **Step 4:** Run `npm test` and `npx tsc -b`. Expected: all pass. If any compile error elsewhere comes from the new
  `filters` field, fix the callers.
- [ ] **Step 5:** Commit: "Results grid: filter model and view state".

### Task 2: Conversion to builder conditions

**Files:**
- Create: `client/src/features/builder/results/filters-to-query.ts`
- Test: `client/tests/filters-to-query.test.ts`

**Interfaces:**
- Consumes:
  - `GridFilter` (Task 1);
  - `aggregateResultDataType` (`../aggregate-functions.ts`);
  - types `QueryColumn`, `FilterCondition`, `FilterGroup`, `QueryResultColumnDto`.
- Produces:

```ts
export interface PlannedFilter { column: string; clause: 'where' | 'having'; conditions: FilterCondition[]; description: string }
export interface AddToQueryPlan {
  add: PlannedFilter[]                       // will be appended
  blocked: { column: string; reason: string }[]
  warnings: string[]                         // empty-values, overlap, case notes (deduplicated)
}
export function planAddToQuery(
  filters: GridFilter[],
  resultColumns: QueryResultColumnDto[],
  rows: Record<string, unknown>[],           // loaded rows, for the "has empty values" check
  definition: { columns: QueryColumn[]; filters: FilterGroup; having?: FilterGroup | null },
): AddToQueryPlan
```

**Rules** (spec "Add to query" and "Settled" 1–5):

- **Mapping.**
  - Map each filter's column to the single *visible* `QueryColumn` whose `alias || columnName` equals it.
  - 0 or 2+ matches → blocked, with "{column} isn't a column of the query any more".
- **Clause.**
  - `aggregate !== 'none'` → `having`, with `aggregate` set and `dataType: aggregateResultDataType(col.dataType,
    col.aggregate)`.
  - Otherwise → `where`, with `aggregate: 'none'` and `dataType: col.dataType`.
  - Every condition has `isParameterized: false` and `parameterName: null`.
- **values/include:**
  - [null] → `isNull` (value null);
  - one value → `equals`;
  - several → `in`.
- **values/exclude:**
  - [null] → `isNotNull`;
  - one value → `notEquals`;
  - several → `notIn`.
- **Values that can't be added.**
  - Values mixed with null in either mode → blocked: "{column}: empty values mixed with other values can't be expressed in
    the query's filters".
  - A range with `includeEmpty` → blocked with the same kind of reason.
- **Exclude on a column with empties.** When the loaded rows hold empties in that column, add a warning: "The query will
  also leave out empty {column} values."
- **Text equality.** For equals/in on a text column, add the warning once: "Matching in the query may ignore upper/lower
  case."
- **Ranges.**
  - Numbers, both ends → `between` with value `JSON.stringify([String(min), String(max)])`. One end → `greaterOrEqual` /
    `lessOrEqual`. Numbers are written with `String(n)`.
  - Dates: min → `greaterOrEqual` `YYYY-MM-DD`.
    - dateTime max → `lessThan` the next day `YYYY-MM-DD`.
    - date max → `lessOrEqual` `YYYY-MM-DD`.
- **Value text.** Booleans → `'true'`/`'false'`. The `in`/`notIn` value is the values joined with `,` unless any value
  contains `,`; then it is `JSON.stringify(values)`.
- **"Match any" clauses.** If the target clause's group has `operator === 'or'` and 1+ conditions → block every filter
  bound for it: "Your query's filters use 'Match any'. Switch to 'Match all' in Build to add these." For HAVING the text is
  "Your query's totals filters use 'Match any'. …".
- **Overlap.** Add the warning "The query already filters {column}; both conditions will apply." when the clause already
  has a condition with the same `tableAlias`, `columnName` and `aggregate`.
- **`description`.** A plain sentence for the dialog, e.g. "ProgramName is Cloud Basics or Finance 101", "FeePaid between
  100 and 500", "Sum of Fee at least 500".

- [ ] **Step 1: Write failing tests**, one per rule:
  - **Mapping by alias:** a QueryColumn {columnName 'Fee', alias 'FeePaid'} maps from 'FeePaid'.
  - **Unmapped column:** blocked.
  - **Summary column:** {aggregate 'sum'} → clause 'having', aggregate 'sum', dataType from `aggregateResultDataType`.
  - **Values to operators:**
    - include ['A'] → `equals` 'A';
    - include ['A','B'] → `in` 'A,B';
    - include ['A,1','B'] → `in` '["A,1","B"]';
    - exclude ['A'] → `notEquals`, plus the empty-values warning when rows contain null for that column;
    - include [null] → `isNull`;
    - include ['A', null] → blocked.
  - **Number ranges:** {min 100, max 500} → `between` '["100","500"]'; {min 100, max null} → one `greaterOrEqual` '100'.
  - **Date ranges:**
    - dateTime range 2025-01-01..2025-03-31 → `greaterOrEqual` '2025-01-01' and `lessThan` '2025-04-01';
    - date-type column → `lessOrEqual` '2025-03-31'.
  - **includeEmpty range:** blocked.
  - **Match-any clauses:** WHERE with operator 'or' and 1 condition → blocked with the Match any text; an empty 'or' group
    is not blocked.
  - **Overlap:** the overlap warning appears.
  - **Case note:** it appears once for two text filters.
- [ ] **Step 2:** Run `npm test`. Expected: FAIL.
- [ ] **Step 3:** Implement `filters-to-query.ts`.
- [ ] **Step 4:** Run `npm test` and `npx tsc -b`. Expected: pass.
- [ ] **Step 5:** Commit: "Results grid: plan grid filters as query conditions".

### Task 3: Filtering in the table, footer and profile

**Files:**
- Modify:
  - `client/src/features/builder/results/grid-table.ts`;
  - `client/src/features/builder/results/useResultsGrid.ts`;
  - `client/src/features/builder/results/ResultsGrid.tsx` (`resetKey`);
  - `client/src/features/builder/ResultsTable.tsx`;
  - `client/src/features/builder/results/ColumnProfileView.tsx`.

**Interfaces:**
- Consumes: Task 1 (`matchesFilter`, `rowsPassing`, `setFilter`, `removeFilter`, `showOnly`, `hideValue`).
- Produces, on the object returned by `useResultsGrid` (later tasks use these):

```ts
filters: GridFilter[]                                   // = view.filters
setColumnFilter(next: GridFilter): void                 // via setFilter
clearColumnFilter(column: string): void
clearFilters(): void
showOnlyValue(column: string, raw: unknown): void       // valueKey + showOnly
hideColumnValue(column: string, raw: unknown): void     // valueKey + hideValue
removeFilters(columns: string[]): void                  // used after Add to query
filterRows: Record<string, unknown>[]                   // result.rows passing ALL column filters, search ignored (memoised on result.rows, filters)
```

**Rules:**
- **Column definitions.** `buildColumnDefs` gives each column `filterFn: (row, id, f: GridFilter) =>
  matchesFilter(row.getValue(id), f, c.dataType)`.
- **TanStack state.** `useResultsGrid` passes `state.columnFilters = view.filters.map(f => ({ id: f.column, value: f }))`,
  memoised on `view.filters`.
- **Filtered row model.** `getFilteredRowModel` already applies the global search. Make sure `getColumnCanFilter` isn't
  blocking anything; `manualFiltering` stays false.
- **Selection reset.** `ResultsGrid`'s `resetKey` gains a stable serialisation of `view.filters` (`JSON.stringify`), so
  any filter change clears the selection, as search does.
- **Footer.** `ResultsTable` keeps computing `filtered` from `rows.length` and `raw` from `grid.totalRaw`. Check that the
  footer says "(filtered from N)" whenever search or filters remove rows.
- **Profile.** `ColumnProfileView` gets new props `rows: Record<string, unknown>[]` and `filtered: boolean`.
  - `ResultsTable` passes `grid.filterRows` and `grid.filters.length > 0`.
  - The profile is computed from `rows`, memoised on `(rows, column)`.
  - When `filtered`, the coverage text uses the exact filtered strings from Global Constraints (n = `rows.length`, m =
    `result.rows.length`). Otherwise the phase 2 wording is unchanged.
  - The stale-column guard (`result.columns.includes(column)`) stays.

- [ ] **Step 1:** Implement the wiring.
- [ ] **Step 2:** Add a node test in `client/tests/grid-table.test.ts`: `buildColumnDefs(...)[i].filterFn` exists and
  delegates to `matchesFilter`. Call it with a fake row `{ getValue: () => '12.50' }` and a range filter, and expect true.
- [ ] **Step 3:** Run `npm test` and `npx tsc -b`. Expected: pass.
- [ ] **Step 4:** Commit: "Results grid: apply column filters; profile follows them".

### Task 4: Header filter mark and filter popover

**Files:**
- Create:
  - `client/src/features/builder/results/FilterPopover.tsx`;
  - `client/src/features/builder/results/ValuesFilter.tsx`;
  - `client/src/features/builder/results/RangeFilter.tsx`.
- Modify:
  - `client/src/features/builder/results/GridHeaderCell.tsx`;
  - `client/src/features/builder/results/GridHeaderRow.tsx`;
  - `client/src/features/builder/results/ResultsGrid.tsx` (pass the filter actions through `HeaderActions`).

**Interfaces:**
- Consumes:
  - Task 1 (`filterUi`, `valueOptions`, `rowsPassing`);
  - Task 3 (`setColumnFilter`, `clearColumnFilter`, `filters`);
  - `histogram` and `Histogram.tsx` from phase 2.
- Produces:
  - `HeaderActions` gains `filter: { active: boolean; open: () => void; clear: () => void }`.
  - `FilterPopover({ column, rows, filters, columns, onApply(next: GridFilter | null), anchor })`, where `null` means
    clear.
  - `ResultsTable` needs to open a column's popover from a chip (Task 5). Expose an `openFilter(column: string)` from the
    grid handle (`GridHandle` gains `openFilter`), or lift the open-column state to `ResultsTable`. Lifting is recommended:
    `const [filterOpenFor, setFilterOpenFor] = useState<string | null>(null)`, passed down.

**Rules:**
- **Filter mark.**
  - It sits in the header after the name and before the menu button: a lucide `Filter` icon in a ghost icon button
    (size like the menu trigger).
  - Muted when inactive; `text-primary` plus a filled style when active.
  - It carries the exact `aria-label` strings.
  - Clicking it must not trigger sort or drag (stop propagation the way the menu trigger does).
  - The header menu gets "Filter…" (opens the popover) and "Clear filter" (only when active), placed after the sort items.
- **Popover.**
  - A Radix Popover anchored to the header cell, with no shadow beyond the app's popover default.
  - Heading: column name plus `TypeBadge`.
- **Values UI.**
  - A search input, filtering options by label with case-insensitive `includes`.
  - "Select all" and "Clear" text buttons.
  - A scrollable checklist (`max-h-64`, using `components/ui/checkbox`). Each item shows the label, plus the count in Barlow
    on the right.
  - The cap message when truncated.
  - Cancel and Apply.
  - Initial ticks:
    - no filter → all ticked;
    - include → those ticked;
    - exclude → all except those.
  - On Apply:
    - all ticked → `onApply(null)`;
    - fewer ticked than half the options → include filter with the ticked values;
    - otherwise → exclude filter with the unticked values;
    - none ticked → Apply is disabled.
- **Range UI.**
  - A mini `Histogram` of the column over `rowsPassing(…, exceptColumn)` values, then From and To inputs.
    - Numbers: `<Input inputMode="decimal">`.
    - Dates: `<Input type="date">`.
  - An "Include empty values" checkbox, then Clear and Apply.
  - Invalid input disables Apply and shows a short message: "From must be before To" or "Enter a number".
  - The dateTime end-of-day is applied when building the filter.
- **Options source.** Options and the histogram come from `rowsPassing(result.rows, filters, columns, column.name)`,
  memoised while the popover is open.
- **Esc** inside the popover closes only the popover. Radix calls preventDefault on Escape, and the panel and full-screen
  listeners already ignore `defaultPrevented`. Verify this.

- [ ] **Step 1:** Implement the components and wiring.
- [ ] **Step 2:** Run `npm test` and `npx tsc -b`. Expected: pass.
- [ ] **Step 3:** Commit: "Results grid: header filter popovers".

### Task 5: Chips bar

**Files:**
- Create: `client/src/features/builder/results/FilterChips.tsx`
- Modify: `client/src/features/builder/ResultsTable.tsx`

**Interfaces:**
- Consumes:
  - `chipText` (Task 1);
  - grid filter actions (Task 3);
  - `setFilterOpenFor` (Task 4).
- Produces: `FilterChips({ filters, columns, truncated, loadedRows, onOpen(column), onRemove(column), onClearAll, onAddToQuery?, addDisabledReason? })`.

**Rules:**
- **Placement.** Rendered under the toolbar and truncation banner, above the band, only when `filters.length > 0`. Full
  width.
- **Label.** "FILTERS" in Barlow caps (muted).
- **Chips.**
  - One chip per filter, in array order. Each chip is a button: **column** in bold, then the text, with `title` = full text.
  - Clicking it calls `onOpen(column)`. ✕ is a separate small button with the exact `aria-label`.
  - Delete or Backspace on a focused chip removes it.
  - Chips use primary-tint colours (`bg-primary/10 text-primary border-primary/30` or the app's equivalent).
- **Cut-off note.** When `truncated`, the yellow note "Filtering the first N rows".
- **Right side.**
  - "Clear all": ghost button.
  - "Add to query": primary, rendered only when `onAddToQuery` is provided. It is disabled with `title` =
    `addDisabledReason` when that is set.

- [ ] **Step 1:** Implement and wire it (Add to query just calls a prop for now; Task 7 fills it).
- [ ] **Step 2:** Run `npm test` and `npx tsc -b`. Expected: pass.
- [ ] **Step 3:** Commit: "Results grid: filter chips bar".

### Task 6: Right-click menu and Details panel filter actions

**Files:**
- Create:
  - `client/src/components/ui/context-menu.tsx` (shadcn-style wrapper over `radix-ui` `ContextMenu`, matching
    `dropdown-menu.tsx`'s styling);
  - `client/src/features/builder/results/CellContextMenu.tsx`.
- Modify:
  - `client/src/features/builder/results/ResultsGrid.tsx` and/or `GridRow.tsx`/`GridCell.tsx`;
  - `client/src/features/builder/results/useGridSelection.ts` (open on Shift+F10 / ContextMenu key);
  - `client/src/features/builder/results/RowDetails.tsx`;
  - `client/src/features/builder/results/ColumnProfileView.tsx`;
  - `client/src/features/builder/results/Histogram.tsx`.

**Interfaces:**
- Consumes:
  - Task 3 actions: `showOnlyValue`, `hideColumnValue`, `setColumnFilter`;
  - the existing grid `copy`, and the Details panel open/tab state in `ResultsTable` (for "Open record").
- Produces:
  - `CellContextMenu` takes `onShowOnly`, `onHide`, `onCopyCell`, `onOpenRecord` for the cell under the pointer or the
    active cell.
  - `Histogram` gains an optional `onBarClick?: (bin: Bin) => void`; bars become buttons when it is given.

**Rules:**
- **Performance.** Don't wrap every virtualised cell in its own ContextMenu root. Use one ContextMenu around the grid's
  scroll body. On `contextmenu`, find the cell from the event target (`data-row`/`data-col` attributes on cells; add them if
  missing), move the active cell there if it is outside the selection, and render the items for that cell.
  - Keyboard: Shift+F10 or the ContextMenu key on the focused grid opens the same menu at the active cell, anchored to the
    cell's rect.
  - Memoised `GridRow` props must stay primitive (phase 1 rule).
- **Items.**
  - `Show only "X"` and `Hide "X"`, using the exact strings and the 40-character cut.
  - A separator.
  - "Copy cell" copies just that cell. The selection is already that cell, so the existing copy works.
  - "Open record" opens the Details panel on the Row tab.
  - X is the cell's display string (`displayString`), or "(empty)".
- **Details panel.**
  - Column tab:
    - Each top-value row's label becomes a button opening a small DropdownMenu with Show only / Hide (exact strings).
    - On number and date profiles, histogram bars become buttons ("Show only this range") that set a range filter to the
      bin bounds (dateTime: max = bin.to as is).
    - Boolean bars Yes/No/Empty are buttons with the same Show only / Hide menu.
  - Row tab: each field gets a small icon button (lucide `Filter`, `aria-label="Show only this value"`). It is visible on
    hover or focus-within and calls `showOnlyValue`.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** Run `npm test` and `npx tsc -b`. Expected: pass.
- [ ] **Step 3:** Commit: "Results grid: right-click filters and panel filter actions".

### Task 7: Add to query

**Files:**
- Create: `client/src/features/builder/results/AddToQueryDialog.tsx`
- Modify:
  - `client/src/features/builder/builder-store.ts`;
  - `client/src/features/builder/ResultsTable.tsx`;
  - `client/src/pages/QueryEditorPage.tsx`.

**Interfaces:**
- Consumes: `planAddToQuery`, `AddToQueryPlan` (Task 2); `removeFilters` (Task 3); `FilterChips.onAddToQuery` (Task 5).
- Produces:

```ts
// builder-store.ts
appendFilterConditions: (conditions: FilterCondition[]) => void   // appends to definition.filters.conditions (through reconcileParameters like other edits)
appendHavingConditions: (conditions: FilterCondition[]) => void   // creates having { operator: 'and', conditions: [], groups: [] } if absent, then appends
// ResultsTable props
queryDefinition?: { columns: QueryColumn[]; filters: FilterGroup; having?: FilterGroup | null }
onAddToQuery?: (where: FilterCondition[], having: FilterCondition[]) => void
addToQueryDisabledReason?: string
```

**Rules:**
- **The dialog.**
  - Clicking "Add to query" computes `planAddToQuery(grid.filters, result.columns, result.rows, queryDefinition)` and opens
    `AddToQueryDialog` (shadcn `Dialog`).
  - Title: the exact "Add {k} filter(s) to the query?" (k = `plan.add.length`).
  - Body: lists under "Filters" and "Totals filters" (each `description`), then "Can't be added" (column: reason), then
    warnings.
  - "Add and run" is disabled when `plan.add.length === 0`. When nothing can be added, the title reads "These filters can't
    be added to the query".
- **Confirm.**
  - Call `onAddToQuery(whereConditions, havingConditions)` (flattened from `plan.add`), then
    `grid.removeFilters(plan.add.map(p => p.column))`, then close.
- **In `QueryEditorPage`:**
  - Pass `queryDefinition={definition}`.
  - Pass `addToQueryDisabledReason={runMutation.isPending ? 'Wait for the run to finish' : undefined}`.
  - `onAddToQuery` = append both lists through the new store actions, then run with the **fresh** definition. Change the run
    mutation to read the definition when it is called, not from the render closure: in `mutationFn`, use
    `useBuilderStore.getState().definition`, and the same for export and preview if they share the pattern. Keep
    `parameterValues` as now.
  - Then show the toast with the exact string, and keep the Results tab active (the run's `onSuccess` already selects it).
- **DrillPage** passes neither prop, so it has no Add to query.

- [ ] **Step 1:** Add the store actions. Implement the dialog and the wiring.
- [ ] **Step 2:** Run `npm test` and `npx tsc -b`. Expected: pass.
- [ ] **Step 3:** Commit: "Results grid: add grid filters to the query".

### Task 8: Docs, tracker, bundle

Runs after the final review and browser acceptance (phase 2 ruling: build the bundle once).

**Files:**
- Modify:
  - `docs/superpowers/specs/2026-11-25-results-panel-features.md`: #10–13 → "Done (unreleased, branch
    feature/results-filters)".
  - `README.md` and `src/QueryBuilder.Editor/README.md`, section "Working with query results": filters, right-click, chips,
    Add to query and its limits, and that profiles follow filters.
  - `src/QueryBuilder.Editor/wwwroot/` (rebuilt bundle).

- [ ] **Step 1:** Edit the docs.
- [ ] **Step 2:** Run `cd client && npm test && npm run build`, then copy `client/dist/assets` and `index.html` into
  `src/QueryBuilder.Editor/wwwroot` (delete the old assets first).
- [ ] **Step 3:** Commit: "Results grid: ship filters bundle and docs". Stage the specific files plus
  `src/QueryBuilder.Editor/wwwroot`; never use a repo-wide `git add -A`.

### Acceptance (controller, Opus, after the final review)

Recreate the phase 1 temporary views (`vw_GridAcceptance` 2,500 rows) and add them to the allowlist. Run the sample app on
:5080 and check with Playwright:

- **Header popovers:** values (search, select all, cap) and range (number, date, dateTime end of day).
- **Right-click menu:** mouse and Shift+F10.
- **Details panel:** top-value and histogram filters, and the Row tab icon.
- **Chips:** reopen, ✕, Delete key, Clear all; the footer count.
- **Following the filters:** totals and the profile.
- **Esc order:** in the popover, the menu and the dialog.
- **Add to query:**
  - on a plain query (WHERE);
  - on a summary query (HAVING), checking that the builder shows the conditions and the re-run returns matching rows and the
    request body carries the new conditions;
  - Match-any block, empty-values warning, mixed-empty block.
- **Plain http:** via the routed `qb.test` origin.
- **Looks:** both themes.

Afterwards drop the views, restore the allowlist and delete any temporary saved queries.
