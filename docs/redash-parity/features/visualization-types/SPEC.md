# SPEC — More visualization types

Status: **APPROVED 2026-11-04.** Decisions: D1 = all three waves; D2 = one shared axis on Combo;
D3 = Table shows the first 1,000 rows with a banner; D4 = Cohort left out for now. (⚑ markers below are
resolved as stated in §6.)

Goal: let a business user answer more kinds of question from a saved query without writing SQL,
while keeping every guarantee the current charts have (viewer's own scope, access re-checked on
render, truncated data never drawn as if complete).

Today we have four types: Bar (grouped or stacked), Line, Pie and Counter.

---

## 1. What Redash does (reference only, no code or text reused)

Source: Redash public docs for version 26.9, captured with REA (`capture_browser_scenario`).

| Evidence | Pages |
|---|---|
| `ev_7bc78ebdd1c1…` | visualization-types |
| `ev_af2545c42c82…` | chart-visualizations, funnel-howto, cohort-howto, pivot-table-visualizations, table-visualizations, visualizations-how-to |
| `ev_64b8504fdd05…` | formatting-numbers, formatting-axis |

**Limit of this evidence:** these are documentation pages and their screenshots. Redash itself was
not run, so behavior is *documented*, not *observed at runtime*. Some screenshots may predate 26.9.
One large screenshot (`multi-form-chart.png`, 164 KB) exceeded the capture's size limit and was not
seen. Its surrounding text was read.

### 1.1 Inventory

Table (the default view), Boxplot, Chart, Cohort, Counter, Funnel, Map, Pivot Table, Sankey,
Sunburst, Word Cloud. "Chart" is one type with eight forms: line, bar, area, pie, scatter, bubble,
heatmap and box.

### 1.2 How each one works

| Type | Data it expects | How it is done |
|---|---|---|
| Chart | One X column and one or more Y columns, optionally a group-by, error and size column | One editor for all eight forms, so the user can switch form on the same data. Forms can be mixed in one chart per series, with left and right Y axes |
| Group by vs. multiple Y | Long data uses a group-by column, wide data uses several Y columns | Same model as our `series` vs. `measures` |
| Stacking | Line, bar and area | Stack order follows the order groups first appear in the results |
| Horizontal bar | Same as bar | An option on the bar form |
| Heatmap | Part of Chart | Built-in color schemes. Cannot be grouped |
| Funnel | Two columns: step and value | Screenshot: a **table with in-cell bars** and two computed columns, percent of the largest step and percent of the previous step. Not a drawn funnel shape |
| Pivot | Raw, ungrouped rows | Drag fields to rows and columns, choose an aggregator (Count in the screenshot), Totals row and column. **Aggregation happens in the browser.** Docs warn it slows above about 50,000 values |
| Cohort | Four columns: cohort date, period, count, cohort size | Shows count ÷ size as a percentage per period. Docs admit a time-zone rendering bug |
| Table | Query results | Reorder, hide and format columns. Can render cells as JSON, image or link, and has an "allow HTML" setting |
| Number format | Format strings | Two separate dialects: numeral-style for values and D3-style for axes |
| PNG download | Chart types | Camera icon on the chart |

Unknown from these docs: whether Counter supports a target or comparison. The docs show only a large
number with a label. Our earlier source note (E-R16) confirms value, row and decimals only.

### 1.3 Lessons worth keeping

1. **One editor, many forms.** Switching form on the same data is the best part of their design, and
   our dialog already works this way.
2. **The funnel is a table with bars.** That is more honest than a tapered shape, where area distorts
   the numbers, and accessible by nature.
3. **Duplicate X values produce odd shapes.** Redash lists this as a common mistake. We have the same
   gap: without a series column, `buildCartesian` plots duplicates as separate same-named points.

### 1.4 Things we should deliberately not copy

| Redash behavior | Why not |
|---|---|
| Rendering cells as HTML, images or links | Data from a business table would run or load in other users' browsers. Our rule is plain text only |
| Two number-format mini-languages | Business users should not learn format strings |
| Left and right Y axes | Two scales make any two lines appear related by choice of scale. See ⚑ D2 |
| Pivot re-aggregating anything | Totals of averages are silently wrong. See §3.3 |
| Treating missing values as zero (E-R15) | Already rejected in the charts spec |

