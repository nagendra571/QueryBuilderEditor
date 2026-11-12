# More visualization types — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Area, Horizontal bar, 100% stacked, Combo, Table, Pivot (with shading), Funnel, Waterfall, Scatter/bubble, Counter target, Trend and per-chart number formats.

**Architecture:** All types keep using `RenderVisualizationCommand`; the server work is one contract, one validator and one row-check class. On the client, pure model functions live in plain `.ts` files tested with Node's built-in runner, and each new visual family gets its own view file, dispatched from `ChartRenderer`.

**Tech Stack:** .NET 8, xUnit; React, TypeScript, Recharts 3 (already installed); `node --test` (Node 24 strips types natively).

**Spec:** `docs/redash-parity/features/visualization-types/SPEC.md`

## Global Constraints

- **No migration, no new npm or NuGet package** (AC-K2, AC-K3). `Type` stays `nvarchar(20)`; `HorizontalBar` (13) is the longest name.
- `VisualizationOptions.CurrentVersion` stays 1. New fields are optional, so charts saved today load unchanged.
- **Never commit, push, bump the version or pack.** `CLAUDE.md` requires a separate ask for each. This overrides the usual per-task commit step. The work stays uncommitted on `feature/rea1`.
- Cell and label values render as React text only. No `dangerouslySetInnerHTML`, images or links from data.
- Missing values stay missing: never coerced to 0.
- Never two Y axes.
- No "what" comments; only "why" for non-obvious constraints (`CLAUDE.md`).
- Zustand selectors must not return new arrays or objects; `truncate` on a flex child needs `min-w-0` (`CLAUDE.md`).
- Use the existing Zoning Map tokens (`var(--chart-N)`, `map-label`, `font-numeric`, `border-border`). Read `DESIGN.md` before writing a view.
- Pure model files (`chart-data.ts`, `format.ts`, `pivot-model.ts`, `story-models.ts`) use only `import type` from `@/…` and relative runtime imports **with the `.ts` extension**, so Node can run them. `allowImportingTsExtensions` is already on.
- Do not start the app or touch a database. Runtime checks are done by the reviewer in Task 10.

## Review Focus

1. A column named `constructor`, `__proto__` or `a.b` used as a pivot row, column or value: must work like any other name. Pinned in Task 6 (use `Map`, positional keys).
2. Typing a partial currency code such as `US` in the editor: `Intl.NumberFormat` throws `RangeError`, which would blank the dialog. Must fall back to plain numbers. Pinned in Task 3.
3. Switching chart type in the editor with roles left over from the last type (text X on Scatter, a `lineMeasures` entry no longer in `measures`): shows the problem message, no crash, and no stale column is saved. Pinned in Task 4.
4. Editor preview uses the results grid, which can itself be truncated: Pivot and Funnel totals from partial rows must be labelled as a partial preview. Pinned in Task 6.
5. The smallest dashboard tile (2 wide, 2 rows) with Table, Pivot or Trend: content scrolls or shrinks and never overflows the tile. Checked in Task 10.

---

## Wave 0: Server

### Task 1: Contract and validator

**Files:**
- Modify: `src/QueryBuilder.Domain/Enums/Enums.cs` (lines 140-146), `src/QueryBuilder.Domain/Model/VisualizationOptions.cs`, `src/QueryBuilder.Application/Visualizations/VisualizationOptionsValidator.cs`
- Test: `tests/QueryBuilder.Tests/VisualizationOptionsValidatorTests.cs`

