# QueryBuilder Hub: one QueryBuilder for many applications — design

Status: draft for review · 2026-11-10 · decided in chat with the product owner (no separate SPEC/PLAN before this).

## 1. Intent

The organisation runs many applications ("apps", one per agency or product). Instead of each app embedding its own
QueryBuilder, there will be **one central QueryBuilder** that every app sends its users to. Each app signs its users
in its own way, then hands them to QueryBuilder with a "Go to QueryBuilder" button that carries who the user is,
their role and their data scope values, **signed by the app's server**.

Inside QueryBuilder each app is a sealed area (a *tenant*): its data sources, queries, charts, dashboards, shares,
scope rules and audit entries are invisible to every other app. A small group of **super admins**, who belong to no
app and sign in with the organisation's **Entra ID**, run QueryBuilder itself and can see and manage everything.

Why now: several apps want QueryBuilder; running one copy per app multiplies hosting, upgrades and data-source
credentials. Success = an app can onboard by registering a public key and adding one button, and no user can ever
see another app's data, queries or dashboards.

## 2. Goals, non-goals, anti-goals

Goals
- Multi-app ("hub") mode in the `QueryBuilder.Editor` package, **off by default**.
- Signed launch from apps; QueryBuilder's own session afterwards.
- Roles: Super admin, Admin, Author, Viewer.
- Strict isolation between apps, enforced on the server and in the data layer.
- Published dashboards for dashboards-only Viewers.
- A deployable hub host app and a small launch-helper package for apps (including .NET Framework 4.8).

Non-goals (this release)
- Server-to-server launch tickets (client id + secret) — later.
- Several apps active at once in one browser — later.
- An allowed-hosts list per app for data sources — later (stopgap in §8.4).
- Signing app users in through Entra ID — apps keep their own sign-in.
- Cross-app sharing or cross-app data sources.

Anti-goals (failure even if it "works")
- Any path where a user of App A can read, list, infer the existence of, or change anything of App B.
- Any way for a launch token, a URL or a browser-side value to make someone a super admin.
- Role or scope values the browser could alter (unsigned query strings or headers).
- Breaking embedded (non-hub) consumers such as `TemplateBuilderClient`: with hub mode off, behaviour and the
  database must be unchanged apart from the additive migration.
- "Anyone who can sign in to Entra is a super admin".

## 3. The three deliverables

| Piece | Where | Packed? | Owns |
|---|---|---|---|
| `QueryBuilder.Editor` (existing) | `src/QueryBuilder.Editor` + Domain/Application/Infrastructure | yes, as today | Tenant model and isolation, roles, publish, Viewer rules, app registry + super admin console (UI in the SPA), the **launch-token validator**, hub options and startup validation |
| `QueryBuilder.Hub` (new) | `src/QueryBuilder.Hub` | **no** (deployed app) | HTTPS, Entra ID OIDC for super admins, cookie session, the `/querybuilder/launch` endpoint wiring, super admin allowlist config, Data Protection key storage, connection strings |
| `QueryBuilder.Launch` (new) | `src/QueryBuilder.Launch` | yes, separate NuGet | Builds the signed launch token and the auto-submitting form, for apps A/B/C. `netstandard2.0` (usable from .NET Framework 4.8 and .NET 8/10). No dependency on QueryBuilder |

Security-deciding logic lives in the package so it is tested and versioned with the schema. The hub host is thin and
deployment-specific, built on Microsoft's standard authentication libraries, like `QueryBuilder.Api` is today.

```
App A (own sign-in) ─ QueryBuilder.Launch → signed JWT (≈60 s) → auto form POST
   └→ Hub  POST /querybuilder/launch → package validator → session cookie (app, user, role, scope)
         └→ QueryBuilder.Editor in hub mode: every read/write filtered to App A
Super admin → Hub → Entra ID OIDC + "assignment required" + allowlist → console, all apps
```

## 4. Concepts and roles