---

## 2. What is worth adding

Tested against who uses this: business users, no SQL, one view per query, automatic GROUP BY, at most
1,000 chart rows. A type is valuable if it answers a question they already ask **and** the visual
builder can produce its data without a specially prepared view.

### 2.1 Recommended

| # | Type | Question it answers | Data shape | Cost |
|---|---|---|---|---|
| 1 | **Horizontal bar** | "Rank these names." Long names are unreadable on a vertical axis | Same as bar | S |
| 2 | **Area** | "How does the total build up over time?" | Same as line | S |
| 3 | **100% stacked** (bar, horizontal bar, area) | "How is the mix changing?" | Same as stacked | S |
| 4 | **Combo** (bars + lines) | "Actual vs. target" | X + 2–5 measures, each set to bar or line | S |
| 5 | **Scatter / bubble** | "Do these two things move together? Which item is the outlier?" | Numeric or date X, numeric Y, optional size, label and series | M |
| 6 | **Table** | A list on a dashboard: "overdue items." **Dashboards cannot show a table at all today** | Chosen columns | M |
| 7 | **Pivot, with heatmap shading** | "Program by month." The crosstab people otherwise rebuild in Excel | 1–3 row columns, 0–1 column column, 1–5 values | L |
| 8 | **Funnel** | "Where do we lose people between stages?" | Step + value | S |
| 9 | **Waterfall** | "What moved the budget from A to B?" Not listed in the Redash docs captured | Category + signed amount | M |
| 10 | **KPI upgrades** | "Are we on target? Is it going up?" | Counter + target column; new Trend type: date + measure | M |
| — | **Number formats** (applies to all) | Currency, percent, compact, decimals, chosen from a list | Per chart | S |

Sizes: S under a day, M one to two days, L two to three days of implementation, by rough estimate.

Why these:

- **Heatmap is not a separate type.** A heatmap is a pivot with shaded cells, so a "Shade cells" switch
  gives both from one component. Being a real table, it also stays readable by screen readers.
- **Trend suits dashboards well** (my judgment, not measured usage) and is not listed in the Redash docs
  captured here: latest value, change from the previous period, and a small sparkline.
- **Table comes first among the bigger items** because its absence is a real gap on dashboards.

### 2.2 Not recommended

| Type | Reason |
|---|---|
| Map, Word cloud | Excluded by request |
| Sankey | Needs from-stage / to-stage data the builder cannot produce. Requires a prepared view |
| Sunburst | Hard to read beyond two rings. Pivot with nested rows answers the same question precisely |
| Box plot | Needs raw rows, so the 1,000-row refusal triggers constantly, and most business readers cannot read one. Revisit with a histogram if asked |
| Gauge | Takes a large area to show one number. The Counter target bar shows the same thing |
| Treemap | A sorted horizontal bar is more accurate, and reads better for many categories |
| Radar, gantt, calendar | Niche, or they need data the builder cannot shape |
| Error bars | Rarely wanted by business users |

### 2.3 Cohort ⚑ D4

Useful for retention, but needs a four-column view prepared by a DBA, because the builder cannot
compute "periods since start." Once Pivot exists it is cheap: rows = cohort, column = period,
value = count ÷ size. Recommendation: **leave it out now** and add it when someone has that view.

---

## 3. Design

### 3.1 No migration and no new dependency

- `QueryVisualizations.Type` is stored as a string (`nvarchar(20)`). New enum values are appended, and
  the longest name, `HorizontalBar`, is 13 characters. **No schema change.**
- New options are additive JSON fields with defaults, so `Version` stays 1 and existing charts load
  unchanged. The DBA script is unaffected by this work.
- Recharts 3 (already in the package, MIT) provides area, scatter and composed charts. Waterfall is a
  stacked bar with an invisible base. Table, Pivot, Funnel and Trend text are plain HTML.

### 3.2 Contract

