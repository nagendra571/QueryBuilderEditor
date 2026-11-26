# Results grid, phase 2: details panel and selection totals (design)

Date: 2026-11-26. Status: draft for approval (builder check done). The user picked option A, "Inspector that follows you", and said "so far
so good" to the playback below.

Feature list and tracking: `docs/superpowers/specs/2026-11-25-results-panel-features.md`. This phase covers items #6
(record panel), #7 (column profile) and #8 (selection totals).

Phase 1 spec: `docs/superpowers/specs/2026-11-25-results-grid-foundation-design.md`. Its constraints all still apply.

Mockup: `docs/superpowers/specs/assets/2026-11-26-results-details-panel-mockup.html`. This is a committed copy of the
brainstorm mockup. Option A is the approved one; option B was not chosen.

## Intent

Phase 1 made the grid itself solid. Business users still have no quick way to do three things:

- read one record comfortably, especially a wide row or long text;
- understand a column (its range, the common values, how much is empty);
- add up what they selected.

Excel, Power BI, Tableau, Metabase and Hex each offer these, and users expect them before they start charting. Phase 2
adds one docked **Details** panel that follows the cell you are on, plus totals for the selection in the footer.

## Goals

1. **Details panel.** A toolbar toggle opens a panel docked on the right, with **Row** and **Column** tabs. Both tabs
   follow the active cell.
2. **Row tab.** Shows every visible field of the active row, with Previous/Next and Copy record.
3. **Column tab.** Shows a profile of the active column that depends on its type. It is computed from the loaded rows
   and says so.
4. **Selection totals.** When two or more cells are selected, the footer shows Count, Sum, Average, Min and Max.

## Non-goals (later phases)

