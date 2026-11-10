# Hub Part 2 — Launch, sessions and super admin sign-in — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In hub mode, an app's server can hand a user to QueryBuilder with a signed launch token, QueryBuilder turns
it into its own session carrying app, user, role and scope, and allowlisted super admins can sign in through the
host's Entra ID scheme — with every request resolving to exactly one app (or none), never silently to Default.

**Architecture:** Server-side only (the UI for banners, the app switcher and the super admin console is Part 3). The
package owns everything security-deciding: the app registry tables, the token validator, the session cookie scheme and
its validation, the launch/logout/sign-in endpoints, super admin recognition and the registry API. The host (Part 4)
only adds HTTPS and its OIDC scheme. `AppContextAccessor` gains a session source; embedded mode is untouched.

**Tech Stack:** .NET 8/10, ASP.NET Core cookie authentication, EF Core 9/10, `Microsoft.IdentityModel.JsonWebTokens`
8.16.0 (MIT; already in the dependency graph through `Microsoft.Data.SqlClient` 7.1.0 — declared explicitly),
`Microsoft.AspNetCore.TestHost` (MIT, **test project only**: 8.0.x on net8.0, 10.0.x on net10.0). No other new packages:
Data Protection keys are stored with a small in-house `IXmlRepository`, not the EF Core Data Protection package.

**Spec:** `docs/superpowers/specs/2026-11-10-multi-app-hub-design.md` §4, §5, §6, §7, "Decided by the product owner
after the builder check". Part 1 (commit `769bf08`) and its notes in CLAUDE.md "Where we left off".

## Global Constraints

- Embedded mode (`Hub.Enabled = false`): behaviour, endpoints, JSON and startup unchanged; every existing test passes.
- Launch token (spec §5.1/§5.3, verbatim): RS256 or ES256 only (HMAC refused); `aud` = configured hub audience;
  `kid` header required; required claims `iss`, `sub`, `role`, `iat`, `exp`, `jti`; role ∈ {`Admin`, `Author`,
  `Viewer`}; lifetime (`exp − iat`) ≤ 120 s; clock skew 30 s; `jti` single use until expiry + skew; exactly one of
  `scope` (object key → array of strings) or `unrestricted: true`; ≤ 20 keys, ≤ 1,000 values per key, each value
  ≤ 200 chars, no empty value list; every scope key ∈ app's declared keys ∪ the scope keys of the app's own data
  sources; `returnUrl`, if present, must exactly equal one registered return URL.
- Refusals show a generic page (no reason text) and write a `LaunchRefused` audit entry with the reason; the token
  itself is never logged, stored or audited.
- Session: cookie HttpOnly, Secure, SameSite=Lax; **8 h absolute, 1 h idle**; app enabled re-checked per request with a
  cache ≤ 30 s; a new launch replaces the current session; logout clears it and redirects to the session's return URL
  (or `/querybuilder`).
- Super admin: never from a launch token. Recognised only from the host's sign-in scheme **and** the allowlist
  (`Hub.SuperAdmins`: Entra object ids, emails, group ids). Hub mode refuses to start with no allowlist entry at all.
- Resolution order for the app: explicit `UseApp`/`UseHubWide` → valid session's app (super admin: the app they chose
  to view) → `App.NoneId`. Never Default in hub mode. Hub-wide only inside super-admin registry handlers, never from
  session data.
- Every state-changing hub API call needs header `X-QueryBuilder-Request: 1` (CSRF defence on top of SameSite=Lax);
  the launch endpoint is exempt (it creates the session).
- Cross-app access → 404 (Part 1), now also logged at Warning (app, actor, entity type, id; no entity contents).
- Conventions: `QueryBuilderJson.Options`, `ReadFromJsonAsync`, hand-rolled mediator, child entities added through
  their `DbSet`, why-only comments. New audit actions: `LaunchSucceeded = 26`, `LaunchRefused = 27`,
  `SuperAdminSignedIn = 28`, `AppRegistryChanged = 29`.
