# PLAN — Query parameters + dashboard parameter mapping

Read `SPEC.md` (same folder), `CLAUDE.md` (conventions + sharp edges) and `DESIGN.md` (keep the Zoning Map
visual system: tokens/utilities in `client/src/index.css`; violet `--query` marks query state). Write our
own code; never copy Redash. No new npm/NuGet packages (shadcn/Radix pieces already in
`client/src/components/ui` — Popover, Command, Calendar, Select, Checkbox, Dialog — are enough). Do NOT
edit `src/QueryBuilder.Api/Program.cs` (temporary persona block). No git add/commit/push.

## Phase A — query parameters

### Backend
A1. **Domain** (`Model/QueryDefinition.cs`): extend `QueryParameter` with
   `Type` (new enum `ParameterType { Text, Number, Date, DateTime, DateRange, DateTimeRange, List, QueryList, Boolean }`,
   default derived from `DataType` when absent so old JSON still loads), `Options` (`List<string>`),
   `OptionsQueryId` (Guid?), `OptionsLabelColumn`/`OptionsValueColumn` (string?), `AllowMultiple` (bool).
   `DefaultValue` stays a string in the wire format below. Parameters live inside the definition JSON —
   no migration for Phase A.
A2. **Wire format for values** (unchanged API shape `Dictionary<string,string?>`):
   single → `"v"`; multiple → JSON array string `["a","b"]`; range → `"start~end"`; relative date token →
   `"today"`, `"last_30_days"`, … (ranges accept range tokens; single dates accept `today`/`yesterday`).
A3. **Application** `Parameters/`:
   - `RelativeDates` — token list + `Resolve(token, DateTime now)` → (start, end) (week starts Monday;
     "Last 7 days" = today-6 … today; month/year calendar-based; end inclusive).
   - `ParameterValidator` — validates the definition's parameters on save (type compatible with the bound
     filter's column type/operator per SPEC Q1; list needs ≥1 option, ≤ 500 options, each ≤ 200 chars;
     queryList needs an OptionsQueryId the saver can view; AllowMultiple only for In/NotIn).
   - `ParameterBinder.MaterializeAsync(definition, values, ct)` — for each parameterized condition:
     pick value (supplied → default → required error), parse per type, check list membership,
     for queryList call `IParameterOptionsService` (below) to get the caller's options, expand relative
     dates, then write the concrete literal into a **copy** of the definition (`IsParameterized=false`,
     `Value` in the builder's existing format: single literal, the existing Between format, the existing
     IN-array format — read `SqlServerQuerySqlBuilder.ParseBetween/ParseArray`). Unknown names → error.
     All errors collected → one `CatalogValidationException` (400) listing parameter titles.
   - `IParameterOptionsService` + implementation: loads the source saved query, checks
     `SavedQueryAccess.CanView` (else `ForbiddenException`), refuses disabled queries, runs it through the
     normal path (`ValidateAsync` → `IDataScopeGuard.AuthorizeAsync` → `Build` → execute) as the caller with
     the source query's own defaults, caps at 1,000 distinct options, returns `{label,value}` using
     `name`/`value` columns or the configured columns or the first column. Cache per request.
   - Use the binder in **every** path that executes or previews a definition: `RunQueryCommand`,
     `ExportQueryCommand`, SQL preview, `RenderVisualizationCommand` (and anything else that today passes
     runtime parameter values). Keep the old runtime-parameter mechanism working or remove it cleanly —
     but values must still end up as SqlParameters (the builder already binds every literal).
A4. **Endpoint** `GET /api/queries/{id}/parameters/{name}/options` (Editor `Endpoints/`), using
   `QueryBuilderJson.Options`. For an unsaved query being built, also accept
   `POST /api/parameters/options` with `{ dataSourceId?, parameter }` so the settings dialog can preview
   options (same access rules: caller must view the source query).