- Clicking a top value or histogram bar to filter. That is phase 3, #10–13.
- The server-side "Calculate for all rows" profile. A placeholder is shown, but it does nothing.
- Editing values.
- Remembering whether the panel is open across visits. That is #17.
- Number and date formats. That is phase 4; the panel shows values the way the grid shows them today.
- Totals row (#9).

## Anti-goals (a failure even if it works)

- **A profile that looks query-wide but covers only the loaded rows.** When results were cut off, the yellow note is
  mandatory.
- **A panel that slows scrolling or arrow-key movement.** Profiles are memoised per column per run. The row tab renders
  at most the visible columns.
- **Losing phase 1 behaviour.** That covers keyboard operation, Esc handling, full screen, re-run carry-over,
  copy-on-http and screen-reader semantics.
- **Breaking the design rules.** Shadows, decorative colour, or yellow used for anything other than the warning.
- **Making every arrow press noisy for screen readers.** Do not announce each change through a live region.

## Constraints

Everything in phase 1's Constraints still applies:

- client-only;
- no `navigator.clipboard` or `crypto.*` in `client/src`;
- Zoning Map tokens: `--row-h`, Barlow `font-numeric` for numbers, the Flat Map Rule, primary blue for focus and
  selection;
- no new dependencies;
- pure logic in `.ts` files under `features/builder/results/` with node tests.

Rows are client-side. Up to 1,000 by default, but a data-source record limit can allow up to 100,000. Profiles must
stay fast at 100,000 rows: compute on demand for the active column only, then memoise.

Selection model today (`selection.ts`, `useGridSelection.ts`):

- `CellRange {anchor, focus}`; the active cell is `focus`.
- Indexes are display positions (after search and sort, over visible columns).
- The selection is dropped when search, sort, order, visibility or pinning changes, and on a new run.
- Only a cell count reaches `ResultsTable` (`onSelectionChange(cells)`).

## Approaches

The approach was settled during the conversation; the alternatives below were considered and rejected.

- **Chosen: one docked "inspector" panel** (option A). It has Row and Column tabs, and both follow the active cell.
  This is the most "work in it" feel, and it matches Excel/Power BI/Hex habits. It costs one more toolbar control and a
  narrower grid while it is open.
- **Rejected: open on demand** (option B). You open a record from the row-number cell or a profile from the column menu,
  and the panel shows that one thing until you close it. It is simpler, but you can't arrow through records or columns,
  so it is less useful for scanning.
- **Rejected for the profile data source: server statistics now.** The user chose C: loaded rows now, with a labelled
  "Calculate for all rows" server option in a later phase.

**Build approach (my call):** state stays client-side in the existing grid; no new dependencies.

- Histograms are plain divs or inline SVG, not Recharts. They are tiny, and keeping them out of Recharts avoids its
  animation and bundle weight in the grid.
- Statistics are pure functions in `results/column-profile.ts` and `results/selection-totals.ts`.

## Decided behaviour

### Panel

- **Toggle.** A "Details" toggle button in the toolbar, next to Wrap text. It has `aria-pressed` and a lucide
  `PanelRight` icon.
- **Opening.** The panel opens docked on the right of the scroll area, between the toolbar and the footer. The toolbar
  and footer keep the full width.
- **Width.** It starts at 340px and can be dragged on its left edge between 280px and 560px, never more than 50% of the
  grid's width. A keyboard alternative is required: the handle is focusable, and ←/→ resize it by 16px.
- **Closing.** It closes with its ✕ button, the toolbar toggle, or Esc while focus is inside the panel. Focus then
  returns to the grid's active cell, or to the toggle if there is no active cell.
- **Full screen.** It appears there too, in the same state.
- **Re-runs.** Open/closed state, tab and width survive re-runs and full screen within the session, held where phase 1
  holds `GridMemory`/view state. Nothing persists across visits.
- **Narrow hosts (my call).** When the grid area is narrower than 900px, the panel overlays the grid's right side instead
  of docking. It looks the same, has a 1px border, and has no shadow, per the Flat Map Rule.
- **Semantics.** The panel is a `<section aria-label="Details">`. The tabs are a Radix Tabs list ("Row", "Column").
- **No active cell.** Each tab shows a short hint: "Select a cell to see its row." or "Select a cell to profile its
  column." The tabs never show a stale record.

This also happens after sort or search clears the selection. That follows phase 1, where those changes drop the
selection. The panel does not try to keep the old row.

### Row tab

- **Header.** Barlow label "ROW 12 OF 1,000". The number is the 1-based display position; the total is the displayed
  row count (after search).
- **Previous/Next.** The buttons move the active cell up or down one row. They keep the column, scroll the row into
  view, and are disabled at the ends. The selection collapses to that one cell. Keyboard shortcuts are not added: arrows
  in the grid already do this.
- **Fields.** The body lists every **visible** column in grid display order (pinned left, centre, pinned right): name
  above or beside value, with the type badge.
  - Values use the grid's cell rendering rules. Numbers are right-aligned in Barlow. Long text wraps in full, with no
    line clamp; the panel scrolls.
  - null shows as a muted "empty". Booleans and dates show the way the grid shows them.
- **Active field.** It is marked with the selection tint, so it is clear which column you are in.
- **Copy record.** Copies two-column TSV: a header `Column\tValue`, then one line per visible field, serialised with
  phase 1's TSV rules. It uses `copyText`, and the toast says "Copied record".
- **Drill.** When `onRowClick` is provided, there is also a "See these rows" button, labelled `rowActionLabel`, that
  calls it for this row.

### Column tab

**Header:** the column name, its type badge, and a coverage line.

- **Not truncated:** plain muted text, "Based on all 340 rows".
- **Truncated:**
  - A yellow warning note: "Based on the first 1,000 rows. The query has more."
  - Below it, a disabled dashed placeholder button: "Calculate for all rows · coming later". It must not look broken.

**Scope.** Profiles cover **all loaded rows**, ignoring the search box and sort. This is my call, and it is stated in the
coverage line: a profile describes the result, not the current search. Values are the raw cell values.

**Shared, for every type:**

- **Empty:** a count and a meter, showing what share of rows is null, plus empty string for text.
- **Distinct:** the count of distinct non-empty values.

**By the column's `dataType`** (use the same type grouping as phase 1's badges):

