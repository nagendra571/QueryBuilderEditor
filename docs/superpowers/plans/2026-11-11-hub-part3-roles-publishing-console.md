# Hub Part 3 — Roles, publishing, sharing, data-source lock and the super admin console — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In hub mode the four roles behave as the spec says (Admins and super admins see everything in their app,
Viewers see only dashboards, Admins publish dashboards to the app), shares stay inside one app, super admins lock, hand
over and move data sources and cap record limits per app, and super admins get a console in the SPA to run the
registry. Part 2's leftover hardening items are closed.

**Architecture:** Access decisions move from `string userId` to one `AccessActor` built from `ICurrentUserService`, so
every existing check (`SavedQueryAccess`, `DashboardAccess`, repositories, audit) learns "sees the whole app" and
"is a Viewer" in one place. Embedded mode builds an actor with neither flag, so it is unchanged by construction. Viewers
are limited by an endpoint allowlist (metadata on endpoints + one hub gate). Dashboard-context chart rendering is a
request-scoped grant checked by the existing render and parameter-options paths. Super-admin-only operations that span
apps (move, re-encrypt, hub audit) run through `IHubWideScopeFactory`, never the request scope. The client gains a
role-aware shell and a `/hub` console in the Zoning Map design.

**Tech Stack:** .NET 8/10, EF Core 9/10, React/TS/Tailwind/shadcn (existing). **No new packages.**

**Spec:** `docs/superpowers/specs/2026-11-10-multi-app-hub-design.md` §4, §7, §8.1–§8.6, §13 (all three sub-lists).
Part 2 plan `docs/superpowers/plans/2026-11-10-hub-part2-launch-and-sessions.md`; the "Part 3 must" list in CLAUDE.md.

## Global Constraints

- Embedded mode (`Hub.Enabled = false`): behaviour, endpoints, JSON and startup unchanged; every existing test passes.
  Spec §13: "Embedded mode keeps today's visibility … role checks branch on hub mode; tests cover both."
- Roles (spec §4, verbatim intent): Super admin = everything in every app; Admin = "see and manage every query,
  dashboard and share in the app", publish dashboards; Author = today's user inside the app; Viewer = "Dashboards only:
  see dashboards published to the app and those shared with them; use parameter controls; refresh. No query list,
  builder, SQL, chart settings, sharing UI or export".
- Only Admins and super admins Publish/Unpublish; a published dashboard is visible read-only to every Viewer and
  Author in the app (spec §8.2).
- Dashboard-context rendering (spec §13 product owner): the render and query-backed parameter-options calls carry
  dashboard id and tile id; server checks caller can see the dashboard, tile belongs to it and points at that
  visualization, dashboard and query in the caller's app; "It grants nothing else". Hub mode only.
- Tiles whose query is deleted or disabled show "unavailable"; Viewers are never offered export.
- Cross-app ids → 404 (Part 1). Inside an app, a resource the caller may not touch keeps today's status (403), except
  a Viewer on a non-allowlisted endpoint → 403 before any handler runs.
- Data sources (spec §8.4 + §13): super-admin-added sources are locked; locked → app admins read-only, only super admins
  change connection, on/off, catalog, scope mappings, record limit, scope keys; unlock/re-lock audited. Switch "App
  admins can add data sources" (default **off**) governs connections only (add, edit connection, test, on/off). Move
  between apps refused while any query, chart, dashboard tile, parameter dropdown or scope rule uses it; audit history
  stays with the old app.
- Record limit: effective = lowest of app ceiling, data source limit, host default (nulls ignored).
- Encryption key status and rotation are hub-wide and super-admin-only in hub mode.
- `IHubWideScopeFactory` stays the only way to a hub-wide scope (`UseHubWide()` throws in hub mode).
- New audit actions (continue numbering after `SuperAdminSignInRefused = 30`): `SuperAdminViewedApp = 31`,
  `DashboardPublished = 32`, `DashboardUnpublished = 33`, `DataSourceLockChanged = 34`, `DataSourceMoved = 35`.
- Conventions: `QueryBuilderJson.Options` + `ReadFromJsonAsync`, hand-rolled mediator, child entities through their
  `DbSet`, why-only comments, CSRF header `X-QueryBuilder-Request: 1` on every state-changing call (client already
  sends it), no `crypto.randomUUID`/`crypto.subtle`/`navigator.clipboard` in `client/src`.
- Branch `feature/hub`, one local commit per task (approved; **never push**). Stage explicit paths only.
- `dotnet test -m:1`, never two test runs at once (memory). Metadata tests stay in collection `MetadataDatabase`.
- Before calling Part 3 done: full server suite on net8.0 and net10.0 with `QB_TEST_POSTGRES` and
  `QB_TEST_METADATA_SQLSERVER`, 0 skipped; `cd client && npm test`; bundle rebuilt and copied (CLAUDE.md step 2).

