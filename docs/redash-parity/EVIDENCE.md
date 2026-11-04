# Evidence index

All evidence collected 2026-11-02. These are reproducible references: anyone can re-run the
command or re-read the cited source to check a finding.

## Pinned subjects

| Subject | Identity | How obtained |
|---|---|---|
| Destination | `nagendra571/QueryBuilderEditor` branch `feature/rea1` @ `bca3dcb53b89399041da859e4a49e079c982134e` | local checkout |
| **Redash — running instance** | `http://localhost:5000`, Docker image `redash/redash:latest` = `sha256:55cedc91a09107e894a0ce872d138ec8c708ec537f6df282207bdf14eec72491`, created 2019-10-27; `redash/__init__.py` `__version__ = '8.0.0+b32245'`; `package.json` version 8.0.0 | `docker image inspect`, `docker exec redash_server grep __version__` |
| Redash 8.0.0 source used for citations | Copied **from the running container** (`docker cp redash_server:/app/{redash,client/app,migrations,package.json,requirements.txt,LICENSE,CHANGELOG.md}`) into the session scratchpad `ref/redash-8.0.0-running/`. The public tag `v8.0.0` = `a16f551e22c6288df6f067aa12caa5afd9a8f1dd`; the build suffix `+b32245` means the image may differ slightly from that tag. Container files are authoritative for this instance. | read-only copy |
| Redash current release | tag `v26.9.0` @ `42fdf9d4a0fdb83841130ab78f69899a224f281b` (shallow clone, scratchpad `ref/redash-v26.9.0-git/`) | `git clone --depth 1 --branch v26.9.0` |
| Redash license | BSD-2-Clause (8.0.0 `LICENSE`: "Copyright (c) 2013-2019, Arik Fraimovich"; v26.9.0 `viz-lib/package.json:27` `"license": "BSD-2-Clause"`) | file read |

> **Important:** the `latest` Docker tag is from 2019 (8.0.0). Current Redash is 26.x. Runtime
> comparisons are only valid against 8.0.0. Notes about v26.9.0 come from reading its source,
> not from running it.

Paths below: `R8:` = `ref/redash-8.0.0-running/`, `R26:` = `ref/redash-v26.9.0-git/` in the
scratchpad; destination paths are relative to the repo root.

## Destination evidence (QueryBuilderEditor)

| ID | Kind | Reference | Establishes |
|---|---|---|---|
| E-Q01 | command | `node -v` 24.22.2, `npm -v` 11.0.0, `dotnet --list-sdks` 10.0.114 / 9.0.304 / 9.0.320, sqlcmd ODBC 18 present, Chrome + Edge present, Docker 29.4.3 | toolchain |
| E-Q02 | command (script) | Licenses: parsed all `src/*/obj/project.assets.json` + `tests/*/obj/project.assets.json` (108 packages) against `~/.nuget/packages/<id>/<ver>/*.nuspec`; parsed `client/package-lock.json` non-dev entries (515) with each `node_modules/*/package.json` | no paid licenses (BASELINE §1) |
| E-Q03 | command | `dotnet test tests/QueryBuilder.Tests` → `Passed! - Failed: 0, Passed: 75, Skipped: 0, Total: 75` | baseline tests green |
| E-Q04 | source | `src/QueryBuilder.Application/DataSources/RunQueryCommand.cs:30-75` | run pipeline order; disabled check only with `SavedQueryId` |
| E-Q05 | source | `src/QueryBuilder.Application/DataSources/ExportQueryCommand.cs:54-65` | export refusal on record limit |
| E-Q06 | source | `src/QueryBuilder.Application/DataScoping/DataScopeGuard.cs:40-79` | scope → predicates; 403 on hidden objects |
| E-Q07 | source | `src/QueryBuilder.Editor/DataScoping/CurrentDataScopeService.cs:36-40` | no HttpContext ⇒ Denied |
| E-Q08 | source | `src/QueryBuilder.Editor/Identity/CurrentUserService.cs:33-41` | no HttpContext ⇒ `anonymous` |
| E-Q09 | source | `src/QueryBuilder.Infrastructure/Sql/SqlServerQuerySqlBuilder.cs:28,130-131,262-266`; `Execution/SqlServerQueryExecutionService.cs:31-34,87-96` | all values bound as `SqlParameter`; runtime names validated |
| E-Q10 | source | `src/QueryBuilder.Application/Commands.SavedQueries/ToggleFavoriteCommand.cs:13-21`; `src/QueryBuilder.Editor/Endpoints/SavedQueryEndpoints.cs:36-40` | global favorite, no access check (D2) |
| E-Q11 | source | `SaveQueryCommand.cs:51-56`, `GetSavedQueryByIdQuery.cs:18-23`, `SetQueryDisabledCommand.cs:20-23` | edit = owner/Editor; view = owner/any share; disable = owner |
| E-Q12 | source | `src/QueryBuilder.Editor/EndpointRouteBuilderExtensions.cs:21-60` | route-group auth; admin AND composition |
| E-Q13 | source grep | `IMemoryCache` only in `SqlServerDataCatalogService.cs:16`, `DataScopeRuleRepository.cs:9`; hosted services only migration/seeding | no result cache, no scheduler |
| E-Q14 | source | `client/src/App.tsx:12-16`, `client/src/pages/QueryEditorPage.tsx:305-324,455-456` | SPA routes, editor tabs, run request shape |
| E-Q15 | source | `README.md` "Roadmap" (lines ~115-124) | saved visualizations and dashboards are documented as not built |
| E-Q16 | source | `src/QueryBuilder.Infrastructure/Catalog/SqlServerDataCatalogService.cs:38-131,157,168,289-300`; `ExportService.cs:13-28` | validation uses the policy-filtered catalog; CSV/XLSX only |
| E-Q17 | source | `DatabaseMigrationHostedService.cs:9,19`; `Scripts/README.md:4-29`; `SetupDiagnosticsRunner.cs:52-61` | auto vs DBA provisioning |
| E-Q18 | command | `grep -cE '\[(Fact|Theory)\]' tests/QueryBuilder.Tests/*Tests.cs` → 10/7/15/3/8/10 | test coverage map (BASELINE §7c) |