**Interfaces:**
- Produces:
  - `VisualizationType`: append `Area = 4, HorizontalBar = 5, Combo = 6, Scatter = 7, Table = 8, Pivot = 9, Funnel = 10, Waterfall = 11, Trend = 12`.
  - `enum ValueFormatStyle { Number = 0, Compact = 1, Currency = 2, Percent = 3 }`
  - `class ValueFormat { ValueFormatStyle Style; int? Decimals; string? Currency; }`
  - `class TableOptions { List<string> Columns = []; }`
  - `class PivotOptions { List<string> Rows = []; string? Column; List<string> Values = []; bool Totals = true; bool Shade; }`
  - On `VisualizationOptions`: `bool Percent; List<string> LineMeasures = []; string? Size; string? Label; TableOptions? Table; PivotOptions? Pivot; ValueFormat? Format;`
  - On `CounterOptions`: `string? Target;`
  - Constants on the validator: `MaxTableColumns = 30`, `MaxPivotRows = 3`, `MaxPivotValues = 5`, `MaxPivotColumns = 50`, `MaxFunnelSteps = 20`, `MaxWaterfallBars = 30`.

- [ ] **Step 1: Write the failing tests**, using the fixture columns (`Region`, `Channel` text; `Total`, `Orders`, `Avg1` numbers):

```csharp
[Theory] // each is valid
// Area/HorizontalBar X+measures; stacked+percent on both; Combo Total+Orders with LineMeasures [Orders];
// Table [Region, Total]; Pivot Rows [Region], Column Channel, Values [Total]; Funnel/Waterfall one measure.
```

  Named cases, each asserting the issue exists:
  - `Percent_without_stacked_is_rejected`; `Line_still_cannot_be_stacked`
  - `Combo_needs_at_least_one_line_and_one_bar` (LineMeasures empty, and all measures); `Combo_line_measure_must_be_a_measure`; `Combo_cannot_have_a_series`
  - `Scatter_x_must_be_number_or_date` (X = `Region`); `Scatter_size_must_be_number`
  - `Funnel_waterfall_and_trend_need_exactly_one_measure_and_no_series`
  - `Trend_x_must_be_number_or_date`
  - `Table_needs_1_to_30_columns`; `Table_column_must_be_visible` (`HiddenNote` gives `MissingColumn`)
  - `Pivot_limits` (0 or 4 rows; 2 columns not expressible; 0 or 6 values); `Pivot_value_must_be_number`; `Pivot_column_cannot_have_two_roles`
  - `Counter_target_must_be_number`
  - `Currency_style_needs_three_uppercase_letters` (`us`, `US`, null rejected, `USD` accepted); `Format_decimals_are_0_to_4`
  - `ReferencedColumns_lists_every_role` as a `[Theory]`, one row per type, including `size`, `label`, `table.columns`, `pivot.rows/column/values`, `counter.target`, `lineMeasures`.
  - `Options_saved_before_this_work_still_validate`: deserialize `{"version":1,"x":"Region","measures":["Total"],"stacked":false,"sortX":"none"}`, no issues.

- [ ] **Step 2: Run:** `dotnet test tests/QueryBuilder.Tests --filter VisualizationOptionsValidatorTests`. Expected: compile failure on the new members.

- [ ] **Step 3: Add the contract and extend `Validate` and `ReferencedColumns`.** Branch early for Counter, Table and Pivot; the remainder share the X + measures path. Stacking is allowed for Bar, HorizontalBar and Area. Date-or-number means `ColumnDataType.Number`, `Date` or `DateTime`. Validate `Format` before the type branch, so it applies to all. Keep every existing message unchanged, since tests and the UI depend on them.

- [ ] **Step 4: Run:** `dotnet test tests/QueryBuilder.Tests`. Expected: all pass, including the existing suite.

### Task 2: Render path