- Branch `feature/hub`. One local commit per task is fine now (user approved local commits; **never push**).
- Before calling Part 2 done: full server suite on net8.0 and net10.0 with `QB_TEST_POSTGRES` and
  `QB_TEST_METADATA_SQLSERVER`, 0 skipped; client tests.

## Review Focus

1. **A launch for App A replayed or altered to act in App B** (swapped `iss`, a key registered to another app, a
   `kid` of a revoked key) must be refused → Task 2 tests.
2. **A session whose app was turned off, or deleted, mid-session**: the next request (after ≤ 30 s) is anonymous/None
   and gets 401, not Default's data → Task 3 test.
3. **A super admin who signed in but is not (or no longer) on the allowlist**, or a launch token claiming
   `role: "SuperAdmin"`: no super admin rights → Task 5 tests.
4. **Writes made during launch before any app is resolved** (replay store, refusal audit, `SeenUsers`): must not hit
   the None-context guard and turn a refusal into a 404 → Task 4 tests.
5. **Any endpoint reachable with another app's ids** once real sessions exist → Task 7 generated matrix.

---

### Task 1: Registry and system tables

**Files:**
- Create: `src/QueryBuilder.Domain/Entities/AppSigningKey.cs`, `AppReturnUrl.cs`, `SeenUser.cs`, `LaunchReplay.cs`,
  `DataProtectionKeyRecord.cs`, `IHubRegistryRecord.cs`, `IHubSystemRecord.cs`
- Modify: `src/QueryBuilder.Domain/Entities/App.cs` (`List<string> ScopeKeys`), `AppDbContext.cs` (DbSets, guard
  branches), `Configurations/` (one file per new entity), `tests/QueryBuilder.Tests/Hub/AppModelTests.cs`
- Create: migration `<timestamp>_AddHubRegistry`
- Test: `tests/QueryBuilder.Tests/Hub/HubRegistryModelTests.cs`

**Interfaces:**
- Produces:
  - `AppSigningKey : IHubRegistryRecord { Guid Id; Guid AppId; string Kid (≤64); string Algorithm ("RS256"|"ES256"); string PublicKeyPem (≤ 4000); DateTimeOffset CreatedAtUtc; DateTimeOffset? RevokedAtUtc; }` unique `(AppId, Kid)`.
  - `AppReturnUrl : IHubRegistryRecord { Guid Id; Guid AppId; string Url (≤ 2000); }` unique `(AppId, Url)`.
  - `App` implements `IHubRegistryRecord`; `App.ScopeKeys` stored like `DataSource.ScopeKeys`.
  - `SeenUser : IAppOwned { Guid Id; Guid AppId; string UserId (≤450); string? Name (≤200); string? Email (≤320); DateTimeOffset LastLaunchUtc; }` unique `(AppId, UserId)`.
  - `LaunchReplay : IHubSystemRecord { string Jti (PK, ≤200); Guid AppId; DateTimeOffset ExpiresUtc; }`.
  - `DataProtectionKeyRecord : IHubSystemRecord { int Id; string FriendlyName (≤200); string Xml; }`.
  - Guard: `IHubRegistryRecord` changes only when `IsHubWide` (as `App` today, which now uses the marker);
    `IHubSystemRecord` changes are allowed in **any** context, including None (they are never tenant data and are
    never exposed by an endpoint); all other non-app-owned entities keep today's "writable app" rule.
  - Migration: plain EF DDL; no data changes; `Apps.ScopeKeys` non-null default `''` via raw `EXEC` default like Part 1.