```csharp
public enum VisualizationType
{
    Bar = 0, Line = 1, Pie = 2, Counter = 3,
    Area = 4, HorizontalBar = 5, Combo = 6, Scatter = 7,
    Table = 8, Pivot = 9, Funnel = 10, Waterfall = 11, Trend = 12
}
```

Additions to `VisualizationOptions`, all optional:

```jsonc
{
  "percent": false,            // 100% stack; requires stacked; bar, horizontalBar, area
  "lineMeasures": [],          // combo: the subset of measures drawn as lines
  "size": null,                // scatter: bubble size column, never negative
  "label": null,               // scatter: name shown in the tooltip
  "table": { "columns": [] },  // table: output columns, in display order
  "pivot": { "rows": [], "column": null, "values": [], "totals": true, "shade": false },
  "format": { "style": "number", "decimals": null, "currency": null },
  "counter": { "value": "", "label": null, "decimals": 0, "target": null }
}
```

- `format.style`: `number | compact | currency | percent`. `currency` is a 3-letter ISO code, validated
  on the server and passed to `Intl.NumberFormat`. `percent` shows the value as stored with a % sign:
  it never multiplies by 100, because a query's value of 42 means 42%.
- Trend reuses `x` and `measures[0]`.

### 3.3 Pivot totals must not lie

Pivot works on rows that may already be aggregated by the query. Re-adding them is only correct for
some aggregates, so the column's aggregate decides what happens when several rows land in one cell, and
for totals:

| Output column | Combine | Totals |
|---|---|---|
| Sum, Count, or plain number | Add | Add |
| Min / Max | Min / Max | Min / Max |
| Average, Count Distinct | **Not combinable.** One row in the cell: show it. Several: "—" | **"—"**, with a footnote saying why |

The client needs each column's aggregate to apply this, so `VisualizationDataDto` columns gain an
`aggregate` field, derived on the server from the saved definition.

### 3.4 Security invariants (must hold for every new type)

Everything still goes through `RenderVisualizationCommand`, so access, disabled, scope and record limit
checks apply with no new data path. Specific risks:

1. **`ReferencedColumns` must learn every new role** (`size`, `label`, `table.columns`, `pivot.*`,
   `counter.target`, `lineMeasures`). It drives both stale-column checks and data minimization, so a
   missed role either drops needed data or skips validation. Tests per type.
2. **Plain text only.** Table and pivot cells render as React text. No HTML, image or link rendering,
   and no setting to enable it.
3. **Counter trims to one row** in the handler (`rows.Take(1)`), but Trend needs all of them. Counter's
   trim stays, including when a target column is set: the target comes from the same row.
4. **Restricted dashboard tiles** already hide name and id. Type is only in the chart DTO, which a
   restricted tile omits, so nothing leaks. A test confirms this for new types.
5. **Stale chart path.** Each type's rules go in `VisualizationOptionsValidator`, so a removed column
   gives the existing "needs updating" message rather than a partial chart.

### 3.5 Per-type rules (validated on the server, mirrored for the live preview)

| Type | Rules |
|---|---|
| Horizontal bar | As bar. Category labels truncate with the full name in the tooltip |
| Area | As line. **Stacked with a missing value shows a footnote**, because a stack cannot show a gap truthfully |
| 100% stacked | Requires `stacked`. A category totaling zero is left empty, not divided by zero. The editor warns if a measure is an average |
| Combo | At least 2 measures, at least 1 line and 1 bar, no series column, **one shared axis** (⚑ D2) |
| Scatter | X must be number or date, Y number, `size` not negative, series allowed. Over 500 points: no dots animation |
| Table | 1–30 columns. Sticky header, paged 25 per page in the tile. See ⚑ D3 for over 1,000 rows |
| Pivot | 1–3 rows, 0–1 column, 1–5 values, no column in two roles. Over **50 distinct columns: refused** with a message suggesting filters. Truncated data refused, because totals would be wrong |
| Funnel | Query order by default, because stage order is business meaning. At most 20 steps. Negative refused. Blank first step or a zero: percent shows "—" |
| Waterfall | One measure, no series, at most 30 bars. Each bar starts where the last ended, with an automatic final total. Up, down and total differ by shape label and legend, not color alone |
| Counter + target | Target must be a number. Shows "82% of target" plus a thin bar. Zero or missing target: bar hidden |
| Trend | X is date or number, sorted ascending. Latest non-empty value, change from the previous one, sparkline. Names the compared period ("vs 2026-10"), so a partly finished month is visible rather than hidden. Change is shown with an arrow and text, **not green or red**, since a rise is not always good |