**Files:**
- Create: `src/QueryBuilder.Application/Visualizations/VisualizationDataChecks.cs`
- Modify: `src/QueryBuilder.Application/Visualizations/RenderVisualizationCommand.cs`, `src/QueryBuilder.Application/Dtos/VisualizationDtos.cs`, `src/QueryBuilder.Application/Dtos/SavedQueryDtos.cs:77`
- Test: `tests/QueryBuilder.Tests/RenderVisualizationCommandTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `QueryResultColumnDto(string Name, ColumnDataType DataType, AggregateFunction? Aggregate = null)`; run and export leave it null.
  - `VisualizationDataDto(..., int RowLimit, bool Truncated = false)`
  - `static void VisualizationDataChecks.Check(VisualizationType type, VisualizationOptions options, List<Dictionary<string, object?>> rows)`, throwing `CatalogValidationException`. `CheckPie` moves here unchanged.

- [ ] **Step 1: Write the failing tests:**
  - `Table_over_the_cap_returns_rows_and_truncated_flag` (Truncated result: no throw, `data.Truncated == true`).
  - `Pivot_over_the_cap_is_refused`; `Bar_over_the_cap_is_still_refused`.
  - `Pivot_with_51_distinct_columns_is_refused` (message contains `50`); 50 passes.
  - `Funnel_with_21_steps_is_refused`; `Funnel_with_a_negative_value_is_refused`.
  - `Waterfall_with_31_bars_is_refused`; a negative amount passes.
  - `Scatter_with_negative_size_is_refused`.
  - `Render_projects_only_each_types_role_columns` (`[Theory]`, one row per new type; `HiddenNote` and unused columns never in any row).
  - `Counter_with_target_returns_one_row`; `Trend_returns_all_rows`.
  - `Columns_carry_the_aggregate_of_the_definition` (`Total` = Sum, `Avg1` = Avg, `Region` = None).
  - `Scope_predicates_reach_build_for_every_new_type` (`[Theory]`: `f.Guard.Predicates` set, `f.Builders.ReceivedScope` same).
  - `Removed_column_in_any_role_says_needs_updating` (`[Theory]` over `size`, `label`, table column, pivot row, pivot value, counter target, with `Message` containing `needs updating`).

- [ ] **Step 2: Run:** `dotnet test tests/QueryBuilder.Tests --filter RenderVisualizationCommandTests`. Expected: failures on the new tests.

- [ ] **Step 3: Implement.** The truncated refusal becomes `if (result.Truncated && visualization.Type != VisualizationType.Table)`. `ProjectColumns` takes the definition, so it can set `Aggregate`, and its numeric check must cover every numeric role (measures, `size`, `pivot.values`, counter value and target). Keep `rows.Take(1)` for Counter. Audit stays as is. Refusal messages:
  - Pivot: `Too many columns for a pivot table (max 50); add filters or choose a different column.`
  - Funnel: `Too many steps for a funnel (max 20).` / `A funnel cannot show negative values.`
  - Waterfall: `Too many bars for a waterfall (max 30); add filters or totals.`
  - Scatter: `Bubble size cannot be negative.`

- [ ] **Step 4: Run:** `dotnet test tests/QueryBuilder.Tests`. Expected: all pass.

---

## Wave A: More chart forms

### Task 3: Test runner, client contract, number formats

**Files:**
- Create: `client/src/features/visualizations/format.ts`, `client/tests/format.test.ts`
- Modify: `client/package.json` (`scripts.test`), `client/src/types/index.ts` (lines 402-453, line 279), `client/src/features/visualizations/chart-data.ts`, `ChartRenderer.tsx`, `ChartPanel.tsx`, `client/src/features/dashboards/ChartTile.tsx:181`

**Interfaces:**
- Consumes: Tasks 1 and 2, mirrored in camelCase with string unions.
- Produces:
  - `"test": "node --test tests/"` in `package.json`. Tests sit outside `src`, so `tsc -b` ignores them, and import with the `.ts` extension.
  - `formatValue(value: number | null, format?: ValueFormat | null): string`, and `formatAxis(value: number, format?: ValueFormat | null): string`.
  - `ChartRenderer` gains optional props `columns?: { name: string; aggregate?: AggregateFunction | null }[]`, `truncated?: boolean`, `rowLimit?: number`. `ChartPanel` and `ChartTile` pass them from the render response. `OutputColumn` gains `aggregate: AggregateFunction`, and the editor passes that.

- [ ] **Step 1: Write the failing tests** in `format.test.ts` (fix the locale by passing `en-US` through an internal constant, or compare against `Intl.NumberFormat` output):
  - null gives `—` for every style.
  - `currency` with `USD`, decimals 2, on `1000.5` matches `Intl` currency output.
  - **`currency` with `US`, `''` or null falls back to number and does not throw** (Review Focus 2).
  - `percent` on `42` gives `42%`, never `4,200%`.
  - `compact` on `1230974` matches `Intl` compact output.
  - Default with no format: whole numbers no decimals, fractions up to 2, matching current chart behavior.

- [ ] **Step 2: Run:** `cd client && npm test`. Expected: fails, module not found.

- [ ] **Step 3: Implement `format.ts` and the types, and route all number display through it** in tooltip, axis, counter and `DataTable`. `formatNumber` and `formatCompact` stay for existing callers.

- [ ] **Step 4: Run:** `npm test && npm run build`. Expected: pass.

### Task 4: Horizontal bar, area, 100% stacked, combo, duplicate-X warning, editor groups

**Files:**
- Create: `client/tests/chart-data.test.ts`, `client/src/features/visualizations/CartesianView.tsx` (moved out of `ChartRenderer.tsx`), `client/src/features/visualizations/chart-types.ts`
- Modify: `chart-data.ts`, `ChartRenderer.tsx`, `ChartEditorDialog.tsx`

**Interfaces:**
- Produces:
  - `toPercentStack(points, series): points`: each category's values become percent of its total; a zero-total category becomes all null; a missing value stays null.
  - `duplicateXCount(options, rows): number`: rows sharing an X, 0 when `series` is set.
  - `hasStackGap(model): boolean`: true when any point has a null among stacked series.
  - `chart-types.ts`: `TYPE_GROUPS: { label: string; types: VisualizationType[] }[]`, with Compare (bar, horizontalBar, combo, waterfall), Trend (line, area, trend), Part of whole (pie, funnel), Relationship (scatter), Table (table, pivot), Single number (counter). Add a type to the group only in the task that builds it.
  - Extended `optionsProblem`, `referencedColumns`, `TYPE_LABEL`, covering all thirteen types, matching Task 1's rules.

- [ ] **Step 1: Write the failing tests** in `chart-data.test.ts`:
  - `toPercentStack`: 30 and 70 give 30 and 70; 0 and 0 give null, null, with no `NaN`; a null stays null while the rest divide by the non-null total.
  - `duplicateXCount`: two `EU` rows and one `US` gives 2; with `series` gives 0.
  - `hasStackGap`: true and false cases.
  - **`optionsProblem` with leftover roles** (Review Focus 3): Scatter with a text X returns a message; Combo with `lineMeasures: ['Gone']` returns a message; neither throws.
  - `referencedColumns` per type matches the C# `[Theory]` rows.

- [ ] **Step 2: Run:** `npm test`. Expected: fails.

- [ ] **Step 3: Implement the model functions, then the views.** Horizontal bar uses Recharts `layout="vertical"`, with a category `YAxis` truncating long labels at about 24 characters and the full name in the tooltip. Area uses `<Area>` with a dashed outline per series, following the Line rule that series never differ by color alone. Combo uses `ComposedChart` with one `YAxis`. Percent axis uses the 0–100 domain with a `%` tick.

  Footnotes under the chart, same style as the existing "could not be plotted" line:
  - `{n} rows share the same X value. Add a Split by column or a total.`
  - `Some values are missing, so this stack may understate the total.`

  100% stacked over an `avg` or `countDistinct` measure shows this hint in the editor only: `Percentages of averages are rarely meaningful.`

- [ ] **Step 4: Update the editor:** grouped type select using `SelectGroup` and `TYPE_GROUPS`; "Show as 100%" switch under Stacked; per-measure bar or line toggle for Combo; a Format section (style select, decimals, currency code). Roles carry across type switches, and the saved `options` contains only fields valid for the chosen type, so leftovers are never saved.

- [ ] **Step 5: Run:** `npm test && npm run build && npm run lint`. Expected: pass.

---

## Wave B: Tables

### Task 5: Table

**Files:**
- Create: `client/src/features/visualizations/TableView.tsx`
- Modify: `ChartRenderer.tsx`, `ChartEditorDialog.tsx`, `chart-types.ts`

**Interfaces:**
- Consumes: `formatValue`, `formatCategory`, `truncated` and `rowLimit` props from Task 3.
- Produces: `TableView({ options, rows, columns, truncated, rowLimit, fill })`. Page size constant 25.

- [ ] **Step 1: Add a test to `chart-data.test.ts`:** `buildModel('table', …)` returns `{ kind: 'table', columns, rows }` in `options.table.columns` order; 26 rows produce 2 pages through an exported `pageRows(rows, page, size)`; a page past the end clamps to the last page.
- [ ] **Step 2: Run** `npm test`. Expected: fails.
- [ ] **Step 3: Implement.** Sticky header; numbers right-aligned with `tabular-nums`; dates through `formatCategory`. `ChartRenderer` skips `role="img"` and "Show data" for table-kind models (SPEC §3.7). When truncated, show the grid's wording from `ResultsTable.tsx:70`. Editor: an ordered column checklist, with up and down buttons rather than drag, so it works by keyboard.
- [ ] **Step 4: Run** `npm test && npm run build`. Expected: pass.

### Task 6: Pivot with shading

**Files:**
- Create: `client/src/features/visualizations/pivot-model.ts`, `PivotView.tsx`, `client/tests/pivot-model.test.ts`
- Modify: `ChartRenderer.tsx`, `ChartEditorDialog.tsx`, `chart-types.ts`

**Interfaces:**
- Consumes: `aggregate` per column from Task 3.
- Produces: `buildPivot(options, rows, aggregateOf: (name: string) => AggregateFunction | null | undefined): PivotModel`, where

```ts
interface PivotCell { value: number | null; notCombinable: boolean }
interface PivotModel {
  kind: 'pivot'
  rowHeaders: string[][]
  columnHeaders: string[]
  values: string[]
  cells: PivotCell[][][]      // row, column, value
  rowTotals: PivotCell[][] | null
  columnTotals: PivotCell[][] | null
  grandTotals: PivotCell[] | null
  hasNotCombinable: boolean
  valueRanges: { min: number; max: number }[]   // one per value, for shading
}
```

- [ ] **Step 1: Write the failing tests** in `pivot-model.test.ts`, covering SPEC §3.3 exactly:
  - Sum, Count and None: two rows in one cell add up, and totals add up (AC-C1).
  - Min and Max: combine by min and max in cells and totals.
  - Avg and CountDistinct: one row in a cell shows it; two rows give `value: null, notCombinable: true`; **all totals give `notCombinable`**, and `hasNotCombinable` is true.
  - A missing combination is `{ value: null, notCombinable: false }`, not 0.
  - A null measure is ignored, while null row or column values go under `(blank)`.
  - **Columns named `constructor`, `__proto__` and `a.b` in any role work** (Review Focus 1).
  - No `column` set: one implicit column, and column totals null.
  - `totals: false`: all totals null.
  - Headers are sorted with the existing `compareCategories`; `(blank)` is last.
  - 1,000 rows finish in under 200 ms.

- [ ] **Step 2: Run:** `npm test`. Expected: fails.

- [ ] **Step 3: Implement `buildPivot` using `Map`,** keying rows by `JSON.stringify` of the header tuple, so a value containing a separator can't collide.

- [ ] **Step 4: Implement `PivotView`:** real `<table>` with `scope` attributes, repeated row headers merged with `rowSpan`, footnote when `hasNotCombinable`: `Averages and distinct counts can't be added together, so these show —.` Shade switches cell background using `color-mix` of `--chart-1` into `--background`, per value's range; text color stays token-based and the number stays visible (SPEC §3.7). Editor: pickers for rows, column, values; Totals and Shade switches.