- [ ] **Step 1: Failing tests:** `Registry_records_are_hub_wide_only` (per-app and None contexts throw
  `CrossAppWriteException` on Added/Modified/Deleted `AppSigningKey`/`AppReturnUrl`; hub-wide passes via the sentinel
  interceptor pattern in `AppDbContextFilterTests`); `System_records_are_writable_without_an_app` (None context saves a
  `LaunchReplay`); `Seen_users_are_app_owned_and_filtered`; `AppModelTests` exception list becomes exactly
  {App, AuditLogEntry, MetadataSetting, AppSigningKey, AppReturnUrl, LaunchReplay, DataProtectionKeyRecord}.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** entities, configurations, guard branches, migration (generate with
  the EF command in CLAUDE.md). **Step 4:** `[MetadataSqlFact] AddHubRegistry_applies_on_top_of_AddApps` (fresh DB to
  latest; tables and unique indexes exist) + the existing migration tests → PASS; idempotent script twice on a
  throwaway LocalDB DB. **Step 5:** local commit `Add the hub's app registry and system tables`.

### Task 2: Launch token validator

**Files:**
- Create: `src/QueryBuilder.Application/Hub/LaunchClaims.cs`, `ILaunchTokenValidator.cs`, `LaunchRefusalReason.cs`
- Create: `src/QueryBuilder.Infrastructure/Hub/LaunchTokenValidator.cs`, `LaunchReplayStore.cs`
- Modify: `src/QueryBuilder.Infrastructure/QueryBuilder.Infrastructure.csproj`, `src/QueryBuilder.Editor/QueryBuilder.Editor.csproj` (declare `Microsoft.IdentityModel.JsonWebTokens` 8.16.0)
- Test: `tests/QueryBuilder.Tests/Hub/LaunchTokenValidatorTests.cs`, `tests/QueryBuilder.Tests/Hub/TestTokens.cs`

**Interfaces:**
- Produces:
  - `public sealed record LaunchClaims(Guid AppId, string AppKey, string UserId, string? Name, string? Email, HubRole Role, ResolvedDataScope Scope, string? ReturnUrl, string Jti, DateTimeOffset ExpiresUtc);`
  - `public enum HubRole { Viewer = 0, Author = 1, Admin = 2 }` (in Domain/Enums).
  - `public enum LaunchRefusalReason { Malformed, UnknownApp, AppDisabled, UnknownKey, RevokedKey, BadSignature, AlgorithmNotAllowed, WrongAudience, Expired, NotYetValid, LifetimeTooLong, Replayed, MissingClaim, UnknownRole, ReturnUrlNotRegistered, UndeclaredScopeKey, ScopeMissing, ScopeAndUnrestricted, ScopeTooLarge }`
  - `public sealed record LaunchResult(LaunchClaims? Claims, LaunchRefusalReason? Refusal, Guid? AppId);` (AppId set when the app was identified, for the audit entry)
  - `public interface ILaunchTokenValidator { Task<LaunchResult> ValidateAsync(string token, CancellationToken ct); }`
  - Implementation reads the app, keys, return URLs and data-source scope keys with a **hub-wide** scope it creates
    itself (`IServiceScopeFactory` + `IAppContextScope.UseHubWide()`), validates with `JsonWebTokenHandler`
    (`ValidAlgorithms` = RS256/ES256, `ValidateLifetime`, `ClockSkew` 30 s, `ValidAudience` from `Hub.Audience`),
    then checks lifetime, role, scope and return URL itself; the replay store inserts `LaunchReplay` (PK violation =
    replayed) and deletes expired rows opportunistically. `iss` selects the app by `App.Key`; the `kid` must belong to
    **that** app.
  - `TestTokens` (test project): builds RS256/ES256/HS256 tokens with chosen claims, lifetimes and keys.

- [ ] **Step 1: Failing tests** — one per refusal reason (each asserting exactly that reason), plus:
  `Valid_rs256_and_es256_tokens_are_accepted` (claims mapped, scope values preserved, `unrestricted` →
  `ResolvedDataScope.Unrestricted`); `A_key_registered_to_another_app_is_unknown_key`; `Swapped_iss_is_bad_signature_or_unknown_key`;
  `Same_jti_twice_is_replayed`; `Clock_skew_of_30s_is_tolerated_31s_is_not`; `Scope_key_declared_only_on_one_of_the_apps_data_sources_is_accepted`;
  `Scope_limits` (21 keys, 1,001 values, a 201-char value, an empty list → ScopeTooLarge / ScopeTooLarge / ScopeTooLarge / ScopeTooLarge).
  Use `[MetadataSqlFact]` with the `MetadataDb` helper (real registry rows).
