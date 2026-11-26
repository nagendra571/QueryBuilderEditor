# Results grid, phase 4: formats, highlights and a totals row (design)

Date: 2026-11-26. Status: draft for approval.

- **Decisions so far.** The user chose **session-only** settings (nothing saved; saving is #17) and option **B**: a
  **Format** tab in the existing right-hand panel. They said "so far so good" to the mockup and to the calls listed under
  "My calls" in it.
- **Feature list and tracking.** `docs/superpowers/specs/2026-11-25-results-panel-features.md`, items #9 (totals row),
  #14 (conditional formatting) and #15 (number and date formats).
- **Earlier phases still bind.** Phase 1 `2026-11-25-results-grid-foundation-design.md`, phase 2
  `2026-11-26-results-details-panel-design.md`, phase 3 `2026-11-26-results-filters-design.md`.
- **Mockup.** `docs/superpowers/specs/assets/2026-11-26-results-formatting-mockup.html`.

## Intent

Business users read the Results grid before they chart it. Raw cells make that hard: dates show as
`2026-11-03T00:00:00`, money as `12400.5`, and nothing stands out. Phase 4 lets one person tidy the grid **for their own
reading, right now**: per-column number and date formats, in-cell bars, colour scales and highlight rules, and an optional
totals row. Nothing is saved; a later phase (#17) decides what is worth remembering per saved query.

The point that applies to every phase still holds: the grid holds only the rows the run returned. That count is the data
source's record limit when an admin set one (else the host default, else 1,000; `RecordLimits.GridRows`). Totals, bars
and scales cover the **loaded rows after the grid's filters**, and say so when the result was cut off.

## Goals

1. **Format tab** beside Details and Profile in the right-hand panel. It follows the active column (like Profile) and has
   previous/next column buttons. The header menu gains **Format…**, which opens the panel on this tab for that column.
2. **Number formats** (number columns): Automatic (today's display), Number, Compact, Currency (ISO code), Percent;
   decimals Auto/0/1/2/3; thousands separator on/off; a live "Looks like …" preview using the active cell's value (else
   the first non-empty value).
3. **Date formats** (date and dateTime columns): Automatic (today's raw text), "Tue 3 Nov 2026", "3 Nov 2026", locale
   short date ("03/11/2026" in en-GB), ISO "2026-11-03", "Nov 2026", "2026". dateTime columns add **Show time** (hh:mm,
   locale 12/24-hour).
4. **Bars in cells** (number columns): a bar behind the number sized to the value.
5. **Colour scale** (number columns): two palettes, "white → blue" and "red → white → green".
6. **Highlight rules** (every type except boolean): up to 5 per column, first match wins, four colours (red, amber, green,
   blue). Conditions:
   - numbers: greater than, less than, between (inclusive), equals;
   - text, guid, unknown: is, contains (case-insensitive), is empty;
   - date, dateTime: before, after, between (inclusive, by day).
7. **Totals row** (#9): **Σ Totals** toggle in the toolbar shows one row stuck to the bottom of the grid. Each column has a
   function; clicking a totals cell opens a menu to change it. The same choice sits in the Format tab ("Total") for
   keyboard users.
8. **Formatted text everywhere a cell value is shown to read**: grid cells, the Row tab of the Details panel, the Column
   profile's min/max/average/top values, the totals row, and auto-fit widths.
9. A **blue dot** in a column header marks a column with any format, bars, scale or rule.

## Non-goals

- Saving any of this (#17).
- Whole-row highlighting.
- Formats for text, guid or boolean columns (booleans keep their ✓/✕ icons).
- Formatted text in filter popovers, filter chips and the search box (they keep raw values this phase).
- Changing export, charts or the server. Client-only.
- Custom format strings ("#,##0.00;[Red]…"), custom colours, icon sets.
- Group subtotals (#22).

## Anti-goals (a failure even if it works)

- **A total that looks query-wide but isn't.** When the result was cut off, the yellow note under the totals row is
  mandatory: "Totals cover the first N loaded rows. Export and charts use all matching rows." (The run response can't
  tell an admin limit from the default cap, so the note doesn't name the source; see "Settled after the builder check".)
- **A total that is arithmetically meaningless.** Summing a column that is itself an average, min, max or distinct count
  (the query's `aggregate` is `avg`, `min`, `max` or `countDistinct`) is not offered — same rule as pivot totals.
- **A format that changes the value.** Copy, sorting, filtering, search and Add to query keep using raw values. A
  formatted cell's title (tooltip) shows the raw value when the two differ.
- **A date shown a day off.** Zoneless database date text is the database's wall-clock time; format it in UTC exactly as
  `parseDateValue` reads it, never in the viewer's zone.
- **Colour carrying meaning alone, or failing contrast.** Highlights tint the background; text stays readable (≥ 4.5:1)
  in both themes. A highlighted cell's rule is named in its tooltip ("Amount > 15,000").
- **Losing earlier behaviour.** Selection styling stays visible over highlights; keyboard navigation, Esc order, full
  screen, re-run carry-over, render isolation, copy on http, and arrow-key cost with 64 columns must not regress.
- **Breaking the design rules.** No shadows at rest; yellow only for the cut-off note; Barlow (`font-numeric`) for numbers
  in the totals row and rule values.

## Constraints

- Client-only; no API, schema or package change; no new dependency.
- Pure logic in `.ts` files with node tests (`import type` for `@/` imports, relative `.ts` runtime imports).
- No `navigator.clipboard`/`crypto.*` in `client/src`.
- Settings live in `GridView` (session state). Phase 1's `carryOver` rule applies unchanged: same column set keeps the
  whole view (formats, rules, totals included); a different set resets everything. "Reset view" clears them, and
  `isDefaultView` counts them.
- Zustand/`useSyncExternalStore` selectors never return new objects.
- Chart number formats live in `features/visualizations/format.ts` (`ValueFormat`: number/compact/currency/percent,
  decimals, currency; percent treats 42 as 42%). The grid's number output must match it for the same settings.

## Approaches

- **Chosen (user): B, a Format tab in the right-hand panel.** It follows the active column, keeps the grid visible while
  formatting, has room for several rules, and reuses phase 2's docking, overlay mode and Esc handling.
- **Rejected: A, a popover from the header menu.** It covers the cells being formatted and has little room for rules.
- **Rejected: C, one rules dialog for the whole grid.** Modal, so users edit blind; heavy for business users.

Implementation approach (designer's call): one pure module compiles a column's settings into a cell formatter and a cell
decorator once per (rows, settings) — the same compile-and-cache pattern as phase 3's filter matchers — so rendering a
cell is a lookup, not a recomputation.

## Decided behaviour

### Model (pure)

`GridView` gains:

- `formats: Record<string, ColumnFormat>` keyed by column name. `ColumnFormat` holds `number?` (style, decimals,
  currency, thousands, `fraction` for percent), `date?` (pattern, showTime), `bars?: boolean`,
  `scale?: 'blue' | 'redGreen' | null`, `rules: HighlightRule[]` (≤ 5).
- `totals: { on: boolean; fns: Record<string, TotalFn> }`, `TotalFn` = `sum | avg | count | distinct | min | max |
  earliest | latest | none`.

Percent: 42 means 42% (as in charts). A **"Values are fractions (0.42 = 42%)"** switch sets `fraction`. Grid numbers use
the viewer's locale via `Intl`; a malformed currency code falls back to a plain number, as in charts.

### Cells

- A cell whose value isn't usable by its format (text in a number column, e.g. PostgreSQL huge `numeric` or `money`,
  or an unreadable date) shows its raw text and takes no bar, scale or number rule.
- **Bars:** drawn behind the text in the primary colour at low strength. All values ≥ 0: from zero to the largest value.
  Any negative: a zero line placed by the range, negatives grow left in red at low strength.
- **Colour scale:** min → max of the column's loaded, filtered numbers. Three-colour scale: midpoint is the median.
  Strength is capped so text keeps contrast in both themes.
- **Rules** paint over the scale. First match wins. Rule value inputs: numbers typed as plain numbers; dates via a date
  input. An incomplete rule (empty value) is kept in the list but matches nothing and shows "Incomplete".
- Selected and active cells keep phase 1's selection styling on top of all of this.

### Totals row

- Toggled by **Σ Totals**; stuck to the bottom of the scroll area above the footer; scrolls horizontally with the grid;
  pinned columns stay pinned; the row-number cell shows Σ.
- Defaults: number → Sum (but None when the column's aggregate is avg/min/max/countDistinct); date/dateTime → Latest;
  text, guid, unknown → Count; boolean → Count. Count = non-empty values.
- Offered: numbers sum, avg, count, min, max, none (no sum for the aggregate columns above); dates earliest, latest,
  count, none; others count, distinct, none.
- Values use the column's format; sums and averages are rounded like phase 2's selection totals (no float noise).
  The label (SUM, AVG, …) is shown before the value.
- Computed over the grid's filtered rows (search included, matching the footer count). Cut-off note as in Anti-goals.
- Totals cells are not part of the cell selection or copy. They are buttons (mouse), `tabIndex=-1`; the keyboard path is
  the Format tab's "Total" select.

### Format tab

- Tab order: Details, Profile, Format. Header row: type badge, column name, previous/next column buttons.
- Sections by type: Number format / Date format; Bars in cells and Colour scale (numbers); Highlight rules; Total (only
  while the totals row is on — otherwise one line "Turn on Σ Totals to show totals").
- Each section has Reset; the tab ends with "Clear formatting for this column".
- Booleans: "Nothing to format for Yes/No columns" plus the Total section.
- Changes apply as you type/select (no Apply button); invalid input (e.g. a currency code that isn't three letters) is
  flagged inline and ignored.

## My calls (shown to the user in the mockup)

- Copy (Ctrl+C) copies raw values; export is unchanged; filters, sorting and search use raw values.
- Rules colour the cell only.
- Chart number formats are reused so a column reads the same in the grid and in a chart.

## Settled after the builder check (designer's calls)

1. **Where the aggregate comes from.** Run results carry only name and type. The grid maps each result column back to the
   query's visible columns by `alias || columnName`, as `filters-to-query.ts` does, and reads its `aggregate` from there.
   Where `ResultsTable` has no `queryDefinition` (the drill page, which shows plain underlying rows) every column counts as
   plain, so Sum is offered. No server change.
2. **Cut-off note wording.** "Totals cover the first N loaded rows. Export and charts use all matching rows." No claim
   about where N comes from: the response has `rowLimit` and `truncated` only, and guessing from the number would be wrong
   when an admin sets exactly 1,000.
3. **Zoned date-times** (`Z` or an offset, e.g. PostgreSQL `timestamptz`) are instants, so they show in the **viewer's**
   zone. Zoneless text is the database's wall-clock time and shows as written (formatted in UTC). Date rules ("before",
   "between by day") compare the same calendar day the cell shows.
4. **Date patterns follow the viewer's locale** through `Intl` styles: weekday + date, medium date, short date, month +
   year, year; ISO is fixed. The mockup's labels are en-GB examples. The picker shows each option as the active value
   formatted that way. Tests pin a locale.
5. **Fraction percents.** With "Values are fractions" on, rule values are typed as raw values (0.15), and the rule editor
   shows the formatted equivalent next to the input ("= 15%"). Rule tooltips show formatted values.

## Left to the builder

- File and component names. Suggested: `grid-format.ts` (pure: types, number/date formatting, compile, rule matching,
  scale/bar maths), `grid-totals.ts` (pure), `FormatPanel.tsx`, `RuleEditor.tsx`, `TotalsRow.tsx`.
- Whether `grid-format.ts` calls `formatValue` from `visualizations/format.ts` or shares a lower-level helper with it.
- Exact colour tokens for the four highlight colours and the scales (from the design tokens, checked for contrast in both
  themes).

## Tests and acceptance

**Node tests** on the pure files: every number style and decimals choice (including fraction percent, bad currency,
negatives, text in a number column); every date pattern for date, zoneless dateTime, zoned dateTime and unreadable text,
under `TZ=Asia/Kolkata` and `America/New_York`; rule matching for every condition including incomplete rules and first
match wins; bar extents with and without negatives; scale positions and median midpoint; totals for every function,
defaults per type and aggregate, rounding; `carryOver`/`isDefaultView` with formats and totals.

**Browser acceptance** (Opus, Playwright, sample app with a temporary 2,500-row view and a temporary record limit to
force a cut-off, both removed afterwards): Format tab follows the column; header "Format…"; every format visibly applied;
bars, scale and rules; totals row with filters, pinned columns, horizontal scroll and the cut-off note; Row tab and profile
show formatted text; copy still raw; Reset view; full screen; http routed origin; both themes; scroll and arrow-key cost
with 64 columns and formatting on; 100,000 loaded rows with totals and a scale stay responsive.

## Shipping

- Client-only. Rebuild the bundle into `wwwroot`.
- Mark #9, #14, #15 "Done (unreleased)" in the tracker.
- Update both READMEs ("Working with query results").
- No version bump unless the user asks.