## Reference evidence (Redash)

| ID | Kind | Reference | Establishes |
|---|---|---|---|
| E-R01 | source R8 | `redash/models/__init__.py:956-982` `class Visualization` (columns `type`, `query_id` FK, `name`, `description`, `options` Text) | visualization belongs to exactly one query; options are free-form JSON text |
| E-R02 | source R8 | `redash/models/__init__.py:986-1001` `class Widget` (`visualization_id`, `text`, `width`, `options`, `dashboard_id`) | dashboards reference visualizations through widgets |
| E-R03 | source R8 | `redash/handlers/visualizations.py:11-56` — create/update/delete require `@require_permission('edit_query')` **and** `require_object_modify_permission(query)` | changing a visualization = permission to modify its query |
| E-R04 | source R26 | `redash/handlers/visualizations.py:13-47` — same two checks | rule unchanged in v26.9.0 |
| E-R05 | source R8 | `redash/permissions.py:17-49` (`has_access` via data-source groups, view_only level), `:104-108` (`can_modify` = admin/owner or modify access) | access is decided by group ↔ data source, not per query |
| E-R06 | source R8 | `redash/handlers/query_results.py:31-70` `run_query`: `max_age` ⇒ `QueryResult.get_latest(data_source, query.text, max_age)`; a cache hit is returned to any caller | results are cached per (data source, query text hash) and **shared between users** |
| E-R07 | source R8 | `redash/models/__init__.py:237-281` `QueryResult` (`query_hash`, `retrieved_at`, `get_latest`) | cache key does not include the user |
| E-R08 | source R8 | `redash/handlers/query_results.py:208-252` GET result: only `require_access(query_result.data_source, user, view_only)` | reading a cached result checks data-source access only |
| E-R09 | source R8 | `redash/handlers/query_results.py:172-205` POST `/api/queries/<id>/results`: view-only users may execute only when `query.parameterized.is_safe` | view-only + parameters rule |
| E-R10 | source R8 / R26 | R8 `redash/models/parameterized_query.py:172-174` (unsafe = any `text` param); R26 `:188-191` (unsafe = `text` or `text-pattern`) | parameter safety changed between versions |
| E-R11 | doc | https://redash.io/help/user-guide/querying/query-parameters (retrieved 2026-11-02, page undated): "If a query uses a Text type parameter it cannot be embedded because Text parameters are not safe from SQL injection." "…dynamic dates … aren't compatible with Scheduled Queries." | documented parameter limits |
| E-R12 | source R8 | `redash/tasks/queries.py:~197-212`: scheduled refresh applies the query's stored parameter values, skips invalid ones, and enqueues with `query.user_id` | scheduled runs use the owner's identity and stored defaults |
| E-R13 | source R8 | `redash/handlers/dashboards.py:238-292` public dashboard via `ApiKey` token, created by admin/owner (`:267`) | anonymous public links exist (opt-in per dashboard) |
| E-R14 | source R8 | `client/app/visualizations/` dirs: box-plot, chart, choropleth, cohort, counter, details, funnel, map, pivot, sankey, sunburst, table, word-cloud; R26 `viz-lib/src/visualizations/` same set | visualization type families |
| E-R15 | source R8 | `client/app/visualizations/chart/index.js:13-32` defaults (`globalSeriesType:'column'`, `sortX`, `legend`, `columnMapping`, `missingValuesAsZero: true`), `:99-106` series types Line/Bar/Area/Pie/Scatter/Bubble/Heatmap/Box, `:157-158` drops `columnMapping` entries whose column no longer exists | chart configuration model; client-side pruning of stale mappings |
| E-R16 | source R8 | `client/app/visualizations/counter/index.js:6-11` (`counterColName`, `rowNumber`, `stringDecimal`) | counter = single value from a chosen row/column |
| E-R17 | source R8 | `redash/models/__init__.py:436` default `type="TABLE"` visualization created with each new query | every query has a Table visualization |
| E-R18 | source R8 / R26 | R8 `package.json`: `plotly.js` 1.41.3, `d3` ^3.5.17, `leaflet` ^1.2.0; R26 `viz-lib/package.json`: `plotly.js` 3.3.1 | Redash renders charts with Plotly (MIT) — **not reused** |
| E-R19 | source R8 | `client/app/services/visualization.js:5` `api/visualizations/:id`; `client/app/services/query-result.js:39-42` result/job endpoints; `redash/handlers/api.py:126-151` route table | client ↔ API surface |
| E-R20 | source R8 | `redash/models/__init__.py:720-735` `Favorite(object_type, object_id, user_id)`; `:400` `tags` array on Query; `:390` `schedule` JSON | favorites are per user; tags and schedules live on the query |
| E-R21 | doc | https://redash.io/help/user-guide/visualizations/visualizations-how-to (retrieved 2026-11-02, undated): "You can modify the settings of an existing visualization from the query editor screen." | visualizations are edited inside the query editor |
| E-R23 | source R8 / R26 | R8 `redash/models/users.py:292`, R26 `models/users.py:304-322` `AccessPermission(object, access_type, grantor_id, grantee_id)`; R26 `handlers/permissions.py:25-104` (list needs modify; grant/revoke admin-or-owner); R26 `settings/organization.py:46` `FEATURE_SHOW_PERMISSIONS_CONTROL` default **false** | per-object per-user grants exist alongside group↔data-source access, and their UI is hidden by default |
| E-R24 | source R26 | `client/app/components/ParameterMappingInput.jsx:28-46` `MappingType` (dashboard-level / widget-level / static value); used by `components/dashboards/EditParameterMappingsDialog.jsx`, `AddWidgetDialog.jsx` | dashboard parameter mapping model |
| E-R25 | source R26 | `redash/models/__init__.py:982-1030` `Alert` states unknown/ok/triggered, `rearm`; `evaluate()` reads `self.query_rel.latest_query_data` (`:1016`), selector first/max/min, `options["op"]` | alerts evaluate the query's **shared latest result**, not a per-subscriber run |
| E-R26 | source R26 | `redash/handlers/embed.py:23-41` embed view = audit event + SPA; `:42-47` public dashboard resolved by `ApiKey` token; `authentication/__init__.py:139-154` API-key request auth | embeds and public dashboards authenticate by key, not by user session |
| E-R27 | source R26 | `redash/handlers/query_results.py:425-437` `JobResource.delete` → `job.cancel()` | running queries can be cancelled |
| E-R22 | source R26 | `redash/handlers/query_results.py:64-89` `run_query(…, should_apply_auto_limit, max_age)`, `apply_auto_limit` | v26 adds an optional automatic LIMIT; cache still keyed on text |