- **Number:**
  - Tiles: Min, Average, Max, Sum, Median.
  - A histogram of about 20 equal-width bins between min and max, with a single bar if min = max.
  - Sum is hidden for aggregate columns whose aggregate is Avg/Min/Max/Count Distinct (my call), because adding
    averages is misleading. It is shown for Sum/Count aggregates and plain numbers. Average is computed over non-empty
    values.
- **Date / DateTime:**
  - Tiles: Earliest, Latest.
  - A histogram over time, about 20 equal-width bins.
  - Values that don't parse as dates count as "unreadable" and are shown if any exist.
- **Text** (and anything else):
  - Tiles: Distinct, Shortest length, Longest length.
  - The top 10 values by count, each with a bar (count / rows) and its count. Ties are broken alphabetically.
  - An "Other values" line gives the remainder.
  - Long values are truncated with a title.
- **Boolean:** a Yes / No / Empty split, as three bars with counts.

**Histograms.**

- Bars use the primary blue at reduced opacity; no gradients or animation.
- Each bar has a title or tooltip with its range and count.
- The chart has an accessible text alternative, e.g. "Histogram of FeePaid, 20 bins, peak 12.40–24.80 (93 rows)".

### Selection totals (footer)

**When they show.** When the selection covers two or more cells, the footer's right side shows Barlow items, in this
order and only when they apply:

- **Count:** the number of non-empty cells. It is always shown when there are two or more cells.
- **Sum, Average, Min, Max:** over the cells whose column is numeric and whose value is a finite number. These four
  appear only if at least one such cell exists.

Text-only selections show Count only. The existing "8 cells selected" stays.

**Format.** Numbers use `toLocaleString` with up to the input's max decimals, capped at 4 for Average.

**Copy a total (my call).** Each total is a button; clicking it copies the raw, unformatted number. The toast says
"Copied Sum".

**Performance.** Totals compute over the selected range only, memoised on the range plus the run's data.

**Select all.** Totals for a select-all on 100,000 rows × many columns must not freeze the UI. Compute in one pass over
numeric columns only. If the range is larger than 1,000,000 cells, show Count only, with title "Totals skipped for very
large selections" (my call).

## Settled calls (designer's calls unless marked)

- The active cell, the range and the displayed rows must reach `ResultsTable`/the panel. Today only a count does.
  Extend `onSelectionChange` (or lift the state) to pass `{ range, active }`. Phase 1's memoised `GridRow` slices must
  stay intact, so the panel must not cause every row to re-render on each arrow press.
- Prev/Next needs a way for the panel to set the grid's active cell and reveal it. Expose a small imperative handle or a
  callback from `ResultsGrid`.
- The row tab reads its field list from the grid's current visible, ordered columns, not from `result.columns`.

## Settled after the builder check (designer's calls)

1. **Selection state reaches the panel through a small external store, not React state in `ResultsTable`.**
   `useGridSelection` publishes `{ range, active }` to a store object that `ResultsTable` creates once, for example
   `createSelectionStore()` in `results/selection-store.ts`. The pattern is a plain subscribe/getSnapshot pair. The
   panel and the footer totals read it with `useSyncExternalStore`, and their selectors return primitives or the stored
   object itself, never a new object (CLAUDE.md Zustand rule). Moving the cursor must not re-render `ResultsTable` or
   `ResultsGrid`.
   - **Acceptance check:** React Profiler, or a render counter, shows that an arrow press re-renders only the two
     affected `GridRow`s plus the panel and footer.
   - The panel also needs the displayed rows and visible ordered columns. `ResultsGrid` publishes those into the same
     store whenever they change. They change on sort, search, visibility or a run, not on cursor moves.
