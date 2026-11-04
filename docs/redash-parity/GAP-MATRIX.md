# Gap matrix — Redash vs QueryBuilderEditor

Reference: Redash **8.0.0** (the running instance; source copied from the container) plus
**v26.9.0** source where noted. Destination: QueryBuilderEditor `bca3dcb` (1.0.13). Evidence IDs
refer to [EVIDENCE.md](EVIDENCE.md).

Status: **Present** / **Partial** / **Missing** / **Unknown**. Size: S (≤2 days), M (≤1 week),
L (multi-week). Priority: P1 next, P2 after P1 ships, P3 later, — not recommended.

Redash is a SQL-first tool for analysts. QueryBuilderEditor is a no-SQL builder for business
users that adds row-level scoping, which Redash does not have. "Missing" therefore never means
"copy Redash's design". Every row records the security difference.

| # | Area | Redash evidence | Destination evidence | Status | User value | Dependencies | Security implications | Size | Priority |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Visual query authoring | Redash is SQL-text authoring (`Query.query_text`, E-R20 area) — no visual builder | Full visual builder (BASELINE §3) | **Present (exceeds)** | — | — | — | — | — |
| 2 | Schema browsing | Schema sidebar (8.0.0 client) | Catalog sidebar with search, scoped and allowlisted (BASELINE §5) | **Present** | — | — | already scope-filtered | — | — |
| 3 | Snippets | `QuerySnippet` model (R8 `models/__init__.py:1193`) | none | **Missing (not applicable)** | Low — snippets are SQL text; business users don't type SQL | manual-SQL mode | n/a | — | — |
| 4 | Parameter controls | Typed params incl. dropdowns, query-based dropdowns, date ranges; text params "unsafe" (E-R09, E-R10, E-R11) | Typed runtime params on filters, prompted at run, always bound as `SqlParameter` (E-Q09) | **Partial** (no dropdown / query-based list / date-range presets) | Medium | none for presets; query-based lists need saved-query-to-saved-query reference | QBE never substitutes text into SQL, so the "unsafe text parameter" class does not exist here. Dropdown values must still be validated server-side | M | P3 |
| 5 | **Saved visualizations** | `Visualization` per query, free-form options, edit requires modify on the query (E-R01, E-R03, E-R04, E-R15, E-R16) | none — README roadmap item (E-Q15); grid only | **Missing** | **High** — the first step from "rows" to "answer"; prerequisite for dashboards | saved queries (present) | Must render with the **viewer's** scope and re-check query access/disabled server-side (D1, D4). No shared result cache | M | **P1** |
| 6 | Dashboards & widget layout | `Dashboard` + `Widget(visualization_id, width, options)` (E-R02) | none — roadmap | **Missing** | High | #5 | Dashboard access must never widen data access: each widget renders with the viewer's own query access and scope | L | P2 |
| 7 | Dashboard parameter mapping | Each widget parameter is mapped as dashboard-level (shared), widget-level, or static value (E-R24) | none | **Missing** | Medium | #6, #4 | values still bound per query and validated per widget's query; a dashboard-level value must never bypass a widget query's type validation | M | P3 |
| 8 | Execution status / async jobs | `enqueue_query` + job polling (`/api/jobs/:id`) (E-R06, E-R19) | synchronous request, 60 s command timeout (E-Q09) | **Partial** | Medium for long queries | none | job ids must be bound to the requesting user | M | P3 |
| 9 | Cancellation | `JobResource.delete` → `job.cancel()` (R8 `handlers/query_results.py:323`; R26 E-R27) | `CancellationToken` flows, but there is no UI cancel (BASELINE §3) | **Partial** | Low–medium | #8 | — | S | P3 |
| 10 | Result caching & freshness | `QueryResult` cached per (data source, query hash), shared across users; `max_age` (E-R06, E-R07, E-R08) | none (E-Q13) | **Missing (intentionally)** | Medium (speed, load on business DB) | #5/#6 for value | **Redash's shared cache is incompatible with row-level scoping.** Any QBE cache must key on (definition hash, parameter values, data-source policy version, **resolved scope fingerprint**) and be invalidated on scope-rule, catalog-policy, share and disable changes | M | P3 (only after dashboards show a need) |
| 11 | Scheduled execution | `Query.schedule`; refresh uses stored params and the **owner's** user id (E-R12, E-R20) | none; no scheduler (E-Q13) | **Missing** | Medium | needs background identity (U2), #10 | Background jobs have no HttpContext: today that means scope **Denied** and actor `anonymous` (E-Q07, E-Q08). Needs a new host contract | L | P3 |
| 12 | Alerts | `Alert` (unknown/ok/triggered, rearm, selector first/max/min, op) evaluates the query's **shared latest result** (E-R25); notification destinations (R8 `tasks/alerts.py:29-36`) | none | **Missing** | Medium | #11 | Redash's model (one shared result, many subscribers) would leak across scopes in QBE. An alert must be evaluated per subscriber scope, or limited to the owner. Real notifications are out of scope without explicit authorization | L | P3 |
| 13 | Query discovery / search | full-text `search_vector` on name/description/query (R8 Query model) | list page with client-side filter (QueryListPage) | **Partial** | Low–medium | — | results must stay limited to own + shared | S | P3 |
| 14 | Favorites | per-user `Favorite(object_type, object_id, user_id)` (E-R20) | one global `IsFavorite` flag, no access check (E-Q10, D2) | **Partial / defective** | Medium | — | fix: per-user rows, access-checked | S | **P2 (quick fix)** |
| 15 | Tags | `Query.tags` array (E-R20) | none | **Missing** | Low–medium | — | — | S | P3 |
| 16 | Collaboration (permissions on objects) | group ↔ data source, view_only vs full (E-R05), plus optional per-object `AccessPermission` grants (modify), whose UI is hidden unless `REDASH_FEATURE_SHOW_PERMISSIONS_CONTROL` is set (E-R23) | per-query Viewer/Editor shares + owner (BASELINE §4) | **Present (different model)** | — | — | QBE's per-object grants are finer-grained; keep them | — | — |
| 17 | Data-source administration | admin UI to add data sources | catalog policy, scope rules, record limit in admin UI; registration is raw SQL (README roadmap) | **Partial** | Medium | — | connection strings stay in config (names only) | M | P3 |
| 18 | Additional providers | many query runners | SQL Server only; enum lists others (D5) | **Partial** | Depends on customers | builder + executor per provider | scope predicates must be ported per dialect | L each | — (on demand) |
| 19 | API access (API keys) | per-query `api_key`, user API keys (R8 Query model, `ApiKey`) | host-authenticated JSON API only | **Missing** | Low for business users | — | an API key would bypass host auth. Not recommended without a host-controlled resolver | M | — |
| 20 | Dashboard sharing / public links | `ApiKey` token ⇒ anonymous public dashboard, admin/owner only (E-R13) | none | **Missing** | Medium | #6 | **Do not enable anonymous links by default.** QBE shares should stay identity-based. A future public link would need a frozen scope chosen by the sharer, with explicit opt-in by the host | M | P3 (identity-based sharing in P2 with #6) |
| 21 | Embedding | embed visualizations (embed view + API-key auth, E-R26); text params block embedding (E-R11) | SPA under `/querybuilder` only | **Missing** | Low–medium | #5, #20 | embedding runs in the host's auth context; no anonymous mode | M | P3 |
| 22 | Record limits on visual output | no chart-level limit found in 8.0.0 (bounded search; U3) | grid cap + export refusal (BASELINE §5) | n/a | — | #5 | QBE must define this itself (U3) | — | part of #5 |