- [ ] **Step 5: Partial preview** (Review Focus 4): `ChartEditorDialog` passes `previewResult.truncated` as `truncated`. When truncated on a non-table type, the preview shows above the chart: `Preview uses partial results, so totals may be lower than the saved chart.`

- [ ] **Step 6: Run:** `npm test && npm run build && npm run lint`. Expected: pass.

---

## Wave C: Stand-alone types

### Task 7: Funnel and waterfall

**Files:**
- Create: `client/src/features/visualizations/story-models.ts`, `FunnelView.tsx`, `client/tests/story-models.test.ts`
- Modify: `CartesianView.tsx` or a new `WaterfallView.tsx`, `ChartRenderer.tsx`, `ChartEditorDialog.tsx`, `chart-types.ts`

**Interfaces:**
- Produces:
  - `buildFunnel(options, rows): { kind: 'funnel'; steps: { name: string; value: number | null; pctOfFirst: number | null; pctOfPrevious: number | null }[] }`
  - `buildWaterfall(options, rows): { kind: 'waterfall'; bars: { name: string; base: number; size: number; kind: 'up' | 'down' | 'total'; value: number }[] }`

- [ ] **Step 1: Write the failing tests:**
  - Funnel `100, 50, 25`: `pctOfFirst` 100, 50, 25; `pctOfPrevious` null, 50, 50 (AC-C4).
  - Funnel with a zero previous or zero first step: percent is null, not `Infinity`. A single step works.
  - Funnel keeps query order when `sortX` is `none`, and honors `asc` and `desc`.
  - Waterfall `+10, -3, +5`: final `total` bar value 12 (AC-C3); each bar's base starts where the last ended; a bar crossing zero (`+5, -8`) gives non-negative `size` and a correct `base`.
  - Waterfall skips null amounts and counts them in `skipped`.