2. **Prev/Next uses an imperative handle.** Extend the existing `copyRef` pattern into a `gridRef`:
   `{ copy(), moveTo(pos: CellPos), focus() }`. `moveTo` collapses the selection to `pos`, reveals it (the existing
   `reveal`, which scrolls virtualised rows into view) and does not take focus. Prev/Next therefore work while focus is
   in the panel, and focus stays on the button.
3. **Panel state is stored separately from both `GridMemory` and `GridView`.** Open/closed state, tab and width go in a
   separate `PanelState` held in `ResultsTable` state, which already survives the full-screen portal because the portal
   is inside `ResultsTable`. "Reset view" does not touch it. A new run keeps it.
4. **Layout.** The toolbar and the truncation banner span the full width. Below them, a row holds the scroll area
   (`min-w-0 flex-1`) and the panel. The footer spans the full width.
   - Opening or closing the panel changes the grid's width, but it does **not** re-fit columns. Widths are kept, and the
     grid simply scrolls horizontally more.
   - Centre-column virtualisation must use the new container width. Its existing ResizeObserver or measurement should
     handle this; verify it.
5. **Type branches.** `ColumnDataType` is `'text' | 'number' | 'date' | 'dateTime' | 'boolean' | 'guid' | 'unknown'`.
   - number → Number;
   - date and dateTime → Date;
   - boolean → Boolean;
   - text, guid and unknown → Text.
   In a number column, text that parses as a number (PostgreSQL numeric text such as `"12.50"`) counts as a number, via
   the existing `toNumber` from `chart-data.ts`. Other text (e.g. PostgreSQL `money` such as `"$1,234.00"`) counts as
   "unreadable" and is left out of Min/Max/Sum/Average, like dates. `QueryResultColumnDto.aggregate` exists client-side and drives the Sum-hiding rule.
6. **Parsing.**
   - Numbers: `toNumber(v)` returns a number (finite numbers and numeric strings).
   - Dates: a string that `Date.parse` accepts, or a `Date`. This is the same rule `compare-cells.ts` uses; reuse its
     helper if it exports one.
   - Raw values come from `result.rows`. The Row tab shows them with the grid's own cell renderer or formatting.
7. **Esc.** Esc inside the panel closes it and calls `preventDefault`, so the full-screen listener ignores that press. A
   second Esc exits full screen.
8. **Copy record** copies the active row's visible fields in display order. It ignores the range; only the active row
   counts.

## Left to the builder

- Component and file names: e.g. `DetailsPanel.tsx`, `RowDetails.tsx`, `ColumnProfile.tsx`, `Histogram.tsx`,
  `SelectionTotals.tsx`, plus pure `column-profile.ts` and `selection-totals.ts` with node tests.
- Exact spacing and type sizes within the tokens.
- The median algorithm. Sort a copy; it is fine at 100,000 numbers for one column.
- How the panel width is stored.

## Tests and acceptance

**Node unit tests:**

- profile maths for each type: empty and null handling, a single value, min = max, unreadable dates, ties in top
  values;
- selection totals: mixed text and number, all empty, the large-range skip.

**Browser acceptance** (Opus, via Playwright on the sample app, with the phase 1 temporary views recreated and then
removed):

- the panel follows the cell when arrowing and clicking;
- Prev/Next work;
- Esc closes and focus returns;
- full screen works with the panel open;
- the docked and overlay widths work;
- Copy record works on http (the `qb.test` route);
- the truncated and untruncated notes appear correctly;
- footer totals are right;
- both themes and both densities;
- scroll performance with the panel open is no worse than phase 1 (64-column view).

## Shipping

Client-only. Rebuild the bundle and copy it to `wwwroot`. Mark #6–8 "Done (unreleased)" in the tracker. Update both
READMEs ("Working with query results"). There is no version bump unless the user asks.