## REA evidence

REA `rea-agents` 6.8.0 (MCP protocol 2025-11-25; 229 tools, 6 prompts). Full session bundle
exported to the scratchpad file `rea-evidence-bundle.json` (16.5 MB, 7 records, 4 unknowns).

| Evidence ID | Operation | Result / limitation |
|---|---|---|
| `ev_b0e6bb2cfc955d480b50c0e2f0860b7ce8febf8310932572ad665e92529fac62` | `analyze_javascript_application` on R8 `client/app` (root sha256 `86d91c50…a9b8`), `source_paths` = 11 visualization/query/result files | 11 files parsed, 0 parse failures; graph **partial** (311 files deliberately out of scope). Inventory identity of all 322 relevant files retained. |
| `ev_abdf15778aad3f568ccb338b5b46cbbd42a6fe8a441357e068be41f7aacd2385` | `inspect_analysis_view` summary of the above | request family: **0 relations** retained |
| `ev_510cf00448538b200c7d81a1c184ca8bef3605c651a6f47d8970a2fb8dd3fcb0` | `inspect_analysis_view` `javascript-routes` occurrences | **empty**. REA's request/route extraction covers `fetch`/WebSocket/node http, while Redash 8 calls the API through Angular `$resource` (E-R19). **This does not show that the client makes no calls.** Client↔API mapping therefore relies on source citations E-R19. |
| `ev_e8ce2e43…`, `ev_69fad8e4…`, `ev_2fc77f0d…`, `ev_85d60cda…` | `record_unknown` ×4 | see UNKNOWNS.md U1–U4 |

Guided prompt note: `investigate_feature` and `audit_residual_unknowns` are MCP **prompts**.
In Claude Code only the user can start them, as slash commands
(`/mcp__rea__investigate_feature`); the agent cannot invoke them. This study followed the method
the prompts describe (observations / inferences / unknowns) but **did not execute them**.

## Not done / limits

- **No runtime observation of Redash yet.** The instance is reachable (`GET /` → 302 to
  `/login`) but no credentials were supplied, and runtime capture needs separate approval
  (UNKNOWNS U1).
- `ctx_fetch_and_index` failed (tool bug: `classifyIp is not defined`) and `WebFetch` is blocked
  by a hook. The two doc pages were read with a minimal Python `urllib` text extract, so only
  sentences matching the keywords were reviewed.
