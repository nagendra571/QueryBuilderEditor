# SPEC — Query parameters (types, dropdowns, ranges, relative dates, dashboard mapping)

Branch `feature/rea1`. User decisions (2026-11-03): one round covering query parameters **and**
dashboard-wide parameters; inline parameter bar with **Apply**; types: fixed-list dropdown,
query-based dropdown, date range + relative dates, multi-select (IN).

## 1. Observed Redash behaviour (evidence; never copy code)
Sources: Redash v26.9.0 source (`scratchpad/ref/redash-v26.9.0-git`, BSD-2) and the public help page
<https://redash.io/help/user-guide/querying/query-parameters/> (saved as text in the session scratchpad).

| # | Observation | Evidence |
|---|---|---|
| P1 | `{{ name }}` in SQL text declares a parameter; widgets appear above results; gear opens settings (title, type) | help page §intro, §Parameter Settings |
| P2 | Types: text, text-pattern (regex), number, enum (static list), query (values from another query), date, datetime-local, datetime-with-seconds, date-range, datetime-range, datetime-range-with-seconds | `redash/models/parameterized_query.py:158-174`; `client/app/services/parameters/*.js` |
| P3 | Values are substituted into the SQL **text** with mustache after validation (not bound) | `parameterized_query.py:128-136` |
| P4 | Validation per type: number parse, date parse, value ∈ options (list allowed only if multi), regex fullmatch; any failure → error naming the parameters | `parameterized_query.py:114-185,214-218` |
| P5 | Text/text-pattern parameters (and undeclared placeholders) mark a query **unsafe** for embeds/shared dashboards | `parameterized_query.py:187-202`; help page FAQ table |
| P6 | Query-based dropdown values come from the source query's **latest cached result**; `name`/`value` columns if present, else first column | `parameterized_query.py:12-36` |
| P7 | Multi-select joins values with configurable prefix/suffix/separator into the SQL text (`IN ({{p}})`) | `parameterized_query.py:39-51`; `EnumParameter.js:36-40` |
| P8 | Date range renders `{{ p.start }}` / `{{ p.end }}` | help page §Date-Range; `parameterized_query.py:110-111,197-200` |
| P9 | Relative dates (Today/Now, Yesterday, This/Last week/month/year, Last 7/14/30/60/90 days, Last 12 months) are computed **in the browser**, so they don't work for scheduled queries | `DateParameter.js:17-22`; help page §Quick Date |
| P10 | Values in the URL as `p_<name>` | `Parameter.js:15,97-112`; help page FAQ |
| P11 | Saving the query with current values stores them as defaults | help page §Parameter Mapping "Default Value" |
| P12 | Dashboards: per widget parameter, value source = new/existing dashboard parameter, widget parameter, or static value (hidden) | help page §Value Source Options; `components/dashboards/EditParameterMappingsDialog.jsx` |
| P13 | Query-based dropdown access is checked separately (`require_access_to_dropdown_queries`) | `redash/handlers/queries.py:169` |

## 2. Desired behaviour (ours)
Business users never write SQL, so a parameter stays attached to a **filter condition** (today's
"Ask the user at run time" toggle). Values are **always bound as SQL parameters** — never text
substitution — so there is no "unsafe parameter" class (improves on P3/P5).