- [ ] **Step 2: Run:** `npm test`. Expected: fails.

- [ ] **Step 3: Implement.** Funnel is a real table with columns Step, Value (in-cell bar, number in text), % of first, % of previous, skipping `role="img"` and "Show data". Waterfall is a Recharts stacked bar with a transparent base series; up, down and total are told apart by the label in the tooltip and legend as well as tint, not color alone. Editor: existing X and single-measure roles; default `sortX` is `none`, and Funnel explains that query order is the step order.

- [ ] **Step 4: Run:** `npm test && npm run build`. Expected: pass.

### Task 8: Scatter and bubble

**Files:**
- Create: `client/src/features/visualizations/ScatterView.tsx`
- Modify: `chart-data.ts`, `client/tests/chart-data.test.ts`, `ChartRenderer.tsx`, `ChartEditorDialog.tsx`, `chart-types.ts`

**Interfaces:**
- Produces: `buildScatter(options, rows): { kind: 'scatter'; groups: { name: string; points: { x: number; y: number; size: number | null; label: string | null }[] }[]; skipped: number; xIsDate: boolean }`

- [ ] **Step 1: Write the failing tests:** ISO date X becomes a timestamp with `xIsDate` true; a row with null X or Y is skipped and counted; `series` splits into groups; no series gives one group; a date with no time and a UTC stamp give the same day whatever the machine's time zone, avoiding the time-zone bug Redash's docs mention.
- [ ] **Step 2: Run** `npm test`. Expected: fails.
- [ ] **Step 3: Implement** with Recharts `ScatterChart` and `ZAxis` for size. A different marker shape per group (`circle`, `square`, `triangle`, `diamond`), so groups don't differ by color alone. The tooltip shows `label` when set. No animation over 500 points. Editor: X, Y, optional Size, Label and Split by.
- [ ] **Step 4: Run** `npm test && npm run build`. Expected: pass.

