# Super admin sign-in options — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A hub deployment can let super admins sign in through Entra ID or any OpenID Connect provider, and/or with
one-time codes generated on the hub's server — no new tables, no passwords.

**Architecture:** The package (`QueryBuilder.Editor`) owns recognition and codes: `SuperAdminRecognizer` learns a
provider kind (Entra | OpenIdConnect) and two new match kinds (`UserId` for the OIDC `sub`, `Code`); a new
`SuperAdminSignInCodes` issues and verifies HMAC codes keyed from the data-source key, single use via the existing
`LaunchReplay` table (`AppId = App.NoneId`); the sign-in endpoints gain a small server-rendered chooser page and a code
post. The host (`src/QueryBuilder.Hub`) maps `Hub:SignIn:*` settings, registers the OIDC scheme only when a provider is
configured, adds the `signin-code` command and a rate-limit bucket for the code post.

**Tech Stack:** .NET 8/10, ASP.NET Core, existing OpenIdConnect package (host only). No new packages, no migration.

**Spec:** `docs/superpowers/specs/2026-11-12-super-admin-sign-in-options-design.md` (amends §6 of
`docs/superpowers/specs/2026-11-10-multi-app-hub-design.md`).

## Global Constraints

- Two options, either or both: identity provider (`Kind` = `Entra` | `OpenIdConnect`) and one-time codes. Hub refuses
  to start with neither (message names the setting, never a value).
- Codes: `base64url(payload).base64url(HMAC-SHA256)`, payload `{ purpose:"qb-super-signin", aud:<Hub.Audience>, name,
  jti, iat, exp }` (whole seconds); key = HKDF-SHA256 over the data-source encryption key with a fixed label; lifetime
  default 15 min, 1–60; 30 s skew; single use via `LaunchReplay` (`AppId = App.NoneId`, key `(AppId, Jti)`); name must
  be in `SuperAdmins.CodeNames` (ordinal, case-sensitive); session user id `super:code:<name>`.
- Codes are created only by the server command `signin-code --name <n> [--minutes m]` — never by an endpoint. The
  command writes `SuperAdminCodeIssued` (hub-level: name, minutes, jti; never the code) and prints the code only after
  the audit write succeeds; it never runs migrations and never starts the web server.
- Code sign-in: `POST /querybuilder/signin/code` form field `code`; `Origin` header required and equal to the request's
  own origin; own per-IP rate-limit bucket in the host; any failure → the existing generic refusal page (403) + audit
  `SuperAdminSignInRefused` with the reason; success → existing super admin session (8 h / 1 h), audit
  `SuperAdminSignedIn` with method `code`, `303` to `/querybuilder/`.
- Generic OpenID Connect: allowlist `UserIds` (`sub`), `Emails` only when `email_verified` is `true` or `"true"`,
  `GroupIds` from `GroupsClaim` (default `groups`); `TenantIds` not used; userinfo endpoint read; Entra keeps today's
  rules (TenantIds required, oid/email/groups, `sub` never matches).
- Revocation is restart-only (options read once per process) — documented, not changed.
- Launch tokens can never make anyone a super admin (unchanged).
- New audit action `SuperAdminCodeIssued = 36`.
- Embedded mode untouched. Conventions per CLAUDE.md; both READMEs + hub deployment guide updated. No new packages.
- Branch `feature/hub`, local commits only; explicit `git add` paths; never push.

## Review Focus

1. **A code replayed, used after expiry, used on another deployment (`aud`), or for a name removed from the list** →
   Task 2 tests `Code_*` refusals, each audited with its reason.
2. **A generic OIDC user whose email is unverified, or whose `sub` collides with an Entra oid** → Task 1 tests
   (`Unverified_email_never_matches`, `Sub_matches_only_for_OpenIdConnect_kind`).
