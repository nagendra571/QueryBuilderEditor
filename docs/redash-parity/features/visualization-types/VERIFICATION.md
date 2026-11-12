# VERIFICATION — More visualization types

Run 2026-11-04 on `feature/rea1` (uncommitted), against the local sample app in a Release build on port
5081, with the demo "Sales Sample" source and the `TEMP-DATASCOPE-TEST` personas. Temporary "VT" queries,
charts and a dashboard were created for the run and deleted afterwards.

## Static gates

| Gate | Result |
|---|---|
| `dotnet test tests/QueryBuilder.Tests` | 408 passed, 0 failed |
| `cd client && npm test` | 113 passed, 0 failed |
| `npm run build` | Succeeds |
| `npm run lint` | No warnings in visualization files; warnings remain in files this work did not touch |
| AC-K2 `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration" |
| AC-K3 new packages | None. `package.json` gains only the `test` script; `package-lock.json` unchanged |

## Acceptance criteria

| AC | Result | How it was checked |
|---|---|---|
| AC-S1 scope | Pass | Same 12 charts as Super admin and Program-admin 10. Table 100 vs 20 rows; pivot 20 vs 4. In the Program-admin 10 browser, the other four program names appear in neither the page nor the render responses |
| AC-S2 minimal columns | Pass | Render responses list only role columns per type: `ProgramId`, selected by the query, is absent from the Table chart's rows. Server tests per type, including leftover roles |
| AC-S3 stale | Pass | Server tests per role. In the browser: a Bar that lost a measure shows "This chart needs updating", Save is off, **Remove Fees** clears it, Save turns on |
| AC-S4 literal text | Pass, with a limit (below) | `window.__xss` never fired and no `<img>` appeared in any tile. Payload literal in hbar (tooltip too), area100, bar100, scatter, table, pivot, funnel, waterfall |
| AC-S5 restricted tile | Pass | Share revoked: 5 tiles returned `restricted: true`, `chart: null`, no type, name or query id. No chart name in the page. Server theory over 7 new types |
| AC-C1 pivot totals | Pass | Sum: Adult Literacy 1,720 + 1,445 + 1,195 + 1,470 = 5,830 matches. Column totals 7,400 + 7,425 + 7,275 + 7,550 = 29,650 matches the grand total. Average totals show "—", with the footnote |
| AC-C2 100% stack | Pass | Unit tests: adds to 100, zero total empty, no `NaN`. Drawn correctly, as seen in the browser |
| AC-C3 waterfall | Pass | Unit tests, including `+5, -8, +10` = 7. Total bar drawn in the browser |
| AC-C4 funnel | Pass | Unit tests by hand calculation, including a zero previous step |
| AC-C5 trend | Pass | Unit tests, including out-of-order rows under four `TZ` values. Browser: latest 200 on 2025-10-28, "▲ 22.7% vs 2025-10-25" |
| AC-C6 refusals | Pass | With a record limit of 10: pivot gives 409 "This query has more than 10 records"; Table returns 10 rows with `truncated: true` and shows the grid's banner. Over 50 columns: server test |
| AC-K1 old charts | Pass | Existing "Program overview" dashboard: bar, pie, line and bar tiles draw with no new footnotes. A chart created with only the old option fields renders |
| AC-K2, AC-K3 | Pass | See above |
| AC-A1 semantics | Pass | Table, Pivot, Funnel: real tables, caption, every `th` has `scope`, no `role="img"`, no Show data. Drawn charts: `role="img"`, a label, Show data |
| AC-A2 themes, sizes | Pass, with limits (below) | Dark and light captured for all 12. Smallest tile (192×96): no overlap, tile scrolls. 600px wide: no page overflow, single column |

## Other checks

- **Editor reopen:** all 12 charts opened in **Edit chart** and saved back with the same options. Counter
  omits the default `percent` and `lineMeasures` fields, which changes nothing.
- **Editor type switching:** one dialog, switched through all 13 types with a live preview; no crash or
  console error. Saving a new chart from the editor worked.
- **Duplicate X:** "20 rows share the same X value. Add a Split by column or a total." appears.
- **Defect found and fixed here:** in the smallest tile, "Show data" overlapped the chart and Table showed
  no rows. Now fixed and rechecked. This fix had a browser check but no separate code review.

## Limits of this verification

- **AC-S4 cell values were injected in the test browser**, in the render responses. The planned demo-row
  edit was refused by the environment's safety check and was not attempted another way. This proves how
  the client displays a value, not a value travelling from SQL Server; the server returns it as JSON.
- **AC-S4 alias path:** the payload as a real column alias was literal in the results grid and caused no
  `<img>` or script while the editor preview switched through all types. It wasn't exercised in a saved
  chart, because the chart saved in that run did not use that column.
- **Tooltips:** hovered on the horizontal bar only. The others use the same React tooltip.
- **Not seen in the browser (unit tests only):** a waterfall crossing zero or going down, since the demo
  data has no negatives; the partial-preview notice; the "percentages of averages" hint; bubble sizes that
  differ (every demo row had size 1).
- **Dark theme** was forced by adding the `dark` class. It was captured at normal tile size only, and
  shading contrast was judged by eye plus a hand calculation in review, not a measuring tool.
- **Below 768px:** page overflow was measured; only the first tiles were looked at.
- Scoped-persona checks used Program-admin 10 only.

## Known issues, left as they are

1. **Trying Scatter or Trend changes X.** They need a number or date X, so a text X is replaced, and it
   stays replaced when switching back to another type. The preview shows it and it can be changed before
   saving. A chart can also use the same column as both X and measure.
2. **Horizontal bar labels overlap in the smallest tile** (2×2), inside the chart. Scrolling doesn't help
   there; a larger tile does.
3. **Narrow Funnel tiles hide the percent columns** behind horizontal scroll, as with a wide Pivot.
4. **Table `format` applies to every number column**, so choosing Currency also turns an ID column into
   `$10.00`. The raw value is in the cell's tooltip.
5. **Scatter date ticks read "Feb 19, 2025"**, while other charts use `2025-02-19`.
6. **Combo starts with no line measure**, so Save stays off until one is chosen.