A5. **Tests**: validator (type/operator compatibility), binder (each type, multiple, range, relative
   tokens with a fixed clock, required/default, unknown name, list membership), options service (403 when
   caller can't view source, scope applied via fake guard, 1,000 cap, name/value column pick), and a
   negative proof: temporarily skip list-membership check → a test fails → restore.

### Frontend (`client/src`)
A6. Types: `QueryParameter` gains `type`, `options`, `optionsQueryId`, `optionsLabelColumn`,
   `optionsValueColumn`, `allowMultiple`. Helpers in `features/parameters/param-values.ts`
   (encode/decode wire format + URL `p_` encode/decode, relative token labels).
A7. `features/parameters/ParameterBar.tsx`: renders one control per parameter, Apply button, unapplied
   hint, required message, "Save as defaults" (editors). Controls in `features/parameters/controls/`:
   text, number, date, datetime, date range (two dates + relative menu), select, multi-select
   (Popover+Command with checkboxes, chips summary), query-based select (loads options via the endpoint,
   loading/error/403 states). Each control has a visible label and is keyboard-operable.
A8. `features/parameters/ParameterSettingsDialog.tsx`: title, type (only compatible types offered),
   options textarea, source query picker (saved queries the user can view) + label/value column
   selects + options preview, allow multiple, required, default (incl. relative tokens). Open it from a
   gear in the bar and from the parameterized filter row in `FiltersPanel`/`HavingPanel`.
A9. `pages/QueryEditorPage.tsx`: render the bar above the tabs when the definition has parameters;
   Run/Apply use the bar values (remove the prompt-on-run for this page); chart tabs use the same values;
   mirror values in the URL (`useSearchParams`, replace history); store state outside Zustand selectors
   rule-safe.

## Phase B — dashboard parameters

B1. **Domain**: `Dashboard.ParametersJson` (nvarchar(max) null) holding
   `[{ name, title, type, options, optionsQueryId, optionsLabelColumn, optionsValueColumn, allowMultiple, value }]`;
   `DashboardTile.ParameterMappingsJson` (nvarchar(max) null) holding
   `{ "<queryParamName>": { source: "dashboard"|"tile"|"fixed"|"default", dashboardParameter?, value?, title? } }`.
   Migration `AddDashboardParameters` (pure DDL).
B2. **Application**: DTOs carry `parameters` on the dashboard detail and `parameterMappings` on non-
   restricted tiles; `SaveDashboardTilesRequest` gains `parameters` and per-tile `parameterMappings`.
   Validate: ≤ 20 dashboard parameters, unique names (same rules as `QueryParameterNames`), every
   mapping's query parameter exists on the tile's query, `dashboard` mappings point at an existing
   dashboard parameter of a compatible type, fixed values validated by the binder rules. Restricted
   tiles: keep stored mappings unchanged, never return them.
B3. **Frontend**: `features/dashboards/DashboardParameterBar.tsx` (reuses Phase A controls + Apply + URL
   `p_`); `ParameterMappingDialog.tsx` from the tile Layout menu ("Parameters…", edit mode); tile header
   gets compact controls for `tile` mappings (replaces today's per-tile "Set values" prompt; keep the
   prompt only as a fallback for required values with no source); `ChartTile` computes effective values
   (dashboard → tile → fixed → default) and passes them to the render call.
B4. **Tests**: mapping validation (unknown param, incompatible type, restricted tile mapping preserved
   and hidden), dashboard parameter limits, effective value resolution.

## Validation (end of each phase)
`dotnet test tests/QueryBuilder.Tests`; `cd client && npx tsc -b && npm run build`; refresh wwwroot:
`rm -rf src/QueryBuilder.Editor/wwwroot/assets && cp -r client/dist/assets src/QueryBuilder.Editor/wwwroot/assets && cp client/dist/index.html src/QueryBuilder.Editor/wwwroot/index.html`.
Report honestly: files, migration, test counts before/after, anything not done or uncertain.