3. **Login CSRF on the code post** (a foreign page posting the attacker's code) → Task 2 `Code_post_without_matching_origin_is_refused`.
4. **A codes-only hub** (no OIDC scheme at all) → Task 3 `Codes_only_hub_starts_and_signs_in` and the sign-in page shows
   only the paste box.
5. **The command printing a code when the audit couldn't be written** (e.g. wrong connection string) → Task 3
   `Signin_code_command_prints_nothing_when_audit_fails`.

---

### Task 1: Provider kinds in the package (Entra or any OpenID Connect)

**Files:**
- Modify: `src/QueryBuilder.Editor/HubOptions.cs` (`SuperAdminOptions`), `src/QueryBuilder.Editor/Hub/SuperAdminRecognizer.cs`,
  `src/QueryBuilder.Editor/Hub/HubSession.cs` (tenant optional for non-Entra), `src/QueryBuilder.Editor/Hub/HubSessionCookie.cs`,
  `src/QueryBuilder.Editor/ServiceCollectionExtensions.cs` (startup validation)
- Test: `tests/QueryBuilder.Tests/Hub/SuperAdminTests.cs` (extend), `SuperAdminRecognizerTests.cs` (create if absent)

**Interfaces:**
- Produces:
  - `public enum SuperAdminProviderKind { Entra, OpenIdConnect }`; `SuperAdminOptions.ProviderKind` (default `Entra`),
    `List<string> UserIds`, `string GroupsClaim = "groups"`, `List<string> CodeNames` (used by Task 2).
  - `SuperAdminMatchKind` gains `UserId` and `Code`.
  - `Recognize(principal, options)`: OpenIdConnect kind → `sub` vs `UserIds`; email only with `email_verified`
    true/"true"; groups from `GroupsClaim`; no tenant check; stable id = `sub`. Entra kind unchanged.
  - `StillAllowed(kind, value, tenantId, options)`: `Code` → value in `CodeNames`; `UserId` → value in `UserIds` and
    kind is OpenIdConnect; Entra kinds unchanged (tenant required). Session tenant claim may be empty for non-Entra.
  - Startup (hub mode): provider configured (`SuperAdminSignInScheme` set) requires at least one entry for its kind
    (Entra: TenantIds + one of ObjectIds/Emails/GroupIds; OpenIdConnect: one of UserIds/Emails/GroupIds); refuse when
    neither a provider nor codes (`Hub.SignInCodes.Enabled`, Task 2) is configured.

- [ ] **Step 1: Failing tests:** `OpenIdConnect_sub_in_UserIds_matches`; `Unverified_email_never_matches`
  (`email_verified` false / missing / `"false"`); `Verified_email_matches` (bool true and string "true");
  `Groups_read_from_configured_claim`; `Sub_matches_only_for_OpenIdConnect_kind` (Entra kind with sub = listed UserId →
  refused); `Entra_rules_unchanged` (existing tests still pass); `StillAllowed_for_UserId_and_Code`;
  `Hub_refuses_to_start_with_no_sign_in_option`; `OpenIdConnect_kind_needs_an_allowlist_entry`.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS (net10.0; the SuperAdmin test classes on net8.0 too).
  **Step 5:** commit `Recognise super admins from any OpenID Connect provider`.

### Task 2: One-time sign-in codes in the package

**Files:**
- Create: `src/QueryBuilder.Editor/Hub/SuperAdminSignInCodes.cs` (issue + verify, pure apart from the clock),
  `src/QueryBuilder.Editor/Hub/SignInCodeOptions.cs`, `src/QueryBuilder.Editor/Hub/SignInPage.cs` (server-rendered HTML)
- Modify: `src/QueryBuilder.Editor/HubOptions.cs` (`SignInCodes`), `src/QueryBuilder.Editor/Endpoints/HubSuperAdminEndpoints.cs`,
  `src/QueryBuilder.Domain/Enums/Enums.cs` (`SuperAdminCodeIssued = 36`), the replay store (accept `App.NoneId`)
- Test: `tests/QueryBuilder.Tests/Hub/SignInCodeTests.cs`

**Interfaces:**
- Consumes: Task 1 `SuperAdminOptions.CodeNames`, `SuperAdminMatchKind.Code`.
- Produces:
  - `HubOptions.SignInCodes { bool Enabled; int DefaultMinutes = 15; }` (MaxMinutes is the constant 60).
  - `public sealed class SuperAdminSignInCodes(QueryBuilderEditorOptions options, TimeProvider clock)` with
    `IssuedCode Issue(string name, int? minutes)` (`record IssuedCode(string Code, string Jti, DateTimeOffset ExpiresUtc)`;
    throws `ArgumentException` for an unlisted name or minutes outside 1–60) and
    `CodeCheck Verify(string code)` (`record CodeCheck(string? Name, string? Jti, DateTimeOffset? ExpiresUtc, CodeRefusal? Refusal)`;
    `enum CodeRefusal { Malformed, BadSignature, WrongPurpose, WrongAudience, Expired, NotYetValid, LifetimeTooLong, NotListed }`).
    Constant-time signature compare. Registered in hub mode only.
  - `public interface ISuperAdminCodeIssuer { Task<IssuedCode> IssueAsync(string name, int? minutes, CancellationToken ct); }`
    — issues, writes `SuperAdminCodeIssued` in a hub-wide scope (via `IHubWideScopeFactory` — add the file to the
    architecture test allowlist), returns the code only after `SaveChanges` succeeds. Used by Task 3's command.
  - `GET /querybuilder/signin`: provider only → challenge as today; codes enabled → HTML page: a "Sign in with
    <ProviderDisplayName>" form (`POST /querybuilder/signin/provider`, which issues the challenge) when a provider is
    configured, and a paste box form (`POST /querybuilder/signin/code`). `HubOptions.ProviderDisplayName` (default
    "your organisation").
  - `POST /querybuilder/signin/code`: form only; `Origin` must equal `{scheme}://{host}` of the request (missing →
    refused); verify; insert replay `(App.NoneId, jti)` with expiry `exp + 30 s` (replayed → refused); sign in
    `HubSession.ForSuperAdmin("code:" + name, name, null, null, now, SuperAdminMatchKind.Code, name, "")`; audits as in
    Global Constraints (reason names only, never the code); 303 `/querybuilder/`.
  - Refusal reasons for audit: the `CodeRefusal` names plus `Replayed`, `OriginMismatch`, `CodesDisabled`.

- [ ] **Step 1: Failing tests** (`HubApiHost` with codes enabled; issue through `SuperAdminSignInCodes`):
  `Valid_code_signs_in_as_super_admin` (me: isSuperAdmin, user id `super:code:priya`); `Code_used_twice_is_refused`;
  `Code_expired_is_refused` (fake clock); `Code_for_another_audience_is_refused`; `Code_with_bad_signature_is_refused`;
  `Code_for_unlisted_name_cannot_be_issued_or_used` (issue throws; a code signed for a name later removed → refused after
  host restart with the new list); `Code_post_without_matching_origin_is_refused` (missing and foreign Origin);
  `Refusals_are_audited_with_reason_and_never_the_code`; `Signin_page_shows_only_enabled_options` (codes only / both);
  `Code_session_re_checked_against_CodeNames` (restart without the name → 401); `Codes_disabled_post_is_404`;
  `Issuer_writes_audit_before_returning`; `Launch_token_still_cannot_make_a_super_admin` (unchanged guarantee).
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS (net10.0; class on net8.0). **Step 5:** commit
  `Add one-time super admin sign-in codes`.

### Task 3: Host settings, OIDC for any provider, the `signin-code` command

**Files:**
- Modify: `src/QueryBuilder.Hub/HubHostOptions.cs` (replace `Entra` with `SignIn { Provider, Codes }`),
  `HubHost.cs`, `Program.cs`, `appsettings.json`
- Create: `src/QueryBuilder.Hub/SignInCodeCommand.cs`
- Test: `tests/QueryBuilder.Tests/HubHost/HubHostTests.cs` (update for the new settings), `HubHost/SignInOptionsTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces:
  - Settings: `Hub:SignIn:Provider { Kind ("" | Entra | OpenIdConnect), DisplayName, Instance, TenantId, Authority,
    ClientId, ClientSecret, CallbackPath "/signin-oidc", Scopes ["openid","profile","email"], GroupsClaim "groups" }`;
    `Hub:SignIn:Codes { Enabled false, DefaultMinutes 15 }`; `Hub:SuperAdmins { TenantIds, ObjectIds, UserIds, Emails,
    GroupIds, CodeNames }`.
  - Provider wiring: registered only when `Kind` is set. Entra: authority from Instance + TenantId (refuse `common`,
    `organizations`, `consumers`). OpenIdConnect: `Authority` must be absolute https; `GetClaimsFromUserInfoEndpoint =
    true`. Both: code + PKCE, `MapInboundClaims=false`, `SaveTokens=false`, `SignInScheme = QueryBuilder.HubExternal`.
    Required provider settings refused at startup by name (ClientId, ClientSecret, TenantId for Entra, Authority for
    OpenIdConnect).
  - Rate limiter: second bucket for `POST /querybuilder/signin/code` (route-matched like `/launch`), default 10/min per
    IP (`Hub:SignIn:Codes:PermitLimit`, `WindowSeconds` 60).
  - Command: `Program.cs` → if `args[0] == "signin-code"`: build configuration only (same sources as the web host),
    validate the code settings, construct the package services needed for `ISuperAdminCodeIssuer` without hosted
    services (no migrations), call it, print the code and expiry, exit 0; any error → message to stderr, exit 1, no code.

- [ ] **Step 1: Failing tests:** `Entra_kind_configures_entra_authority`; `OpenIdConnect_kind_reads_userinfo_and_uses_authority`;
  `Multi_tenant_entra_authority_is_refused`; `Codes_only_hub_starts_and_signs_in` (no OIDC scheme registered; page shows
  only the paste box); `Code_post_is_rate_limited`; `Signin_code_command_prints_a_working_code` (run the command entry
  method in-process against a test DB; the printed code signs in on a TestServer host with the same settings);
  `Signin_code_command_prints_nothing_when_audit_fails` (bad connection string → exit 1, empty stdout);
  `Old_Hub_Entra_settings_are_rejected_with_a_pointer` (startup message names `Hub:SignIn:Provider`).
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS (net10.0). **Step 5:** commit
  `Let the hub use any OpenID Connect provider or sign-in codes`.

### Task 4: Documentation

**Files:** `src/QueryBuilder.Hub/README.md` (sign-in section: choosing an option, Entra, generic OIDC with Google /
Okta / Auth0 / Keycloak notes on `sub` and `email_verified`, codes: command, lifetime, single use, restart to revoke,
security notes), `README.md`, `src/QueryBuilder.Editor/README.md` (hub mode section), `CLAUDE.md` "Where we left off".

- [ ] **Step 1:** write; every setting/route/limit checked against source with file:line in the report.
  **Step 2:** commit `Document super admin sign-in options`.

### Task 5: Acceptance

- [ ] **Step 1:** HubHost + Hub test classes on net10.0, SuperAdmin/SignInCode classes on net8.0 (full suites only if
  memory allows; say which ran).
- [ ] **Step 2:** Real hub host (throwaway launcher as in Part 4): codes-only config → run `signin-code`, sign in through
  the browser page, use the console; replay the same code → refused; both-options config with the fake provider →
  page shows both.
- [ ] **Step 3:** Opus whole-branch review; one fix wave; re-review. **Step 4:** report.