**App (tenant)** — a registered application: id, key (slug, e.g. `agency-a`), display name, enabled flag, signing
public keys (several, each with a `kid`), allowed return URLs, declared scope keys, per-app maximum record limit,
switch "App admins can add data sources" (default off).

**Identity** — a user is the pair *(AppId, UserId)*. The same email from two apps is two different people.

| Role | Comes from | Can |
|---|---|---|
| Super admin | Entra ID sign-in + allowlist/group; **never** from a launch token | Everything in every app; app registry; encryption key and rotation; lock/unlock data sources; move a data source between apps; per-app record ceiling; per-app switches. Data scope = unrestricted |
| Admin | launch token `role=Admin` | Everything inside their app: Admin page for the app's data sources (except locked ones), **see and manage every query, dashboard and share in the app**, publish dashboards to the app |
| Author | `role=Author` | Today's normal user, inside their app: build, save, chart, run, export, share with people in the same app, build dashboards |
| Viewer | `role=Viewer` | Dashboards only: see dashboards **published to the app** and those shared with them; use parameter controls; refresh. No query list, builder, SQL, chart settings, sharing UI or export |

Role and scope are taken from the **latest launch only**; they are never stored as permanent grants. Shares
(Viewer/Editor on a query or dashboard) stay durable grants as today, but only within one app.

## 5. Launch (app → hub)

### 5.1 Token
JWT, signed by the app's **private key** (RS256 or ES256; HMAC not accepted). QueryBuilder stores only public keys,
so a leak of QueryBuilder's database cannot be used to impersonate an app.

| Claim | Required | Meaning |
|---|---|---|
| `iss` | yes | the app key; selects the registered app |
| `aud` | yes | fixed hub audience (configured in the hub, e.g. `querybuilder-hub`) |
| `kid` (header) | yes | which registered public key |
| `sub` | yes | the app's user id |
| `email`, `name` | email optional, name optional | display/picker; name falls back to email then sub |
| `role` | yes | `Admin` \| `Author` \| `Viewer` (anything else → refused) |
| `scope` | one of scope / `unrestricted` | object of key → array of strings, e.g. `{"ProgramId":["10"],"RegionId":["US2"]}` |
| `unrestricted` | | `true` = no row restriction for this user |
| `returnUrl` | optional | must exactly match one of the app's registered return URLs, else refused |
| `iat`, `exp` | yes | lifetime ≤ 120 s (helper default 60 s); 30 s clock skew |
| `jti` | yes | single use |

### 5.2 Transport
The app's page renders an auto-submitting HTML form that POSTs `token=<jwt>` to `/querybuilder/launch`. Never a query
string, never a header. `QueryBuilder.Launch` provides both the token builder and the form markup.

### 5.3 Validation (package, `LaunchTokenValidator`)
Refuse — with a generic error page and an audit entry carrying the reason, never the token — when any of:
unknown/disabled app; unknown `kid`; bad signature; algorithm not RS256/ES256; wrong `aud`; expired, not yet valid,
or lifetime > 120 s; `jti` already used (replay store keeps ids until expiry + skew); missing `sub`/`role`; unknown
role; `returnUrl` not registered; a scope key the app hasn't declared; neither `scope` nor `unrestricted`;
`scope` and `unrestricted=true` both present; a scope value list that's empty or over limits (≤ 20 keys,
≤ 1,000 values per key, value ≤ 200 chars).

A declared key absent from `scope` means *no value* for that key → views mapped to it return no rows (existing
fail-closed rule).

### 5.4 Session
On success the hub signs the user in with a QueryBuilder cookie (HttpOnly, Secure, SameSite=Lax), carrying app id,
user id, name, email, role, scope (or unrestricted), launch time, and the return URL. **8 hours absolute, 1 hour
idle.** Each request re-checks the app is still enabled (cached ≤ 30 s), so turning an app off ends its sessions.
A new launch replaces the current session (one app per browser); the UI shows a banner with the app name and a
"Back to <app>" link to the session's return URL. Logout clears the cookie and goes to the return URL.

