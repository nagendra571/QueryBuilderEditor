# Roadmap — Redash-inspired capabilities for QueryBuilderEditor

Each phase ships as its own patch release of the single `QueryBuilder.Editor` package, following
the release workflow in `CLAUDE.md`. Nothing is published or pushed without an explicit ask.

Gates that apply to **every** phase (each phase must pass them before it ships):

- G-SEC: every new endpoint sits under the existing `/querybuilder` group (so the host's
  `Authorization` applies) and every data path goes `ValidateAsync → IDataScopeGuard.AuthorizeAsync
  → Build(definition, scope)`. There must be a negative test showing a denied/other-scope user gets
  no rows.
- G-LIC: any new dependency (direct and transitive) is audited; MIT/Apache/BSD/ISC/MPL-for-build
  only, nothing that requires payment.
- G-DB: a migration is added, auto-migration works on an existing 1.0.13 database, and the DBA
  idempotent script is regenerated.
- G-PKG: `npm run build` → wwwroot copy → `dotnet clean` + `dotnet pack` succeeds. `dotnet test`
  is green.
- G-DOC: both READMEs (root + package) are updated.

## Phase 1 — Saved visualizations (P1) — *proposed first slice*

Save a chart for an existing saved query, reopen it, and render it from the viewer's own
authorized results. Spec: [features/saved-visualizations/SPEC.md](features/saved-visualizations/SPEC.md).

- Depends on: saved queries, sharing, disable, scope, record limits (all present).
- Acceptance gate: the SPEC acceptance criteria, including the two-user scope isolation test and
  the disabled/revoked negative tests.

## Phase 2 — Dashboards (identity-based sharing) + per-user favorites fix (P2)

- Dashboard = ordered grid of widgets, where each widget references a visualization.
- Each widget renders through the Phase 1 render endpoint **as the viewer**. A viewer without
  access to a widget's query sees a "no access" placeholder, never the data. Sharing a dashboard
  grants no query access.
- Per-user favorites (`Favorite(UserId, ObjectType, ObjectId)`) with access checks. This replaces
  the global `IsFavorite` flag (D2), and a migration moves existing flags to the owner.
- Gate: a dashboard shared with user B, containing a query not shared with B, shows no rows for
  B (negative test).

## Phase 3 — Parameters UX and dashboard parameters (P3)

- Dropdown / list parameter types (validated server-side against the allowed values), date-range
  presets, and dashboard-level parameters mapped to widget parameters.
- Gate: an out-of-list value is rejected with 400 (negative test). All values are bound as
  `SqlParameter`.

## Phase 4 — Background execution foundation (P3, needs design decision U2)

- A host contract for non-HTTP identity/scope (e.g. `ResolveForUserAsync(userId)`). The default
  is **fail closed** (no resolver ⇒ background runs are refused when data scoping is on).
- Async run jobs with status and cancel (bound to the requesting user).
- Optional result cache keyed on (definition hash, parameter values, policy version, **scope
  fingerprint**), invalidated on scope/catalog/share/disable changes.
- Gate: a cached result for scope A is never served to scope B (negative test). Changing a scope
  rule invalidates the cache.

## Phase 5 — Schedules and alerts (P3, after Phase 4)

- Scheduled refresh runs as the owner via the Phase 4 contract. Alerts are evaluated per
  subscriber scope, never on a shared result.
- No real notifications are sent during development; destinations are stubbed. Enabling real
  delivery needs explicit authorization.
- Gate: a disabled query or revoked share stops scheduled runs and alerts (negative tests).

## More visualization types — *built on `feature/rea1`, not released*

Horizontal bar, Area, 100% stacked, Combo, Table, Pivot with shading, Funnel, Waterfall, Scatter/bubble,
Counter target, Trend and number formats. No migration and no new package. Spec, plan and results:
[features/visualization-types/](features/visualization-types/SPEC.md). Cohort was left out for now, as it
needs a four-column view prepared by a DBA, and is cheap to add on top of Pivot once someone has one.

## Later / on demand

Tags and search (S), data-source registration UI (M), additional providers (L each),
identity-based embedding (M). **Not recommended:** API keys that bypass host auth, and anonymous
public links by default.