## Decisions this plan makes (review these)

1. **User ids are compared exactly, and one app can't hold two ids that differ only by case.** The metadata columns use
   a case-insensitive collation, so SQL owner/share filters would let `MARY` see `mary`'s queries. Instead of a schema
   change, a launch whose `sub` matches an existing `SeenUser` of that app ignoring case but not exactly is refused
   (`LaunchRefusalReason.UserIdCaseConflict`, audited). Embedded mode keeps today's matching.
2. **App admins see super admins as "Super admin"** in audit entries and owner names; only super admins see the
   `super:<oid>` actor. Staff object ids are not the app's business.
3. **Viewers are limited by an allowlist of endpoints** (`.AllowViewers()` metadata), so a new endpoint is closed to
   Viewers until someone opts it in. Allowlist: `GET /api/me`, `GET /api/dashboards`, `GET /api/dashboards/{id}`,
   `POST /api/queries/{q}/visualizations/{v}/data`, `GET /api/queries/{q}/parameters/{name}/options` — the last two only
   with a dashboard context.
4. **A share target in hub mode must be a known person of the app**: a `SeenUser` of the app or a row of the relevant
   `AppUsers` view (query: its data source's view; dashboard: the views of the data sources behind its chart tiles).
   Anything else → 400 "Choose someone from the list."
5. **Locked data source writes by an app admin → 403** (they can see the source; hiding it would be wrong).
6. **Pointing a data source at the hub's own metadata database is refused by identity, not by string:** on test,
   create, connection update and catalog load, hub mode runs `SELECT @@SERVERNAME, DB_NAME(), SERVERPROPERTY('MachineName')`
   on the candidate (SQL Server only) and compares with the same values read once from the metadata DB. Aliases, IPs and
   `localhost` vs `.` can't get around it. PostgreSQL candidates can't be the metadata DB (it is SQL Server).
7. **Logout becomes `POST /querybuilder/logout`** (CSRF header required, returns `{ "redirectUrl": … }`); `GET` → 405.
8. **The Default row is turned off when the hub first claims a database**, and the registry ignores `isEnabled` changes
   for Default instead of refusing the whole update, so renaming Default works.
9. **Registry updates carry a `version`** (new `Apps.Version` concurrency token); a stale version → 409; an update that
   changes nothing writes no audit row and keeps the version.

## Review Focus

1. **A Viewer reaching query data without a dashboard** — calling render or options with no dashboard context, with a
   dashboard they can't see, with a tile from another dashboard, or with a tile id that points at a different
   visualization → 403/404, no rows → Task 4 tests `Viewer_render_without_valid_dashboard_context_is_refused` (4 cases).
2. **An Admin's "see everything" leaking across apps or into embedded mode** — an embedded host with a user named
   "Admin" or a role claim must not see others' queries; an Admin of App B must not see App A → Task 2 tests
   `Embedded_mode_never_sees_whole_app` and the Task 10 matrix.
3. **Publishing a dashboard whose charts sit on queries the Viewer can't otherwise see** — Viewer sees chart data with
   their own scope, but no query name, SQL, definition, export, or a way to run the query outside the dashboard →
   Task 4 `Published_dashboard_tile_shows_no_query_name_to_a_viewer`, `Granted_render_uses_the_viewers_scope`.
4. **An app admin changing a locked source through a side door** (scope keys, record limit, catalog policy, data scope,
   test, on/off, re-encrypt) → Task 6 `Every_admin_write_on_a_locked_source_is_403_for_app_admins` iterates every
   `/api/admin/data-sources/{id}/…` write endpoint from the endpoint list, so a new one can't be missed.
5. **Two super admins editing the same app** (one turns it off, the other renames it from a stale page) — the rename
   must not silently turn it back on → Task 8 `Stale_registry_update_is_409`.

---

### Task 1: Schema for roles, publishing, locks and app settings

**Files:**
- Modify: `src/QueryBuilder.Domain/Entities/Dashboard.cs`, `DataSource.cs`, `App.cs`, `src/QueryBuilder.Domain/Enums/Enums.cs`,
  `src/QueryBuilder.Infrastructure/Persistence/Configurations/` (Dashboard, DataSource, App), `src/QueryBuilder.Infrastructure/Persistence/MetadataModeGuard.cs`
- Create: migration `<timestamp>_AddHubRolesAndPublishing`
- Test: `tests/QueryBuilder.Tests/Hub/HubRolesSchemaTests.cs`, extend `MetadataModeGuardTests.cs`

**Interfaces:**
- Produces:
  - `Dashboard.PublishedToApp bool` (default false), `Dashboard.PublishedBy string?` (≤ 450), `Dashboard.PublishedAtUtc DateTimeOffset?`.
  - `DataSource.LockedBySuperAdmin bool` (default false).
  - `App.MaxRecords int?` (1–100,000 when set), `App.AppAdminsCanManageConnections bool` (default false),
    `App.Version int` (`IsConcurrencyToken`, starts 1).
  - `AuditAction` values 31–35 named in Global Constraints.
  - `MetadataModeGuard`: when it records `Mode = Hub` on an empty database it also sets the Default app's `IsEnabled = false`
    in the same transaction (through the hub-wide factory).
- Migration: plain EF DDL with defaults (`0`, `1`, `NULL`); **no `AlterColumn` on any `AppId`** (CLAUDE.md sharp edge);
  idempotent script applied twice to a throwaway LocalDB database.

- [ ] **Step 1: Failing tests:** `New_columns_exist_with_defaults` (`[MetadataSqlFact]`, fresh DB to latest, insert a
  dashboard/data source/app through raw SQL without the new columns → defaults read back);
  `Hub_claiming_an_empty_database_turns_Default_off`; `Embedded_claim_leaves_Default_on`.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement entities, configurations, enum values, guard change; generate the
  migration with the EF command in CLAUDE.md. **Step 4:** tests + existing migration tests → PASS; idempotent script
  twice on a throwaway LocalDB DB, then drop it. **Step 5:** commit `Add hub roles, publishing and lock columns`.

### Task 2: One access actor — Admins and super admins see the whole app

**Files:**
- Create: `src/QueryBuilder.Application/Common/AccessActor.cs`
- Modify: `Visualizations/SavedQueryAccess.cs`, `Dashboards/DashboardAccess.cs` (+ `DashboardMapper`), every caller listed by
  `grep -rn "SavedQueryAccess\.\|DashboardAccess\.\|OwnerId [!=]=" src/QueryBuilder.Application` (Delete/Disable/Share/RemoveShare/
  GetQueryShares/GetSavedQueries/GetSavedQueryById/SaveQuery/Visualization*/Render/Parameter*/Dashboard*/Audit),
  `Abstractions/Repositories.cs` + `src/QueryBuilder.Infrastructure/Repositories/SavedQueryRepository.cs`, `DashboardRepository.cs`
- Test: `tests/QueryBuilder.Tests/Hub/AppAdminVisibilityTests.cs`, existing unit tests updated to pass an actor

**Interfaces:**
- Produces:
  - `public sealed record AccessActor(string UserId, bool SeesWholeApp, bool IsViewer, bool IsSuperAdmin)` with
    `public static AccessActor From(ICurrentUserService user)`: `SeesWholeApp = user.IsSuperAdmin || user.Role == HubRole.Admin`,
    `IsViewer = !user.IsSuperAdmin && user.Role == HubRole.Viewer`. Embedded (`Role == null`, not super) → both false.
  - `SavedQueryAccess.CanView(SavedQuery, AccessActor)`, `CanEdit(SavedQuery, AccessActor)`, new `CanManage(SavedQuery, AccessActor)`
    (= owner or `SeesWholeApp`; used where code checked `OwnerId == UserId`: delete, disable, shares list/add/remove).
    Viewers: `CanView`/`CanEdit`/`CanManage` all false (Viewer role wins over shares, spec §8.3).
  - `DashboardAccess.CanView(Dashboard, AccessActor)` = `SeesWholeApp` ∨ owner ∨ share ∨ `PublishedToApp`;
    `CanEdit` = not Viewer ∧ (`SeesWholeApp` ∨ owner ∨ Editor share); `CanManage` = not Viewer ∧ (owner ∨ `SeesWholeApp`).
    `AccessLevelFor` returns `Owner` for `SeesWholeApp`.
  - Repositories: `ListForUserAsync(AccessActor actor, CancellationToken)` — when `SeesWholeApp`, no owner/share filter
    (the app query filter still applies); otherwise today's filter, plus `PublishedToApp` for dashboards.
  - Remove the old `string userId` overloads (one way to call).

- [ ] **Step 1: Failing tests** (`HubApiHost`, App A seeded with Mary's query, chart, dashboard and share):
  `App_admin_lists_opens_edits_disables_and_deletes_every_query_in_the_app`; `App_admin_manages_shares_on_others_queries`;
  `Super_admin_viewing_A_opens_As_query` (the Part 2 acceptance 403); `Author_still_sees_only_own_and_shared`;
  `Embedded_mode_never_sees_whole_app` (embedded host whose `ActorResolver` returns "Admin" and whose user has role Admin
  → still own + shared only); `App_admin_sees_audit_of_others_queries`.
- [ ] **Step 2:** FAIL. **Step 3:** implement; existing tests that passed `userId` now build `AccessActor` with both
  flags false. **Step 4:** full suite net10.0 → PASS. **Step 5:** commit `Let app admins and super admins see the whole app`.

### Task 3: Viewer role and publishing

**Files:**
- Create: `src/QueryBuilder.Editor/Hub/ViewerAllowedMetadata.cs`, `src/QueryBuilder.Application/Dashboards/PublishDashboardCommand.cs`
- Modify: `src/QueryBuilder.Editor/Hub/HubGates.cs`, `EndpointRouteBuilderExtensions.cs`, `Endpoints/DashboardEndpoints.cs`,
  `Endpoints/VisualizationEndpoints.cs`, `Endpoints/SavedQueryEndpoints.cs`, `Endpoints/MeEndpoints.cs`, `Dtos/DashboardDtos.cs`
- Test: `tests/QueryBuilder.Tests/Hub/ViewerAndPublishingTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ViewerAllowedMetadata` + `static TBuilder AllowViewers<TBuilder>(this TBuilder b)`; hub gate
    `RequireNotViewerUnlessAllowed()` on the API group: session role Viewer (not super) and endpoint lacks the metadata → 403.
    Decision 3 lists the five allowlisted routes.
  - `PUT /api/dashboards/{id}/published {published: bool}` → `PublishDashboardCommand(Guid DashboardId, bool Published)`:
    hub mode only (embedded → 404); caller must be Admin or super admin (else 403); sets `PublishedToApp`, `PublishedBy`
    (actor id), `PublishedAtUtc`; audits `DashboardPublished`/`DashboardUnpublished`; idempotent (no audit when unchanged).
  - `DashboardSummaryDto`/`DashboardDetailDto` gain `bool Published` (always false in embedded; additive JSON).
  - `/api/me` (hub) gains `permissions { canBuildQueries, canManageDashboards, canPublish, canAdmin, isViewer }` so the
    client never re-derives roles.

- [ ] **Step 1: Failing tests:** `Viewer_gets_403_on_every_endpoint_without_the_marker` (enumerate `EndpointDataSource`
  under `/querybuilder/api`, skip the five allowlisted, call each as Viewer → 403); `Viewer_lists_published_and_shared_dashboards_only`;
  `Author_sees_published_dashboards_read_only` (`accessLevel = Viewer`, PUT tiles → 403); `Only_admins_and_super_admins_publish`
  (Author → 403, Viewer → 403, Admin → 204, super viewing → 204); `Publish_is_404_in_embedded_mode`;
  `Unchanged_publish_writes_no_audit`; `Me_permissions_per_role` (4 roles).
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS. **Step 5:** commit `Add the Viewer role and dashboard publishing`.

### Task 4: Dashboard-context chart rendering

**Files:**
- Create: `src/QueryBuilder.Application/Dashboards/DashboardGrant.cs`
- Modify: `Visualizations/RenderVisualizationCommand.cs`, `Parameters/GetParameterOptionsQuery.cs`, `Parameters/ParameterOptionsService.cs`,
  `Dashboards/DashboardAccess.cs` (`ToTile`), `Endpoints/VisualizationEndpoints.cs`, `Endpoints/SavedQueryEndpoints.cs`,
  `client/src/lib/api.ts`, `client/src/features/dashboards/ChartTile.tsx`, `DashboardParameterBar.tsx`
- Test: `tests/QueryBuilder.Tests/Hub/DashboardContextRenderTests.cs`

**Interfaces:**
- Consumes: Task 2 `AccessActor`, `DashboardAccess.CanView`.
- Produces:
  - Render body gains optional `dashboardId`, `tileId`; options route gains optional query `?dashboardId=&tileId=`.
  - `public interface IDashboardGrant { void Allow(IEnumerable<Guid> savedQueryIds); bool Covers(Guid savedQueryId); }`
    (scoped, Application; empty by default).
  - `DashboardGrantResolver.TryGrantAsync(Guid dashboardId, Guid tileId, Guid queryId, Guid visualizationId, AccessActor, ct) → bool`:
    hub mode only; true when the dashboard loads (filtered), `CanView`, the tile belongs to it, `tile.VisualizationId ==
    visualizationId`, the visualization's `SavedQueryId == queryId`. On success `Allow` covers that query **and** the
    `queryList` source query ids named by that query's parameters and by the dashboard's parameters.
  - Render and options: `CanView(query, actor) || grant.Covers(query.Id)`; `ParameterOptionsService` (also used by
    `ParameterBinder`) uses the same rule for source queries. Viewers **must** pass a valid context (no fallback).
    Disabled query → existing `QueryDisabledException` (client shows "unavailable"). Scope = caller's own (unchanged).
  - `ToTile` in hub mode: a tile on a dashboard the caller can view is never `Restricted`; it exposes query id and
    visualization id/name/type, and the query **name only when `SavedQueryAccess.CanView`** (else null). Embedded keeps
    today's restricted tiles.
  - Client: `ChartTile` and the dashboard parameter bar pass `dashboardId`/`tileId`; the query-page chart passes none.

- [ ] **Step 1: Failing tests:** `Viewer_renders_published_tile_with_context`; `Viewer_render_without_valid_dashboard_context_is_refused`
  (no context / unseen dashboard / tile of another dashboard / tile pointing at another visualization → 403 or 404, no rows);
  `Granted_render_uses_the_viewers_scope` (scope `ProgramId=[10]` sees only 10's rows — use the PostgreSQL or SQL
  data-source fixture already used by render tests); `Published_dashboard_tile_shows_no_query_name_to_a_viewer`;
  `Grant_does_not_open_the_query` (same Viewer: `GET /api/queries/{id}` 403, run/export/preview 403);
  `Dashboard_query_list_parameter_options_run_as_viewer`; `Embedded_tiles_stay_restricted`.
- [ ] **Step 2:** FAIL. **Step 3:** implement server, then client (`npm test`). **Step 4:** PASS. **Step 5:** commit
  `Render dashboard charts for anyone who can see the dashboard`.

### Task 5: Sharing inside one app, and user-id case rule

**Files:**
- Create: `src/QueryBuilder.Application/Hub/ShareTargets.cs`
- Modify: `Commands.SavedQueries/ShareQueryCommand.cs`, `Dashboards/DashboardSharing.cs`, `DataSources/GetAppUsersQuery.cs`,
  `src/QueryBuilder.Application/Hub/LaunchRefusalReason.cs`, `src/QueryBuilder.Infrastructure/Hub/LaunchTokenValidator.cs`,
  `src/QueryBuilder.Infrastructure/Hub/LaunchRecorder.cs`, `client/src/hooks/useDataSources.ts`, share dialogs
- Test: `tests/QueryBuilder.Tests/Hub/HubSharingTests.cs`, extend `LaunchTokenValidatorTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/data-sources/{id}/app-users` in hub mode returns the view's users ∪ the app's `SeenUsers`, deduplicated by
    user id (ordinal) then email (ignoring case); each item gains `source: "launch" | "directory"`. New
    `GET /api/people` (hub mode only, 404 embedded) returns `SeenUsers` alone, for dashboard sharing.
  - `ShareTargets.IsKnownAsync(string userId, IEnumerable<Guid> dataSourceIds, ct) → bool` (Decision 4); hub-mode share
    commands refuse an unknown target with `ValidationException("Choose someone from the list.")` → 400.
  - `LaunchRefusalReason.UserIdCaseConflict` (append at the end); validator checks `SeenUsers` of the app (CI match,
    ordinal mismatch → refuse) before recording the replay.

- [ ] **Step 1: Failing tests:** `Picker_merges_seen_users_and_directory`; `Share_to_unknown_user_is_400_in_hub_mode`;
  `Share_to_seen_user_works`; `Embedded_share_rules_unchanged`; `Sharing_a_query_with_a_viewer_grants_no_query_access`;
  `Launch_with_id_differing_only_by_case_is_refused` (+ audit reason); `Same_id_relaunch_is_accepted`.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS. **Step 5:** commit `Keep shares to known people of the app`.

### Task 6: Data-source lock, connection switch, move between apps, hub-wide encryption key

**Files:**
- Create: `src/QueryBuilder.Application/DataSourceManagement/DataSourceLock.cs`, `MoveDataSourceCommand.cs`,
  `src/QueryBuilder.Editor/Endpoints/HubDataSourceEndpoints.cs`
- Modify: `Endpoints/AdminEndpoints.cs`, `DataSourceManagement/DataSourceCommands.cs`, `Commands.Admin/*.cs`,
  `Queries.Admin/GetAdminDataSourceDetailQuery.cs`, `GetAdminDataSourcesQuery.cs`, `Dtos/AdminDtos.cs`, the encryption-key handlers
- Test: `tests/QueryBuilder.Tests/Hub/DataSourceLockTests.cs`

**Interfaces:**
- Produces:
  - `DataSourceLock.EnsureCanChange(DataSource, AccessActor)` → `ForbiddenException` for an app admin on a locked source;
    `DataSourceLock.EnsureCanManageConnections(App, AccessActor)` → `ForbiddenException` unless super admin or
    `App.AppAdminsCanManageConnections`. Called by every admin write handler (connection ones call both). Embedded: no-ops.
  - Hub mode: connection routes no longer depend on `DataSourceManagementSettings.Enabled` (the key is required in hub
    mode); create by a super admin sets `LockedBySuperAdmin = true`, by an app admin `false`.
  - Admin DTOs gain `locked: bool`, `canEdit: bool`, `canManageConnection: bool`.
  - `PUT /api/admin/data-sources/{id}/lock {locked}` (super admin only, hub only) → audit `DataSourceLockChanged`.
  - `POST /api/hub/apps/{appId}/data-sources/{id}/move {targetAppId}` (registry group, super admin): runs in a hub-wide
    scope; 404 when the source isn't in `appId`; 400 for Default/None/disabled target; **409** with counts when any
    `SavedQuery.DataSourceId`, dashboard tile via visualization, query-list parameter source over it, or `DataScopeRule`
    uses it; otherwise rewrites `AppId` on the source row only and audits `DataSourceMoved` in **both** apps.
  - Hub mode: `GET /api/admin/encryption-key` and `/reencrypt` → 404; replaced by `GET/POST /api/hub/encryption-key[/reencrypt]`
    (super admin) whose handlers run in a hub-wide scope so every app's sources are counted and re-encrypted.

- [ ] **Step 1: Failing tests:** `Every_admin_write_on_a_locked_source_is_403_for_app_admins` (enumerate
  `/api/admin/data-sources/{id}…` non-GET endpoints); `Super_admin_changes_locked_source`; `Super_admin_created_source_is_locked`;
  `Lock_and_unlock_are_audited`; `Connection_routes_need_the_app_switch_for_app_admins` (off → 403, on → allowed,
  catalog/scope/limit on unlocked source allowed regardless); `Move_refused_while_used` (each blocker kind → 409);
  `Move_unused_source_keeps_history_in_old_app`; `Reencrypt_in_hub_covers_every_app`; `Admin_encryption_routes_404_in_hub`.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS. **Step 5:** commit `Lock, hand over and move data sources`.

### Task 7: Per-app record ceiling and the metadata-database refusal

**Files:**
- Create: `src/QueryBuilder.Application/Abstractions/ICurrentAppSettings.cs`, `src/QueryBuilder.Infrastructure/Hub/CurrentAppSettings.cs`,
  `src/QueryBuilder.Infrastructure/Hub/MetadataDatabaseIdentity.cs`
- Modify: `Common/RecordLimits.cs` and every `RecordLimits.Effective` caller (run, export, render, parameter options),
  `src/QueryBuilder.Infrastructure/Persistence/ConnectionTester.cs`, `DataSourceManagement/DataSourceCommands.cs`,
  `src/QueryBuilder.Infrastructure/Catalog/DataCatalogService.cs`
- Test: `tests/QueryBuilder.Tests/RecordLimitsTests.cs` (extend), `tests/QueryBuilder.Tests/Hub/MetadataDatabaseRefusalTests.cs`

**Interfaces:**
- Produces:
  - `RecordLimits.Effective(int? dataSourceMaxRecords, int? appCeiling, RecordLimitSettings settings)` = minimum of the
    non-null values, null when all null. The old two-argument overload is removed.
  - `ICurrentAppSettings { Task<int?> MaxRecordsAsync(CancellationToken) }` — the resolved app's `MaxRecords`, cached
    ≤ 30 s like the app-enabled cache; embedded → null.
  - `MetadataDatabaseIdentity` (singleton): reads `(@@SERVERNAME, DB_NAME(), SERVERPROPERTY('MachineName'))` from the
    metadata connection once; `IsSame(SqlConnection candidate, ct)` runs the same query on an open candidate connection.
    Hub mode: test, create, connection update and catalog load refuse a match with
    `ValidationException("A data source can't point at QueryBuilder's own database.")` (400; catalog → the existing
    catalog-error path). Embedded: not called.

- [ ] **Step 1: Failing tests:** `Effective_is_the_lowest_set_limit` (theory: 7 combinations incl. all-null);
  `App_ceiling_caps_run_export_and_render` (hub host, ceiling 5, source limit 50 → run returns 5 rows + truncated);
  `[MetadataSqlFact] Data_source_pointing_at_the_metadata_db_is_refused` (same DB reached as `(localdb)\MSSQLLocalDB` and as
  `(LOCALDB)\MSSQLLOCALDB` with the database name in other case → `IsSame` true; called directly, because managed sources always connect
  over `tcp:` and LocalDB can't be reached that way); `Hub_handlers_refuse_when_identity_matches` (fake
  `IMetadataDatabaseIdentity` returning true → test/create/update 400); `Other_database_on_same_server_is_allowed`.
  Expose it as `IMetadataDatabaseIdentity` (Application) so handlers can be tested with a fake.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS. **Step 5:** commit `Cap records per app and refuse the hub's own database`.

### Task 8: Registry hardening, app settings, view-app audit, hub audit view

**Files:**
- Modify: `Hub/Registry/RegistryCommands.cs`, `RegistryRules.cs`, `Endpoints/HubRegistryEndpoints.cs`,
  `Endpoints/HubSuperAdminEndpoints.cs`, `src/QueryBuilder.Infrastructure/Hub/` registry implementation,
  `Audit/GetAuditLogQuery.cs`, `Dtos/AuditDtos.cs`
- Create: `src/QueryBuilder.Application/Hub/Registry/GetHubAuditQuery.cs`
- Test: extend `tests/QueryBuilder.Tests/Hub/HubRegistryTests.cs`, create `HubAuditTests.cs`

**Interfaces:**
- Produces:
  - `UpdateAppCommand(Guid Id, int Version, string? Name, bool IsEnabled, List<string>? ScopeKeys, int? MaxRecords,
    bool AppAdminsCanManageConnections)`; `AppDto` gains `Version`, `MaxRecords`, `AppAdminsCanManageConnections`.
    Stale version → 409 (`ConcurrencyException`, existing mapping if present, else map `DbUpdateConcurrencyException` → 409).
    No change → 200 with the same DTO, **no audit row, no version bump**. Default: `IsEnabled` ignored (stays false),
    rename allowed. `MaxRecords` must satisfy `RecordLimits.IsValid`.
  - Turning an app off or revoking a key sets `SessionsValidAfterUtc = max(existing, now)`.
  - `POST /api/hub/view-app` writes `SuperAdminViewedApp` **inside the target app** (AppId = target, actor `super:<oid>`).
  - `GET /api/hub/audit?appId=&action=&before=&take=` (super admin; take ≤ 200, default 50; newest first) via the hub-wide
    factory; each item carries `appKey` (null for hub-level). Includes hub-level rows (`AppId` null).
  - App-facing audit (`GET /api/audit`): for a non-super-admin caller, an actor starting `super:` is returned as
    `"Super admin"` (Decision 2); `DetailsJson` keys named `oid` likewise masked.

- [ ] **Step 1: Failing tests:** `Stale_registry_update_is_409`; `No_op_update_writes_no_audit`; `Default_can_be_renamed_and_stays_off`;
  `Revoke_never_moves_the_stamp_backwards` (set stamp in the future, revoke → unchanged); `App_settings_round_trip`
  (ceiling, switch; 0 and 100,001 → 400); `View_app_is_audited_inside_the_target_app`; `Hub_audit_lists_all_apps_for_super_admin_only`
  (app admin → 403); `App_admin_sees_super_admin_actor_masked`.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS. **Step 5:** commit `Harden the registry and add the hub audit view`.

### Task 9: Logout as POST and the hub-wide factory guard rails

**Files:**
- Modify: `src/QueryBuilder.Editor/Endpoints/HubLaunchEndpoints.cs`
- Create: `tests/QueryBuilder.Tests/Hub/HubArchitectureTests.cs`
- Test: extend `HubLaunchEndpointTests.cs`

**Interfaces:**
- Produces:
  - `POST /querybuilder/logout` (CSRF header required): signs out, returns `200 {"redirectUrl": …}` (registered return
    URL or `/querybuilder/`); `GET /querybuilder/logout` → 405.
  - Architecture test: scans `src/**/*.cs` (excluding `obj`/`bin`) for `IHubWideScopeFactory`; the set of files must equal
    an explicit allowlist in the test (today's six files + files added by Tasks 6–8). A new caller fails the build until
    it is reviewed and added.

- [ ] **Step 1: Failing tests:** `Logout_is_post_only_and_needs_the_header`; `Logout_returns_registered_return_url_only`;
  `Only_reviewed_files_use_the_hub_wide_factory`.
- [ ] **Step 2–4:** FAIL → implement → PASS. **Step 5:** commit `Make logout a POST and pin the hub-wide factory callers`.

### Task 10: Tighten the cross-app matrix

**Files:**
- Modify: `tests/QueryBuilder.Tests/Hub/CrossAppEndpointMatrixTests.cs`

**Interfaces:**
- Consumes: Tasks 2–9 endpoints.
- Produces:
  - An expected-status table per endpoint × role (Viewer, Author, Admin, super viewing B) for App B callers on App A ids:
    always 404 or the listed pre-load 400/403 (Viewer gate 403 counts as "nothing reached a handler").
  - **B-on-B controls** for every body-id route: the same call with App B's own ids must succeed (2xx), proving the 404
    on A's ids is isolation, not a broken body.
  - Secrets list (strings that must never appear in any B response or log) gains App A's return-URL host and App A's `kid`.
  - New endpoints from Tasks 3–9 are in the table (the test already fails on unknown endpoints).

- [ ] **Step 1:** extend the table and controls → run → fix any gap at its handler (filtered load → 404).
- [ ] **Step 2:** PASS on net10.0 and net8.0. **Step 3:** commit `Check every endpoint per role, with same-app controls`.

### Task 11: Client — role-aware shell

**Files:**
- Modify: `client/src/hooks/useMe.ts`, `client/src/components/layout/AppShell.tsx`, `client/src/App.tsx`,
  `client/src/pages/DashboardPage.tsx`, `DashboardListPage.tsx`, `AdminDataSourcePolicyPage.tsx`, `AdminDataSourcesPage.tsx`,
  `client/src/features/admin/*Section.tsx`, `client/src/lib/api.ts`
- Create: `client/src/features/hub/permissions.ts` (pure: `navItemsFor(me)`, `canSee(route, me)`), `client/src/features/hub/HubBanner.tsx`,
  `AppSwitcher.tsx`, `ChooseAppPage.tsx`, `client/tests/permissions.test.ts`

**Interfaces:**
- Consumes: `/api/me` `permissions` (Task 3), `published` (Task 3), admin DTO `locked/canEdit/canManageConnection` (Task 6),
  `POST /logout` (Task 9), `POST /api/hub/view-app`, `GET /api/hub/apps`.
- Produces:
  - Viewer: nav shows only Dashboards; `/` redirects to `/dashboards`; query routes show a "not available for your role" page.
  - Dashboard page: Publish/Unpublish toggle when `canPublish`; "Published" chip in lists; read-only for non-editors.
  - Admin pages: locked sources show a lock chip and every control disabled with "Locked by a super admin"; connection
    controls disabled with an explanation when `!canManageConnection`.
  - Super admin: top-bar banner "Super admin · viewing <App name>", app switcher (enabled apps only), `/choose-app` when
    no app is viewed; logout button calls `POST /logout` then `window.location.assign(redirectUrl)`.
  - Embedded: `/api/me` has no hub fields → shell exactly as today.

- [ ] **Step 1: Failing tests:** `client/tests/permissions.test.ts` — nav items for each of 4 roles + embedded; `canSee`
  for `/`, `/queries/new`, `/admin`, `/hub/apps`.
- [ ] **Step 2:** `npm test` → FAIL. **Step 3:** implement (Zoning Map design; follow `.impeccable/surfaces/client-src.md`).
  **Step 4:** `npm test` PASS, `npm run build`, copy bundle per CLAUDE.md step 2. **Step 5:** commit `Show each hub role its own shell`.

### Task 12: Client — Apps console, hub audit and data-source lock/move

**Files:**
- Create: `client/src/pages/HubAppsPage.tsx`, `HubAppDetailPage.tsx`, `HubAuditPage.tsx`, `client/src/hooks/useHub.ts`,
  `client/src/features/hub/app-form.ts` (pure validation mirroring `RegistryRules`: key regex, kid regex, return URL
  canonical rule, ≤ 20 URLs, ceiling 1–100,000), `client/tests/app-form.test.ts`
- Modify: `client/src/App.tsx` (`/hub/apps`, `/hub/apps/:id`, `/hub/audit`), `AdminDataSourcePolicyPage.tsx` (Lock toggle,
  Move dialog for super admins), `EncryptionKeySection.tsx` (hub mode → `/api/hub/encryption-key`, shown only on the console)

**Interfaces:**
- Consumes: registry API (Part 2 + Task 8 `version`, settings), move/lock (Task 6), hub audit (Task 8).
- Produces:
  - Apps list (key, name, enabled, key count, URL count) + Create dialog.
  - App detail: name, enabled (not for Default), scope keys, record ceiling, "App admins can add data sources" switch —
    saved with `version`; 409 shows "Someone else changed this app. Reload to see their changes." Signing keys: paste
    PEM, algorithm, kid; table shows kid, algorithm, fingerprint, created, revoked; Revoke with confirm. Return URLs editor.
  - Hub audit: filters app/action, "Load older" paging via `before`.
  - Move dialog lists target apps; a 409 shows the counts returned.

- [ ] **Step 1: Failing tests:** `client/tests/app-form.test.ts` (each rule's accept/refuse pairs, same values as the
  server tests).
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** `npm test` PASS; build and copy bundle. **Step 5:** commit
  `Add the super admin console`.

### Task 13: Acceptance for Part 3

- [ ] **Step 1:** Full suite net10.0 then net8.0 (`-m:1`) with both database variables, 0 skipped; `cd client && npm test`.
- [ ] **Step 2:** Packed `-p:Version=2.0.0-part3-test` in the Part 2 throwaway host recipe (fake Entra scheme, empty
  throwaway metadata DB, a throwaway LocalDB data-source DB). `.mjs` script: super admin creates apps A and B, keys, return
  URLs, sets A's ceiling 5; adds a source to A (locked); launch A Admin → can't edit the locked source (403), can build a
  query and dashboard, publishes it; launch A Viewer → sees it, renders the chart with context (≤ 5 rows), 403 on query
  routes and on render without context; launch A Author with id `MARY` after `mary` → refused; super admin moves an unused
  source A→B, a used one → 409; stale registry update → 409; logout POST → registered URL.
- [ ] **Step 3:** Browser pass (Playwright library recipe in CLAUDE.md, if the MCP is unavailable): Viewer shell, admin
  publish toggle, locked source page, console create app + add key + revoke, app switcher + banner, http fake origin.
- [ ] **Step 4:** Opus whole-branch review against the spec and Review Focus; one fix wave; re-review.
- [ ] **Step 5:** Update CLAUDE.md "Where we left off" (Part 3 done, what Part 4 and the release docs must add: Viewer
  allowlist, user-id case rule, masked super admin actor, hub encryption-key routes, metadata-DB refusal); local commit;
  report to the user.