Launch is audited (`LaunchSucceeded` / `LaunchRefused`), with app, user, role and scope keys/values counts — no token.

The "seen users" table (§7) is upserted on each successful launch.

## 6. Super admin sign-in

Entra ID via OpenID Connect in the hub host. A super admin must pass **both**:
1. Entra ID sign-in to an app registration with **"Assignment required" = Yes** (configured by IT), and
2. the hub's allowlist: Entra object ids and/or emails in a server-only settings file (`appsettings.Server.json`
   pattern already used for `TemplateBuilderClient`), and/or a configured Entra group id when IT creates one.

The hub **refuses to start** in hub mode if Entra sign-in is configured with neither an allowlist nor a group.
Super admin sessions use the same cookie lifetimes and are a separate identity kind (no app). Every action a super
admin takes inside an app is audited with the app id and a "super admin" marker, and the UI shows a banner
"Viewing <app> as super admin" with an app switcher.

## 7. Isolation and data model

Add `AppId` (non-null `Guid`, FK to `Apps`) to: `DataSources`, `SavedQueries`, `QueryVisualizations` (via query is
acceptable, but store it for filtering), `Dashboards`, `DashboardTiles`, `QueryShares`, `DashboardShares`,
`DataScopeRules`, `AuditLogEntries` (nullable for hub-level entries). Shares and owners are scoped by app: an owner
or share target is *(AppId, UserId)*.

New tables: `Apps`, `AppSigningKeys` (AppId, Kid, public key PEM/JWK, created, revoked), `AppReturnUrls`,
`AppScopeKeys` (or a column on `Apps`), `AppUsers` *(name clashes with the per-data-source `AppUsers` view — call
the table `SeenUsers`: AppId, UserId, Name, Email, LastLaunchUtc)*, `LaunchReplay` (jti, expiresUtc) unless a
distributed cache is used.

`DataSources` also gets `LockedBySuperAdmin` (bool). `Dashboards` gets `PublishedToApp` (bool) + `PublishedBy/At`.

Enforcement, belt and braces:
- An EF Core global query filter on `AppId` for the current app on every app-owned entity, plus explicit
  `AppId` checks in every command that loads by id. A super admin's context selects the app being viewed;
  hub-wide super admin pages use `IgnoreQueryFilters()` deliberately and only in super-admin-gated handlers.
- Cross-app ids return **404**, never 403.
- Tests: for every endpoint, a request as App B against App A's ids returns 404 and changes nothing (a generated
  matrix test over the endpoint list, so a new endpoint without a test fails the build).

Upgrade: the migration creates one app **"Default"** and assigns every existing row to it. With hub mode off, the
package always uses the Default app and behaves exactly as today (current `Authorization`, `AdminAuthorization`,
`ActorResolver`, `DataScope` options keep working).

`ICurrentUserService` gains `AppId`, `Role`, `IsSuperAdmin`; in embedded mode they come from today's options
(Admin = passes `AdminAuthorization`, otherwise Author; never Viewer, never super admin).

## 8. Behaviour by area

### 8.1 Queries
Owner = Author/Admin who created it. Admins see and manage every query in their app (open, edit, delete, disable,
manage shares). Viewers: no access to any query endpoint except rendering charts on dashboards they can see.

### 8.2 Dashboards and publishing
Only Admins (and super admins) can Publish/Unpublish. A published dashboard is visible read-only to **every Viewer
(and Author) in the app**. Chart tiles on a dashboard a Viewer can see render through the existing render endpoint
with the **Viewer's own scope**; the query's existence grants the Viewer no other access (no name of the query,
no SQL, no definition beyond what the chart needs to draw). Tiles whose query is deleted or disabled show
"unavailable". Dashboard parameters work; parameter dropdowns backed by queries run as the Viewer (CanView rule
extended to "via a visible dashboard"). Export from dashboards is not offered to Viewers.