- [ ] **Step 2–4:** run → FAIL; implement; run → PASS on both frameworks. **Step 5:** commit `Validate signed launch tokens`.

### Task 3: Hub session — cookie, resolution, scope, Data Protection

**Files:**
- Create: `src/QueryBuilder.Editor/Hub/HubSession.cs` (claim names, principal ↔ session), `HubSessionCookie.cs` (scheme + events), `HubAppSessionSource.cs`, `MetadataXmlRepository.cs` (Infrastructure is fine if it needs only `AppDbContext`)
- Create: `src/QueryBuilder.Application/Abstractions/IAppSessionSource.cs`
- Modify: `src/QueryBuilder.Infrastructure/Persistence/AppContextAccessor.cs`, `src/QueryBuilder.Editor/Identity/CurrentUserService.cs`, `src/QueryBuilder.Editor/DataScoping/CurrentDataScopeService.cs`, `src/QueryBuilder.Editor/ServiceCollectionExtensions.cs`, `src/QueryBuilder.Editor/HubOptions.cs`
- Test: `tests/QueryBuilder.Tests/Hub/HubSessionTests.cs`

**Interfaces:**
- Consumes: Task 2 `LaunchClaims`, `HubRole`.
- Produces:
  - `HubOptions` gains `string Audience`, `string CookieName = "__Host-qb-hub"` (path `/`), `TimeSpan AbsoluteLifetime = 8h`, `TimeSpan IdleTimeout = 1h`, `SuperAdminOptions SuperAdmins`, `string? SuperAdminSignInScheme`.
  - `public const string HubSessionScheme = "QueryBuilder.Hub";` registered by `AddQueryBuilderEditor` only when hub mode is on (cookie handler, `SlidingExpiration` with the idle timeout, absolute cap enforced in `OnValidatePrincipal` from an `issued_at` claim, `OnRedirectToLogin`/`OnRedirectToAccessDenied` → 401/403 for API paths).
  - Session claims: `qb:kind` (`app`|`super`), `qb:app` (Guid), `qb:user`, `qb:name`, `qb:email`, `qb:role`, `qb:scope` (JSON, or `*` for unrestricted), `qb:return`, `qb:issued`. Super admin: `qb:kind=super`, `qb:viewing` (Guid, optional), `qb:oid`.
  - `public interface IAppSessionSource { HubSessionInfo? Current { get; } }` with `public sealed record HubSessionInfo(bool IsSuperAdmin, Guid? AppId, string UserId, string DisplayName, string? Email, HubRole Role, ResolvedDataScope Scope, string? ReturnUrl);` (Application).
  - `AppContextAccessor`: in hub mode, without an explicit choice, reads `IAppSessionSource.Current` **once per scope and freezes it**: app session → its `AppId`; super admin → `viewing` app or None; nothing/invalid → None.
  - `OnValidatePrincipal`: reject (sign out) when past absolute lifetime, or when the app is missing/disabled (cached ≤ 30 s per app id in `IMemoryCache`).
  - `CurrentUserService` (hub mode): `UserId`/`DisplayName`/`Email` from the session; adds `HubRole? Role` and `bool IsSuperAdmin` to `ICurrentUserService` (embedded: `Role = null`, `IsSuperAdmin = false`).
  - `CurrentDataScopeService` (hub mode): enabled; `HostKeys` = the app's `ScopeKeys`; `GetAsync` = session scope; super admin = Unrestricted.
  - Data Protection in hub mode: `services.AddDataProtection().SetApplicationName("QueryBuilder.Hub")` with `MetadataXmlRepository : IXmlRepository` over `DataProtectionKeyRecord` (own scope; system records are writable without an app).