## Version-specific notes (Redash, Observation)

- Parameter safety: 8.0.0 treats only `text` as unsafe; v26.9.0 also treats `text-pattern` as
  unsafe (E-R10).
- Docs: dynamic date parameters "aren't compatible with Scheduled Queries" (E-R11). Scheduled
  refresh uses stored parameter values and skips invalid ones (E-R12).
- Visualization permission rule is unchanged between 8.0.0 and v26.9.0 (E-R03, E-R04).
- v26.9.0 adds an optional automatic LIMIT on runs (E-R22). The result cache is still keyed on
  query text, not on the user (Inference, high confidence, from `run_query` reading
  `get_latest(data_source, query_text, max_age)`).

## How deep each area was traced

| Depth | Areas |
|---|---|
| Traced UI → API → permission → persistence/execution (source) | visualizations (#5), result execution and caching (#8, #10), parameter safety (#4) |
| Model + handler permissions (source) | dashboards/widgets (#6), public sharing (#20), alerts (#12), favorites/tags (#14, #15), object permissions (#16), embeds (#21), schedules (#11), cancellation (#9) |
| Client model only | dashboard parameter mapping (#7), chart options (#5) |
| Not runtime-observed | all of the above. Runtime capture on `localhost:5000` was not approved (U1) |

Priority summary: **P1** saved visualizations → **P2** dashboards with identity-based sharing
and the per-user favorites fix → **P3** parameter UX, async/cancel, scope-safe caching,
schedules/alerts (after the background-identity decision U2), tags/search, data-source
registration UI. Not recommended: API keys that bypass host auth, and anonymous public links by
default.

## Deliberate differences from Redash (proposed behavior, not parity)

1. No shared result cache across users (row-level scoping makes this a correctness and security
   requirement).
2. Visualizations always render from a server-loaded saved definition, so disabled and revoked
   access take effect immediately.
3. No anonymous public links by default.
4. The visual builder stays the only authoring path. Manual SQL remains a separate roadmap item
   that must solve catalog restrictions and scope injection, not just a "SELECT-only" check.