### 8.3 Sharing
Picker = SeenUsers of the same app ∪ the data source's `AppUsers` view (deduplicated by user id, then email).
The server refuses any share whose target isn't *(same AppId, user id)*. Sharing a query with a Viewer is allowed
but grants nothing a Viewer can use beyond dashboards (Viewer role wins).

### 8.4 Data sources
- App admins add/edit/test/turn off their app's sources **only if the app's "App admins can add data sources" switch
  is on** (super admin sets it; default off). This is the stopgap for network probing until allowed hosts exist.
- Sources added by a super admin are **locked**: app admins and users can use them, app admins see their settings
  read-only; only super admins change anything (connection, on/off, catalog, scope mappings, record limit,
  scope keys). A super admin can **unlock** (hand over) and **re-lock**; both audited.
- Config-defined sources (`ConnectionStringName` from appsettings) belong to Default, which is never used in hub mode;
  a hub has none.
- Per-app maximum record limit set by super admins; the effective limit is the lowest of app ceiling, data source
  limit, host default.
- The data-source encryption key and rotation are hub-wide and super-admin-only.

### 8.5 Data scope
The app's declared scope keys replace the host's `DataScope.Keys`; the data source's own scope keys (1.5.0) still add
on top. The session's scope replaces `DataScope.Resolver`. `unrestricted=true` → `DataScope.Unrestricted`. Super
admins are unrestricted. Everything downstream (`DataScopeGuard`, evaluator, SQL builder) is unchanged.

### 8.6 Admin page and super admin console
App admins: today's Admin page, filtered to their app, plus the Publish action on dashboards.
Super admins additionally: an **Apps** console — create app, edit name/key, enable/disable, add/revoke signing keys
(paste PEM/JWK; show fingerprint), return URLs, scope keys, record ceiling, "App admins can add data sources";
an app switcher; data source lock/unlock and move-between-apps; encryption key section (moves here).

## 9. Hub host (`src/QueryBuilder.Hub`)
- HTTPS only (HSTS); `X-Frame-Options`/`frame-ancestors 'self'`; antiforgery for cookie-authenticated
  state-changing API calls (the SPA sends a header token); rate limit on `/querybuilder/launch`.
- Cookie authentication + OIDC (Microsoft.Identity.Web or plain OpenIdConnect) for super admins.
- Data Protection keys persisted to a shared location so sessions survive restarts and multiple instances.
- Settings: hub audience, Entra tenant/client ids, super admin allowlist/group, connection strings, data-source
  encryption key — secrets only in server-only files or environment, never in the repo.
- Calls `AddQueryBuilderEditor(o => o.Hub.Enabled = true …)` and `MapQueryBuilderEditor()`.

## 10. `QueryBuilder.Launch` (app-side helper)
`netstandard2.0`, depends only on `System.IdentityModel.Tokens.Jwt` (MIT). API sketch:
`LaunchToken.Create(appKey, kid, privateKey, user, role, scope|unrestricted, returnUrl, lifetime=60s)` and
`LaunchForm.Html(hubUrl, token)`. Ships with an integration guide: generating a key pair (PowerShell / openssl),
registering the public key, adding the button (ASP.NET Core, MVC 5 on 4.8), and the raw token spec for non-.NET
apps. Never handles a private key it didn't receive from the caller.

## 11. Constraints
- Follow CLAUDE.md conventions: hand-rolled mediator, `QueryBuilderJson.Options`, `DbSet` adds for children,
  DBA schema script regenerated, both READMEs, tests on net8.0 and net10.0 with both database variables.
- Metadata store stays SQL Server. No paid libraries; check licences of any new dependency.
- Version: this is a minor bump at least (**2.0.0** suggested, given the role and admin-visibility changes in hub
  mode, even though embedded mode is unchanged).