- **Q1 Types** (chosen per parameter in its settings, limited by the filter's column type and operator):
  `text`, `number`, `date`, `datetime`, `dateRange`, `datetimeRange`, `list` (fixed options),
  `queryList` (options from a saved query). `list`/`queryList` may **allow multiple** values — only when
  the filter operator is *is one of / is not one of* (IN / NOT IN). `dateRange`/`datetimeRange` only on a
  *between* filter over a date/datetime column. Booleans keep today's behaviour.
- **Q2 Settings dialog** (owner/editor): title, type, options (one per line) or source query (+ which
  column is the label and which the value; default: columns named `name`/`value`, else the first
  column for both), allow multiple, required, default value (relative dates allowed).
- **Q3 Relative dates**: Today, Yesterday, This week, This month, This year, Last week, Last month,
  Last year, Last 7/14/30/60/90 days, Last 12 months. Stored as tokens; **resolved on the server at
  run time** (server local date; week starts Monday) so they also work for future scheduling.
- **Q4 Parameter bar** on the query page above the tabs: one control per parameter (text, number,
  date picker, date-range picker with a relative-date menu, single select, multi-select with
  checkboxes/search, query-based select loading options from the server). **Apply** runs the query
  (and redraws the open chart) with the values; an "unapplied changes" hint shows until Apply.
  Required parameters without a value disable Apply with a message. The Run button uses the bar's
  values (the old prompt dialog is no longer used on the query page).
- **Q5 URL**: bar values are mirrored in the address as `p_<name>=…` (multi values repeated
  `p_x=a&p_x=b`; ranges `p_x=2026-01-01~2026-01-31`; relative tokens `p_x=last_30_days`); opening such
  a link pre-fills the bar. Same for dashboard parameters.
- **Q6 Save as defaults**: an editor can click **Save as defaults** in the bar; the query becomes dirty
  and the next Save stores them.
- **Q7 Server validation** (every run, export, preview, chart render, dashboard tile render): unknown
  parameter names → 400; each value must parse for its type; `list` values ∈ options; `queryList`
  values ∈ options **recomputed on the server**; multiple values only when allowed (max 1,000);
  required values present (or default). One 400 listing every invalid parameter title. Values are then
  materialised into the filter and bound as SQL parameters by the existing builder; scope predicates
  and record limits apply unchanged.
- **Q8 Query-based options endpoint** `GET /api/queries/{id}/parameters/{name}/options` → `[{label,value}]`
  (max 1,000, distinct, ordered as returned). The source query is **run live as the caller**: caller must
  be able to view the source query (else 403 — no names revealed), the source query must not be disabled,
  and the caller's row-level scope and record limits apply — so a dropdown never shows values outside
  the caller's zone (improves on P6's shared cache). Parameters of the source query use its defaults.
- **D1 Dashboard parameters**: a dashboard has its own parameter list (name, title, type and options
  copied from the first query parameter mapped to it, current default value). A bar at the top of the
  dashboard shows them with **Apply**; values in the URL as `p_<name>`.
- **D2 Tile mapping** (edit mode, tile menu → **Parameters…**): for each parameter of the tile's query
  choose the value source: **Dashboard parameter** (new or existing, compatible type only),
  **Tile input** (small control in the tile header), **Fixed value** (hidden), or **Query default**.
  New chart tiles default to Query default. Mappings are saved with the layout (same version check).
- **D3 Security**: tile renders still go through the chart render endpoint with the viewer's scope;
  values from dashboard/fixed sources are validated exactly like Q7. Restricted tiles keep their mapping
  server-side but expose none of it. Dashboard parameter names/titles are visible to all viewers.
- **D4 Compatibility**: existing saved queries keep working (their parameters become `text`/`number`/
  `date`/`datetime` from their column type); existing dashboards have no dashboard parameters and every
  tile defaults to Query default (today's per-tile "Set values" becomes the Tile input source).

## 3. Acceptance criteria
| AC | Criterion |
|---|---|
| A1 | Fixed list: owner sets options a/b/c; bar shows a select; run with b returns only b rows; API value `z` → 400 naming the parameter |
| A2 | Multi-select on an *is one of* filter: picking 10 and 30 returns exactly those programs; >1 value on a single-value parameter → 400 |
| A3 | Query-based dropdown from a saved query: Program-admin 10 sees only option 10 (scope applied); Super admin sees 10–50; a user who can't view the source query gets 403 from the options endpoint; a value not in the caller's options → 400 |
| A4 | Date range on a *between* date filter: picking a range and Apply filters rows; relative `last_30_days` resolves server-side (SQL preview shows bound dates) |
| A5 | URL: `?p_<name>=…` pre-fills the bar and the results after Apply; changing values updates the URL |
| A6 | Save as defaults → Save → reload shows the new defaults |
| A7 | Required parameter with no value: Apply disabled with message; API omits it → 400 |
| A8 | Dashboard parameter mapped to two tiles from different queries; changing it and Apply redraws both; Program-admin 10 still sees only scoped rows |
| A9 | Fixed-value mapping hides the control and is used; tile-input mapping shows a control in the tile header |
| A10 | Mappings persist through Save/reload; 409 conflict still works; restricted tiles reveal no mapping |
| A11 | Existing queries/dashboards work unchanged (legacy parameters, per-tile values) |
| A12 | `dotnet test` green (new validator/materialiser/options tests incl. negative proofs); `npm run build`; DBA script applies new migration on a fresh DB |
| A13 | No lost functionality: run/export/preview/charts/add-to-dashboard/sharing/history unchanged; keyboard-operable controls with labels |