### 3.6 Fixes that apply to existing types

- **Duplicate X warning** (lesson 3): a footnote when rows share an X value and no series is set: "N rows
  share the same X value. Add a Split by column or a total."

### 3.7 Editor and accessibility

- The chart type select is grouped: **Compare**, **Trend**, **Part of whole**, **Relationship**,
  **Table**, **Single number**. Roles shown change with the type, and shared roles survive a switch,
  keeping the one-editor strength.
- `role="img"` wraps drawn charts only. **Table, Pivot and Funnel are real tables and must not get it**,
  since it hides their structure from screen readers. They also skip the "Show data" toggle.
- Shading always keeps the number visible, with contrast checked in both themes.
- Colors come from the Zoning Map palette. Any palette change goes through the `dataviz` validator.
- Dashboards: Table and Pivot scroll inside the tile, and auto-height caps at the tile maximum.

---

## 4. Delivery order

Each wave is independently shippable, with runtime verification through Playwright as a scoped and an
unrestricted persona.

| Wave | Contents | Why this order |
|---|---|---|
| **A** | Number formats, horizontal bar, area, 100% stacked, combo, duplicate-X warning | Quick wins that share one data model; number formats are used by everything after |
| **B** | Table, Pivot + shading | Closes the dashboard gap; biggest value |
| **C** | Funnel, Waterfall, Scatter/bubble, Counter target, Trend | Each stands alone, and any can be dropped without affecting the others |

---

## 5. Acceptance criteria

**Security (per new type)**
- AC-S1: The same chart rendered as ProgramId 10 and as Super admin shows only each viewer's rows. A scoped
  viewer never receives other rows, including in pivot totals (**negative test**).
- AC-S2: The render response contains only the columns of that type's roles (**per type**).
- AC-S3: Removing any role's column gives "needs updating" rather than a partial chart (**per role**).
- AC-S4: A value containing `<img src=x onerror=…>` shows as literal text in Table, Pivot, Funnel and
  every chart's tooltip (**Playwright**).
- AC-S5: A restricted dashboard tile reveals no type, name or data for a new type.

**Correctness**
- AC-C1: Pivot of an Average column: totals show "—" with the footnote. A Sum column: totals add up.
- AC-C2: 100% stack: each category adds to 100. A zero-total category is empty, with no `NaN`.
- AC-C3: Waterfall: the final total equals the sum of the amounts.
- AC-C4: Funnel percentages match a hand calculation; a zero previous step shows "—".
- AC-C5: Trend picks the latest non-empty value after sorting, whatever order the query returned.
- AC-C6: Pivot over 50 columns, and truncated data, are refused rather than drawn.

**Compatibility**
- AC-K1: Every chart saved before this work loads and renders unchanged (existing tests stay green).
- AC-K2: No migration is generated (`dotnet ef migrations has-pending-model-changes` is clean).
- AC-K3: No new npm or NuGet package.

**Accessibility and layout**
- AC-A1: Table, Pivot and Funnel expose real table semantics; drawn charts keep their label and "Show data."
- AC-A2: Every new type checked in light and dark themes, in a small dashboard tile, and below 768px.

---

## 6. Open decisions ⚑

- **D1** Which types to build. Recommendation: all of §2.1, in waves.
- **D2** Dual Y axes on Combo. Recommendation: no, keep one shared axis.
- **D3** Table with over 1,000 rows. Recommendation: show the first 1,000 with the same banner as the
  results grid, as a narrow exception to "refuse truncated data," since a list with a clear banner cannot
  mislead the way a partial chart can.
- **D4** Cohort. Recommendation: leave out for now.