## 12. Decided in chat (product owner)
- One central QueryBuilder; apps sign users in and hand them over; package continues to ship; a new hub host app
  consumes it (my recommendation, accepted: security logic in the package, host thin and unpacked, plus a launch
  helper package).
- Role instead of `IsAdmin`; Admins see every query and dashboard in their app.
- Dashboards-only Viewer role; dashboards **published by Admins are visible to every Viewer in the app**.
- Super admins via Entra ID; interim: **both** "assignment required" and a config allowlist.
- Both app admins and super admins can add data sources; super-admin-added sources are locked to super admins;
  super admins can unlock/hand over and re-lock.
- Sharing picker: users seen via launch + the `AppUsers` view.
- Allowed hosts per app: later.
- Defaults accepted: signed token by form POST; one app per browser; 8 h / 1 h sessions; role and scope from latest
  launch; app-declared scope keys with unknown keys refused; existing data → "Default" app; embedded mode unchanged.

## 13. My calls (review these)
- Viewers can use parameters and refresh, cannot export.
- Lock covers the whole data source, not individual fields.
- "App admins can add data sources" switch, default off (stopgap for allowed hosts).
- Token lifetime ≤ 120 s, RS256/ES256 only, jti replay store; limits on scope sizes.
- Cross-app ids → 404; generated cross-app test matrix over every endpoint.
- `SeenUsers` table name; `PublishedToApp` on dashboards; version 2.0.0.

### Settled after the builder check (my calls)
- **Embedded mode keeps today's visibility.** "Admins see every query and dashboard in the app" and every other
  role-based rule applies **only in hub mode**. Embedded admins keep today's rules (own + shared). The role checks
  branch on hub mode; tests cover both.
- **Scope keys a token may carry** = the app's declared keys ∪ the scope keys of the app's own data sources (1.5.0).
  Anything else is refused.
- **Moving a data source between apps is refused while anything uses it** (any query, chart, dashboard tile,
  parameter dropdown or scope rule). An unused source moves with its own settings; its audit history stays with
  the old app. No cascading re-tenanting.
- **The hub has its own metadata database.** The package records the mode in the database the first time it runs
  (hub or embedded) and refuses to start if the other mode points at it, naming the reason.
- **"Default" is never used in hub mode**: a hub database starts empty (embedded-owned databases are refused). The
  registry may list it but refuses to add keys or return URLs to it or to enable it, a launch with `iss` = "default" is
  refused as an unknown app, and super admins cannot view it.
- **The "App admins can add data sources" switch governs connections only**: adding, editing connection details,
  testing and turning on/off. Catalog, scope mappings and record limits on an unlocked source depend only on the
  lock. A handed-over source's connection stays read-only to the app admin while the switch is off.

### Decided by the product owner after the builder check
- **Seeing a dashboard grants drawing its charts (hub mode only).** Anyone who can see a dashboard — published to
  their app, or shared with them directly — can render its chart tiles, with their own scope. The render and
  query-backed parameter-options calls carry the dashboard id and tile id; the server checks the caller can see that
  dashboard, that the tile belongs to it and points at that visualization, and that dashboard and query are in the
  caller's app. It grants nothing else: no query name or id beyond what the tile already shows, no SQL, no
  definition, no running or exporting the query on its own. Without a dashboard context the existing
  `SavedQueryAccess.CanView` rule applies unchanged. Embedded mode keeps today's restricted tiles.
- **Deployment is not yet known, so design for several instances:** Data Protection keys, the `jti` replay store and
  the app-enabled check all live in the metadata database (the app-enabled check may cache ≤ 30 s per instance).
  Nothing correctness- or security-relevant is held only in one process's memory.

## 14. Left to the builder
Exact entity/column names and migration split; replay store implementation (table vs cache); how the SPA receives
role/app (extend `GET /api/me`); UI layout of the Apps console within the Zoning Map design system; antiforgery
mechanism; phasing of the implementation into reviewable slices.