### Task 9: Counter target and Trend

**Files:**
- Create: `client/src/features/visualizations/TrendView.tsx`
- Modify: `story-models.ts`, `client/tests/story-models.test.ts`, `chart-data.ts` (`buildCounter`), `ChartRenderer.tsx`, `ChartEditorDialog.tsx`, `chart-types.ts`

**Interfaces:**
- Produces:
  - `CounterModel` gains `target: number | null; pctOfTarget: number | null`.
  - `buildTrend(options, rows): { kind: 'trend'; latest: number | null; latestLabel: string; previous: number | null; previousLabel: string | null; change: number | null; changePct: number | null; points: { x: string; y: number | null }[]; skipped: number }`

- [ ] **Step 1: Write the failing tests:**
  - Trend with rows out of order sorts by X and uses the latest non-empty value (AC-C5); a null in the latest row falls back to the last non-empty one, and `latestLabel` names that row.
  - `changePct` with a previous of 0 is null; a single row has null `previous` and `change`; all null gives `latest: null`.
  - Date labels stay `YYYY-MM-DD`, via `formatCategory`.
  - Counter: value 82, target 100 gives `pctOfTarget` 82; target 0 or null gives null; no target column behaves as before.

- [ ] **Step 2: Run:** `npm test`. Expected: fails.