- [ ] **Step 1: Failing tests** (`TestServer` host built in the test with `AddQueryBuilderEditor(o => o.Hub.Enabled = true …)`
  against a `MetadataDb` database; helper signs a principal in via a test-only endpoint mapped by the test host, not by
  the package): `No_session_resolves_to_none_and_api_returns_401`; `App_session_resolves_to_its_app_and_frozen_per_request`;
  `Super_admin_without_viewing_app_resolves_to_none`; `Session_older_than_8h_is_rejected`; `Idle_over_1h_is_rejected`;
  `Disabling_the_app_ends_its_sessions_within_30s` (advance a fake `TimeProvider`/clear the cache; next request 401);
  `Session_scope_reaches_the_data_scope_service`; `Data_protection_keys_survive_a_new_host_instance` (two hosts on the
  same DB read each other's cookie); `Embedded_mode_registers_no_hub_scheme`.
- [ ] **Step 2–4:** FAIL → implement → PASS (both frameworks). **Step 5:** commit `Turn launches into hub sessions`.

### Task 4: Launch and logout endpoints

**Files:**
- Create: `src/QueryBuilder.Editor/Endpoints/HubLaunchEndpoints.cs`, `src/QueryBuilder.Application/Hub/RecordLaunchCommand.cs`
- Modify: `src/QueryBuilder.Editor/EndpointRouteBuilderExtensions.cs` (map only in hub mode, outside the authorized group), `src/QueryBuilder.Domain/Enums/Enums.cs` (audit actions), `client/src/lib/api-client.ts` (send `X-QueryBuilder-Request: 1` on every request), `src/QueryBuilder.Editor/Middleware/` (CSRF header check, hub mode only)
- Test: `tests/QueryBuilder.Tests/Hub/HubLaunchEndpointTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:
  - `POST /querybuilder/launch` (form field `token`, `application/x-www-form-urlencoded` only, anonymous): valid → sign
    in `HubSessionScheme` (replacing any session), upsert `SeenUser` in a scope with `UseApp(appId)`, audit
    `LaunchSucceeded` in that app (user, role, scope keys and value **counts**), `303` to `/querybuilder/`. Invalid →
    audit `LaunchRefused` (hub-level, `AppId` = identified app or null, reason name) in a hub-wide scope, sign out any
    session, `400` with a fixed generic HTML page ("This link to QueryBuilder isn't valid or has expired. Go back and
    try again."), no reason shown.
  - `GET /querybuilder/logout`: sign out; `302` to the session's return URL if it is still registered for that app,
    else `/querybuilder/`.
  - CSRF: in hub mode, `POST/PUT/PATCH/DELETE` under `/querybuilder/api` without `X-QueryBuilder-Request: 1` → `400`.

- [ ] **Step 1: Failing tests:** `Valid_launch_sets_the_session_and_redirects`; `Launch_upserts_the_seen_user_in_its_app`;
  `Refused_launch_is_400_generic_audited_with_reason_and_never_stores_the_token` (grep the audit row and logs captured by
  a test logger for the token string → absent); `Launch_accepts_only_form_posts` (GET → 405, JSON → 415);
  `Second_launch_for_another_app_replaces_the_session`; `Logout_redirects_to_a_registered_return_url_only`;
  `State_changing_api_call_without_the_header_is_400_in_hub_mode`; `Embedded_mode_has_no_launch_endpoint_and_no_header_rule`.
- [ ] **Step 2–4:** FAIL → implement → PASS; `cd client && npm test && npm run build` (copy the bundle per CLAUDE.md
  step 2). **Step 5:** commit `Add the launch and logout endpoints`.

### Task 5: Super admin sign-in

**Files:**
- Create: `src/QueryBuilder.Editor/Hub/SuperAdminOptions.cs`, `SuperAdminRecognizer.cs`, `src/QueryBuilder.Editor/Endpoints/HubSuperAdminEndpoints.cs`
- Modify: `src/QueryBuilder.Editor/ServiceCollectionExtensions.cs` (startup validation)
- Test: `tests/QueryBuilder.Tests/Hub/SuperAdminTests.cs`

**Interfaces:**
- Produces:
  - `SuperAdminOptions { List<string> ObjectIds; List<string> Emails; List<string> GroupIds; }`.
  - Startup (hub mode): refuse when `Audience` is empty, when `SuperAdminSignInScheme` is empty, or when all three
    lists are empty — message names the setting, never a value.
  - `GET /querybuilder/signin`: `Challenge(SuperAdminSignInScheme)` with redirect back to `/querybuilder/signin/complete`.
  - `GET /querybuilder/signin/complete`: authenticates the host scheme; `SuperAdminRecognizer.Recognize(ClaimsPrincipal)`
    matches `oid` / `http://schemas.microsoft.com/identity/claims/objectidentifier` against ObjectIds, `email` /
    `preferred_username` against Emails (case-insensitive), `groups` against GroupIds; match → sign in
    `HubSessionScheme` as `qb:kind=super`, audit `SuperAdminSignedIn` (hub-level), `302 /querybuilder/`; no match →
    `403` generic page, no session.
  - `POST /querybuilder/api/hub/view-app {appId}` (super admin only): re-issues the cookie with `qb:viewing`; unknown
    app → 404. Requests then resolve to that app; audit entries written by a super admin inside an app carry that
    app's id and `Actor = "super:<oid>"`.

- [ ] **Step 1: Failing tests** (test host registers a fake authentication scheme as the "host OIDC scheme"):
  `Allowlisted_object_id_email_or_group_becomes_super_admin` (3 cases); `Signed_in_but_not_allowlisted_is_403_without_session`;
  `Launch_token_with_role_SuperAdmin_is_refused_UnknownRole` (re-assert from the session side);
  `View_app_switches_resolution_and_audits_inside_that_app`; `Hub_mode_refuses_to_start_without_audience_scheme_or_allowlist` (3 cases).
- [ ] **Step 2–4:** FAIL → implement → PASS. **Step 5:** commit `Recognise super admins from the host's sign-in`.

### Task 6: Hub authorization, `/api/me` and the registry API

**Files:**
- Create: `src/QueryBuilder.Editor/Endpoints/HubRegistryEndpoints.cs`, `src/QueryBuilder.Application/Hub/Registry/*.cs` (commands/queries + validators), `src/QueryBuilder.Application/Abstractions/IHubWideScopeFactory.cs` + Infrastructure implementation
- Modify: `src/QueryBuilder.Editor/EndpointRouteBuilderExtensions.cs`, `src/QueryBuilder.Editor/Endpoints/MeEndpoints.cs`
- Test: `tests/QueryBuilder.Tests/Hub/HubAuthorizationTests.cs`, `HubRegistryTests.cs`

**Interfaces:**
- Produces:
  - Hub mode gates (replace `Authorization`/`AdminAuthorization` in hub mode; embedded unchanged): API group requires a
    hub session with a resolved app (app session, or super admin viewing an app) → else 401; admin group requires
    `Role == Admin` or super admin; registry group `/querybuilder/api/hub/apps*` requires super admin.
  - `IHubWideScopeFactory.CreateAsync()` → an `AsyncServiceScope` whose `IAppContextScope` is hub-wide; the only way
    registry handlers get a hub-wide context. Request scopes can no longer call `UseHubWide()` in hub mode
    (`AppContextAccessor` throws if asked from a request whose session isn't a super admin) — note the ruling.
  - Registry API (all audited `AppRegistryChanged` with before/after, never key material beyond fingerprint):
    `GET /api/hub/apps`, `POST /api/hub/apps {key,name}`, `PUT /api/hub/apps/{id} {name, isEnabled, scopeKeys}`,
    `POST /api/hub/apps/{id}/keys {kid, algorithm, publicKeyPem}` (validate PEM parses as RSA ≥ 2048 or EC P-256; return
    SHA-256 fingerprint), `DELETE /api/hub/apps/{id}/keys/{kid}` (revoke = set `RevokedAtUtc`, never hard delete),
    `PUT /api/hub/apps/{id}/return-urls {urls}` (absolute `https://` only, ≤ 20; `http://localhost` allowed). App key
    rule: `^[a-z0-9][a-z0-9-]{1,62}$`, unique; `default` can be renamed, never deleted.
  - `GET /api/me` adds `app {id,key,name}`, `role`, `isSuperAdmin`, `returnUrl`, `viewingApp` (hub mode only; embedded JSON unchanged).

- [ ] **Step 1: Failing tests:** gate matrix (no session / Viewer / Author / Admin / super-not-viewing / super-viewing ×
  api / admin / registry → expected 401/403/200); `Registry_rejects_bad_keys_and_urls` (1024-bit RSA, P-384, http
  remote URL, bad app key); `Revoked_key_no_longer_validates_launches`; `Me_shows_hub_fields_only_in_hub_mode`;
  `Request_scope_cannot_turn_hub_wide`.
- [ ] **Step 2–4:** FAIL → implement → PASS. **Step 5:** commit `Gate hub requests by session and add the app registry API`.

### Task 7: Cross-app endpoint matrix and refusal logging

**Files:**
- Create: `tests/QueryBuilder.Tests/Hub/CrossAppEndpointMatrixTests.cs`
- Modify: `src/QueryBuilder.Editor/Middleware/GlobalExceptionHandler.cs` (Warning log for `CrossAppWriteException` and for NotFound on hub requests is **not** wanted — only the cross-app write), handlers that load by id if the matrix finds a gap
- Test: same file

**Interfaces:**
- Consumes: Tasks 3–6 (test host, sessions).
- Produces: a test that enumerates `EndpointDataSource.Endpoints` under `/querybuilder/api` with a `{…:guid}` route
  parameter; seeds App A (query + share, visualization, dashboard + tile + share, data source, scope rule) and an App B
  Admin session; calls every endpoint with App A's ids substituted (bodies: a minimal valid JSON per endpoint from a
  table in the test; an endpoint missing from the table **fails the test**, so new endpoints must be added); asserts
  status 404 (or 400 for body validation that runs before loading — listed explicitly) and that a hub-wide snapshot of
  App A's rows is unchanged afterwards.

- [ ] **Step 1:** write the matrix → run → expect PASS where Part 1 already isolates, FAIL for any gap. **Step 2:** fix
  each gap at its handler (load through the filtered repository → NotFound). **Step 3:** Warning log on
  `CrossAppWriteException` with app id, actor, entity type, entity id; test with a capturing logger. **Step 4:** PASS
  both frameworks. **Step 5:** commit `Prove every endpoint keeps apps apart`.

### Task 8: Acceptance for Part 2

- [ ] **Step 1:** Full suite net10.0 then net8.0 with both database variables, 0 skipped; `cd client && npm test`.
- [ ] **Step 2:** Throwaway multi-target host from a packed `-p:Version=2.0.0-part2-test` package (CLAUDE.md
  "Acceptance recipe"), hub mode on, a fake OIDC scheme standing in for Entra, an empty throwaway metadata DB, a
  generated RSA key pair registered for app `agency-a` and an EC key for `agency-b` via the registry API; a `.mjs`
  script mints tokens with Node `crypto` and drives: launch A → `/api/me` shows A, Admin; create a query as A; launch B
  in the same cookie jar → A's query id is 404; replay A's token → refused; expired token → refused; disable A → A's
  old cookie is 401 within 30 s; super admin sign-in → view A → sees A's query; logout → redirect to registered URL.
- [ ] **Step 3:** Opus whole-branch review of Part 2 against the spec and Review Focus; one fix wave; re-review.
- [ ] **Step 4:** Update CLAUDE.md "Where we left off"; local commit; report to the user.
