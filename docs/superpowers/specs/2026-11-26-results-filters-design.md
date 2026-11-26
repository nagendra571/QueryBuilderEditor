# Results grid, phase 3: filters (design)

Date: 2026-11-26. Status: draft for approval.

- **Decisions so far.** The user picked option A ("grid filters always filter the loaded rows; Add to query gives the true
  answer") and said "so far so good" to the playback below.
- **Feature list and tracking.** `docs/superpowers/specs/2026-11-25-results-panel-features.md`. This phase covers items #10
  (per-column filters), #11 (right-click show only / hide), #12 (filter chips) and #13 (Add to query).
- **Earlier phases still bind.** Phase 1 spec `2026-11-25-results-grid-foundation-design.md` and phase 2 spec
  `2026-11-26-results-details-panel-design.md`.
- **Mockup.** `docs/superpowers/specs/assets/2026-11-26-results-filters-mockup.html`.

## Intent

Business users narrow results down while they read them. Excel's filter dropdowns, Metabase's "filter by this value" and
Databricks' result filters set the expectation. The grid holds only the rows the run returned (1,000 by default), so a
grid filter on a cut-off result answers "among the first 1,000". Phase 3 therefore does three things:

- makes filtering instant on the loaded rows;
- says plainly when that is partial;
- gives a one-click way to turn the filters into real query filters ("Add to query") for the true, query-wide answer.

## Goals

1. **Per-column filters**, opened from a filter mark in each header:
   - text and Yes/No get a searchable value checklist with counts;
   - numbers and dates get a from/to range with a small histogram.
2. **A right-click menu** on any cell: Show only "value", Hide "value", Copy cell, Open record. The keyboard opens it too.
3. **Filtering from the Details panel:**
   - Column tab: click a top value or a histogram bar to get Show only / Hide.
   - Row tab: each field gets "Show only this value".
4. **A chips bar.**
   - Content: one removable chip per filtered column, plus Clear all.
   - Cut-off results: it says "Filtering the first N rows".
   - **Add to query**: turns the chips into builder filters and re-runs.
5. **Everything follows the filtered rows:** the footer counts, selection totals, Copy, the Row tab's "ROW n OF m", and the
   Column profile (which says so).

## Non-goals

- Server-side filtering without changing the query. That was option B, rejected.
- Contains / starts-with text conditions in the grid. The search box covers loose matching; the builder has those
  operators.
- OR between different columns' filters.
- Showing nested filter groups in the builder.
- Remembering filters across visits. That is #17.
- Number and date display formats. That is phase 4.

## Anti-goals (a failure even if it works)

- **A filtered number that looks query-wide but isn't.** When results were cut off, the chip bar's yellow "Filtering the
  first N rows" note is mandatory, and the profile says "N filtered rows of the first M".
- **"Add to query" quietly changing meaning.** Two cases must be shown before anything is added:
  - a grid filter whose SQL meaning differs, e.g. SQL `NOT IN` also drops empty values;
  - a grid filter that can't be expressed in the builder.
- **The builder showing something it can't render.** That means nested groups, or JSON the user can't read when a plain
  list would do.
- **Losing phase 1 or 2 behaviour.** Keyboard operation, the Esc order, full screen, re-run carry-over, the store's render
  isolation, copy on http.
- **Breaking the design rules.** Shadows on surfaces at rest, or yellow for anything but the cut-off note. Popovers and menus
  keep the app's existing overlay style.

## Constraints

- **Client-only.** No API, schema or package change. `QueryDefinition` and `FilterCondition`
  (`client/src/types/index.ts`) are unchanged.
- **Builder model.**
  - `definition.filters` (WHERE) and `definition.having` (Totals filter) are each a `FilterGroup {operator: 'and'|'or',
    conditions, groups}`.
  - The builder UI shows only `conditions`, never nested `groups`.
  - Store actions live in `features/builder/builder-store.ts`.
  - The server parses `in` / `notIn` values as a JSON array, or falls back to comma-separated (`SqlQueryBuilder.ParseArray`).
  - The builder input for `in` shows the raw text ("Comma-separated values").
  - `between` takes `JSON.stringify([lo, hi])`.
- **Result columns ↔ builder columns.**
  - A result column's name is `QueryColumn.alias || columnName` (server `EffectiveAlias`). Only visible columns are
    returned.
  - A column with `aggregate !== 'none'` is a summary column, so its filter goes to `having` (with that aggregate set).
    Otherwise it goes to `filters`.
- **TanStack.** The grid already uses `getFilteredRowModel` for the global search. Column filters use TanStack
  `columnFilters` with a custom filter function, combined with the existing global filter.
- **Unchanged from phases 1 and 2:**
  - no `navigator.clipboard`/`crypto.*` in `client/src`;
  - pure logic in `.ts` files with node tests;
  - no new dependency;
  - Zustand/`useSyncExternalStore` selectors never return new objects.

## Approaches

The user handed over the how. My call:

- **Chosen.** TanStack `columnFilters` holding our own filter objects, plus a custom `filterFn`. Filter state lives in
  `GridView`, beside search, sort and layout, so phase 1's re-run carry-over and full-screen memory apply to it. A pure
  `grid-filters.ts` holds three things: matching, chip text, and conversion to builder conditions.
- **Rejected.** Pre-filtering `result.rows` before handing them to the table. That duplicates what TanStack already does for
  search, and breaks the row identity the selection and memory rely on.
- **Rejected.** A separate filter store. That would be a second source of truth beside `GridView`.

The user chose A over B (re-run automatically when results are cut off). See Intent.

## Decided behaviour

### Filter model (pure, `results/grid-filters.ts`)

```ts
type GridFilter =
  | { kind: 'values'; column: string; mode: 'include' | 'exclude'; values: FilterValue[] }   // text, boolean, any column via right-click
  | { kind: 'range'; column: string; min: number | null; max: number | null; includeEmpty: boolean } // number, date (epoch ms for dates)
type FilterValue = string | number | boolean | null   // null = (empty)
```

**Rules:**

- **At most one filter per column.** A new one on the same column replaces the old one; for example, a right-click "Show
  only" replaces a range.
- **Combining.** Filters on different columns are AND. Within a `values` filter the values are OR, or "none of" for
  `exclude`. Search still applies on top.
- **Matching.**
  - Values match by the cell's raw value, normalised to a key: numbers via `toNumber`, booleans as booleans, null, `''` and
    undefined as empty, everything else `String(v)`. The comparison is exact and case-sensitive.
  - Ranges are inclusive at both ends.
  - Dates parse with phase 2's `parseDateValue`, so zoneless text is read as UTC.
  - For a dateTime column, the To date includes that whole day. The filter stores the end of day; the input shows the date.
  - Values that can't be parsed (unreadable) never match a range. Empty values match only when `includeEmpty` is set.
- **An empty filter is no filter.** `values` with none ticked under `include`, or a range with neither end and
  `includeEmpty` false, is treated as "not filtered" and is never stored.

### Where filters are set

- **Header filter mark.**
  - A small funnel icon button in every header cell, after the name and before the menu.
  - Muted when the column isn't filtered, primary blue (filled) when it is.
  - It has an `aria-label` such as "Filter ProgramName" or "Filter ProgramName (active)" and opens the filter popover. The
    header menu also gets "Filter…" and "Clear filter".
  - Clicking the header label still cycles sort.
- **Popover.**
  - It is a Radix Popover anchored to the header, opened by click, Enter or Space on the mark. It has a heading with the
    column name and type badge.
  - **Values** (text, guid, unknown, boolean):
    - Contents: a search box, then "Select all" and "Clear", then a checklist of the distinct values from the loaded rows
      that pass every *other* column's filter (Excel-style cascading). Each value shows its count and the list includes an
      "(empty)" entry.
    - Order: by count descending, then by value.
    - Size: at most 1,000 distinct values are listed. Beyond that it says "Showing the first 1,000 values — search to find
      others".
    - Apply/Cancel. Apply with every value ticked removes the filter.
  - **Range** (number, date, dateTime):
    - Contents: a mini histogram of the loaded rows (same bins as the profile), with the selected range highlighted. Below it,
      From and To inputs and "Include empty values", then Clear and Apply.
    - Inputs: numbers use a numeric text input; dates use `<input type="date">`.
    - Invalid input (From > To, not a number) disables Apply and shows a short inline message.
- **Right-click on a cell** (and Shift+F10 or the context-menu key on the active cell):
  - A Radix ContextMenu with **Show only "X"**, **Hide "X"**, a separator, **Copy cell** and **Open record** (opens the
    Details panel on the Row tab).
  - Show only sets `values/include [X]`. Hide adds X to an existing `exclude` filter, or starts one.
  - X is truncated to about 40 characters in the label, and empty shows as "(empty)".
  - Right-clicking a cell outside the current selection first moves the active cell there.
- **Details panel.**
  - Column tab: each top value becomes a button. Clicking it opens a small menu with Show only / Hide. Each histogram bar is
    a button that applies "Show only this range" (a range filter for the bar's bounds) directly. A range can't be hidden.
  - Row tab: each field gets a hover/focus "Show only this value" icon button.

### Chips bar (#12)

- **Where.** A row under the toolbar (and under the truncation banner), shown only when at least one filter is active.
- **Label.** It starts with a "FILTERS" label in Barlow caps.
- **Chips.** One per filter, in the order they were added.
  - Text, for example:
    - "**ProgramName** is Cloud Basics, Finance 101";
    - "**ProgramName** is not Cloud Basics";
    - "**FeePaid** 100 – 500";
    - "**FeePaid** ≥ 100";
    - "**EnrolledOn** 2025-01-01 – 2025-03-31";
    - "**Notes** is (empty)".
  - Long lists show the first two and "+3 more", with the full list in the title.
  - "+ empty" is added when `includeEmpty` is set.
  - Clicking a chip reopens that column's popover. Its ✕ button ("Remove filter on ProgramName") removes it.
- **Right side.**
  - **Clear all**, a ghost button.
  - **Add to query**, a primary button. It appears only when the host passes `onAddToQuery` (the query editor does;
    DrillPage doesn't).
- **Cut-off results.** When `result.truncated`, a yellow note reads "Filtering the first N rows" (the same warning style as
  phase 1's banner).
- **Footer.** It already reads "1–40 of 412 (filtered from 1,000)" through `footerRange`. Its filtered count now reflects
  search and filters together.
- **Keyboard.** Chips are buttons, so Tab reaches them. Delete or Backspace on a focused chip removes it.

### Add to query (#13)

- **Interface.** `ResultsTable` gets an optional `onAddToQuery?: (where: FilterCondition[], having:
  FilterCondition[], filterCount: number) => boolean` (returns false when blocked, so the grid keeps its filters). The grid needs to know the builder columns, so it also gets `queryColumns?:
  QueryColumn[]` (the current definition's columns) to map each result column name back to `{tableAlias, columnName,
  dataType, aggregate}`.
- **Conversion** (pure `toBuilderConditions(filters, queryColumns, resultColumns)`). Each filter becomes one or more flat
  `FilterCondition`s, AND-ed:
  - **values/include:**
    - 1 value → `equals`; several → `in`;
    - only (empty) → `isNull`.
  - **values/exclude:**
    - 1 value → `notEquals`; several → `notIn`;
    - only (empty) → `isNotNull`.
  - **range:**
    - both ends, numbers → `between` `[lo, hi]`;
    - dates → `greaterOrEqual` From plus `lessThan` (To + 1 day) for dateTime columns, or `lessOrEqual` To for date-only
      values;
    - one end → `greaterOrEqual` or `lessOrEqual`.
  - **Value formats:**
    - The `in`/`notIn` value is comma-separated (readable in the builder) unless some value contains a comma, in which case
      it is a JSON array.
    - in/notIn uses a JSON array also when a value has leading/trailing spaces, is blank, or starts with "[" (the server
      trims comma lists).
    - Numbers are written plainly; booleans as `true`/`false`; dates as `YYYY-MM-DD`, or `YYYY-MM-DDTHH:mm:ss` when there is
      a time.
  - Each condition has `isParameterized: false`.
- **Filters that can't be added as they are.** Each one is listed with a reason; none of them is added silently.
  - **(empty) mixed with values**, in include or exclude, or **a range with `includeEmpty`**. SQL needs an OR group, which the
    builder can't show. *Can't be added.*
  - **Hide (exclude), when the column has empty values in the loaded rows.** SQL `NOT IN`/`<>` also drops empty values.
    *Added*, with the warning "The query will also leave out empty ProgramName values."
  - **Equality on text.** SQL Server's usual collation ignores case. A *note*, not a blocker: "Matching in the query may
    ignore upper/lower case." It is shown only when a text equals/in filter exists.
  - **The builder's Filters set to "match any" (`or`) with 1+ existing conditions.** Appending would change their meaning.
    The WHERE part is *blocked* with "Your query's filters use 'Match any'. Switch to 'Match all' in Build to add these." The
    same applies to `having`.
- **Confirm step.** Clicking Add to query opens a small dialog, "Add 3 filters to the query?".
  - It lists each condition in plain words, grouped under "Filters" and "Totals filters".
  - It lists the warnings and the filters that can't be added.
  - Its buttons are **Add and run** and Cancel.
  - When nothing can be added, the primary button is disabled and the dialog explains why.
- **On confirm:**
  1. The query editor appends the conditions to `definition.filters.conditions` and `definition.having.conditions` (creating
     `having` with operator `and` if it is absent), through new builder-store actions `appendFilterConditions` and
     `appendHavingConditions`.
  2. The grid clears exactly the filters that were added; ones that couldn't be added stay as chips.
  3. The editor re-runs the query and stays on the Results tab.
  4. A toast says "Added 3 filters to the query". The query becomes unsaved-changed, as with any builder edit.
- **When the query uses parameters,** the re-run uses the current parameter values, exactly as Run does.

### What follows the filters

- **Display rows.** `rows` (TanStack's filtered and sorted model) already drive the grid, the selection bounds, copy, the
  store's published `rows`, selection totals and the Row tab. Adding column filters to the same model makes them all follow
  without further work.
- **Column profile (changes phase 2).** The profile is computed from the **filtered** rows when any column filter is active
  (search still ignored, as in phase 2). The coverage line becomes:
  - not cut off: "Based on 412 filtered rows";
  - cut off: the yellow note "Based on 412 filtered rows of the first 1,000. The query has more."
  - With no filters, phase 2's wording is unchanged.
  - The profile takes filtered rows from a new published field, or a prop. It does **not** read the search-filtered rows, and
    it stays memoised on (rows, column).
- **Selection.** It is cleared when filters change, like search; the filters join the grid's `resetKey`.
- **Re-runs and full screen.**
  - Filters live in `GridView`, so phase 1's `carryOver` keeps them across a re-run with the same column set. On a different
    column set, filters on vanished columns are dropped.
  - "Reset view" clears them.
  - Full screen keeps them, because the view state sits above the portal.
- **The values checklist and histogram** are computed from loaded rows that pass the other columns' filters, ignoring the
  search box. They are memoised per open popover.

## Settled calls (designer's calls)

- One filter per column.
- Exact, case-sensitive matching in the grid.
- Cascading value lists.
- A 1,000-value cap in the list.
- Profiles follow the filters.
- The confirm dialog before Add to query.
- Hide-with-empties is added with a warning, while empty+values is blocked.
- Histogram bars offer only "Show only this range".

## Settled after the builder check (designer's calls)

1. **Mapping result columns back to the builder.**
   - A result column maps to the builder column whose `alias || columnName` equals its name, among **visible** columns of the
     **current** definition.
   - No match, or more than one match, makes that filter *can't be added* ("ProgramName isn't a column of the query any
     more").
   - While a run is pending (the grid shows the previous result), Add to query is disabled, with the title "Wait for the run
     to finish".
   - Builder edits made since the last run don't block it: mapping is by name against the current definition, and what isn't
     found is listed as can't-add.
2. **WHERE vs HAVING.** The builder auto-groups by every visible non-aggregate column when any column has a total. A
   non-aggregate result column is therefore always a group key, and filtering it in WHERE is equivalent to filtering the
   grouped rows. No extra blocked case.
3. **Overlap with existing conditions.** The confirm dialog adds a note for each column that already has a condition in the
   same clause: "The query already filters ProgramName; both conditions will apply." It is not blocked.
4. **Date ranges.**
   - `ColumnDataType` distinguishes `'date'` from `'dateTime'`. SQL Server `date` columns may arrive as `dateTime`.
   - For a `dateTime` column the upper bound always becomes `lessThan` (To + 1 day, `YYYY-MM-DD`). For `'date'` it becomes
     `lessOrEqual` To. Inputs are date-only.
   - Number bounds are written as the user typed them (trimmed, validated with `toNumber`), so precision is preserved.
5. **Types.** `ColumnDataType` is exactly text, number, date, dateTime, boolean, guid, unknown. All have a filter mark:
   - values filter: text, guid, unknown, boolean;
   - range filter: number, date, dateTime.
   - A right-click "Show only" on a number or date column makes a values filter, which replaces any range. Its chip reads
     "**FeePaid** is 0.37".
6. **Re-run after Add.** `QueryEditorPage`'s run mutation captures `definition` from the render. The add-and-run path must
   run with the fresh store state, e.g. `useBuilderStore.getState().definition` after the append, or by triggering the run
   from an effect once the definition has changed. It must never run with the stale closure.
7. **carryOver and clearing.**
   - `carryOver` keeps its current rule: same column set keeps the whole view including filters; a different set resets
     everything.
   - `ResultsTable` owns clearing. When the user confirms, it calls `onAddToQuery(...)`, then immediately removes the added
     filters from `GridView` via `setView`. The re-run returns the same columns, so the remaining filters carry over.
   - If the run fails, the conditions are in the builder and the grid filters are gone. That is acceptable: the user sees
     the error and the builder.
8. **`onAddFilter` stays.** The truncation banner's "Add a filter" (go to Build) is separate and unchanged. Add to query
   lives only in the chips bar.
9. **Case.** Values that differ only by case are separate entries in the grid list. The case note in the dialog is enough;
   no de-duplication.

## Left to the builder

- File and component names. Suggested: `grid-filters.ts` (pure: types, matching, chip text, `toBuilderConditions`),
  `FilterPopover.tsx`, `ValuesFilter.tsx`, `RangeFilter.tsx`, `FilterChips.tsx`, `CellContextMenu.tsx`,
  `AddToQueryDialog.tsx`.
- How `GridView` stores filters (an array in add order is suggested) and the exact `carryOver` change.
- Whether Radix ContextMenu or a DropdownMenu at the pointer works better with the virtualised cells. It must work by
  keyboard.

## Tests and acceptance

**Node tests**, on the pure files:

- matching for every kind:
  - empty and null handling;
  - numeric strings;
  - dates, including zoneless text and the end of day for dateTime;
  - unreadable values;
- chip text, including "+N more";
- cascading value lists;
- `toBuilderConditions` for every operator mapping, the comma/JSON choice, blocked and warned cases, and HAVING routing;
- `carryOver` with filters.

**Browser acceptance** (Opus, Playwright, on the sample app with the phase 1 temporary 2,500-row view recreated and then
removed):

- the header popovers, the right-click menu (mouse and keyboard), and clicks in the panel;
- chips, Clear all and the footer count;
- totals and profile following the filters;
- Add to query on a plain query and on a summary query (HAVING), checking that the builder shows the conditions and the
  re-run returns matching rows;
- the "match any" block and the empty warnings;
- the http routed origin, both themes and both densities;
- no regression in Esc, full screen or selection.

## Shipping

- Client-only. Rebuild the bundle into `wwwroot`.
- Mark #10–13 "Done (unreleased)" in the tracker.
- Update both READMEs ("Working with query results").
- No version bump unless the user asks.