- [ ] **Step 3: Implement.** Trend shows the large number, then `▲ 12% vs {previousLabel}` in neutral ink (arrow plus text, never green or red, SPEC §3.5), then a `LineChart` sparkline with no axes, sharing `aria-label`. Counter shows `82% of target` plus a thin bar, hidden when `pctOfTarget` is null. Both shrink to fit a small tile using container-relative sizing rather than the fixed `text-7xl`.

- [ ] **Step 4: Run:** `npm test && npm run build && npm run lint`. Expected: pass.

---

## Wrap-up

### Task 10: Docs, bundle and runtime acceptance

Done by the reviewer in the main session, not a subagent, per the project's working style.

**Files:**
- Create: `docs/redash-parity/features/visualization-types/VERIFICATION.md`
- Modify: `README.md`, `src/QueryBuilder.Editor/README.md`, `docs/redash-parity/ROADMAP.md`, `CLAUDE.md`, `client/` → `src/QueryBuilder.Editor/wwwroot` (bundle copy)

- [ ] **Step 1: Run the static gates:** `dotnet test tests/QueryBuilder.Tests`, `cd client && npm test && npm run build && npm run lint`, and `dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/QueryBuilder.Infrastructure --startup-project src/QueryBuilder.Api` (expected: no changes, AC-K2), and `git diff client/package.json`, showing only the `test` script (AC-K3).
- [ ] **Step 2: Copy the bundle** into `wwwroot` per `CLAUDE.md`, then start the sample app with the existing `TEMP-DATASCOPE-TEST` personas.
- [ ] **Step 3: Playwright, one saved chart per new type** on the local demo data, using a fresh `newContext`, judging by DOM measurements (`CLAUDE.md`). Record each in `VERIFICATION.md`:
  - AC-S1: Super admin and Program-admin 10 on the same chart, comparing row and total values.
  - AC-S4: a demo row or alias with `<img src=x onerror=…>` shows as literal text in Table, Pivot, Funnel and a tooltip.
  - AC-S5: restricted tile for a new type reveals nothing in the DOM or network response.
  - AC-A1, AC-A2: table semantics; light and dark; smallest tile (Review Focus 5); below 768px.
  - AC-K1: the existing demo charts still render.
- [ ] **Step 4: Update both READMEs, the roadmap and `CLAUDE.md`** (feature status, test count, `npm test`).
- [ ] **Step 5: Clean up:** remove any test-only data added in Step 3; stop stray `dotnet` processes. Leave `Program.cs` personas in place, since the existing list of temporary state already tracks them.
- [ ] **Step 6: Report** to the user with results and any failures. Do not bump, pack, commit or push.
