# Results panel: feature list

The Results tab (shown after **Run** on a query) should be a place where business users work with their
data for a while before they move on to charts. This file is the list we agreed on (2026-11-25) and
tracks what is done. Mark a row **Done** (with the version) when it ships.

Prior art looked at: Snowflake worksheets (side panel, column statistics), Metabase (header actions,
"filter by this value", detail view, distribution), Databricks (results filters, column pinning, data
profile), AG Grid (pinning, status bar, grouping), Excel (freeze panes, status-bar totals), Hex/Deepnote
(header summaries). Evidence only; no code or assets copied.

## A point that applies to every feature

The grid holds only the rows the run sent back: 1,000 by default, or fewer when a record limit is set.
Anything the grid computes (filters, totals, profiles) covers those loaded rows only, and export and
charts re-run the query on the server, ignoring what was done in the grid. So grid-side numbers must be
labelled honestly when the result was cut off ("based on the first 1,000 rows"), and #13 ("Add to query")
is the way to get a true, query-wide answer.

## Recommended

| # | Feature | Group | Phase | Status |
| --- | --- | --- | --- | --- |
| 1 | Smooth scrolling through all loaded rows, no pages; header and row numbers stay fixed | Moving around | 1 | Planned |
| 2 | Pin columns to the left or right | Moving around | 1 | Planned |
| 3 | Resize columns, auto-fit, wrap-text switch | Moving around | 1 | Planned |
| 4 | Row density: compact or comfortable | Moving around | 1 | Planned |
| 5 | Full-screen results | Moving around | 1 | Planned |
| 6 | Record panel on the side (every field of a row, next/previous) | Moving around | 2 | Planned |
| 7 | Column profile panel (histogram, top values, min/max/average, empty share) | Understanding | 2 | Planned |
| 8 | Selection totals in the footer (Sum, Average, Count, Min, Max) | Understanding | 2 | Planned |
| 9 | Optional totals row at the bottom | Understanding | 4 | Planned |
| 10 | Per-column filters (value checklist for text, range for numbers and dates) | Finding | 3 | Planned |
| 11 | Right-click a cell: show only this value / hide this value | Finding | 3 | Planned |
| 12 | Active filters as removable chips above the grid | Finding | 3 | Planned |
| 13 | "Add to query": grid filters become builder filters, then re-run | Finding | 3 | Planned |
| 14 | Conditional formatting (colour scales, in-cell bars, highlight rules) | Shaping | 4 | Planned |
| 15 | Per-column number and date formats | Shaping | 4 | Planned |
| 16 | Copy a cell, row or selection with headers, pastes cleanly into Excel | Getting data out | 1 | Planned |
| 17 | Remember the layout per saved query (order, widths, pins, hidden, formats). Touches the database: decide first | Remembering | 5 | Planned |
| 18 | "Chart this column" from the header menu, opens the chart editor filled in | Moving on | 5 | Planned |

## Other ideas (not planned yet)

| # | Feature | Group | Status |
| --- | --- | --- | --- |
| 19 | Mini summary under each header (empty %, distinct count) | Understanding | Idea |
| 20 | Search matches highlighted, with next/previous | Finding | Idea |
| 21 | Sort by several columns (shift-click) | Finding | Idea |
| 22 | Group rows by a column, with subtotals | Shaping | Idea |
| 23 | Export exactly what is on screen (needs server work: export re-runs the query today) | Getting data out | Idea |
| 24 | Row drill-down | Moving on | Done (2.2.0) |

## Phases

1. **Foundation**: #1–5 and #16. The new grid, its header menu and toolbar, which everything else uses.
2. **Side panels**: #6–8. Record panel and column profile share one right-hand panel; selection totals in the footer.
3. **Filtering**: #10–13.
4. **Formatting**: #9, #14, #15.
5. **Remembering and moving on**: #17–18. #17 last, once we know which settings are worth saving.
