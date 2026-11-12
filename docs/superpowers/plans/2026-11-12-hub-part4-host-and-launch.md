# Hub Part 4 — the hub host and the `QueryBuilder.Launch` package — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An organisation can deploy one QueryBuilder hub (`src/QueryBuilder.Hub`, never packed) with HTTPS, Entra ID
sign-in for super admins and a rate-limited launch endpoint, and any app — .NET 8/10 or .NET Framework 4.8 — can send
its users there with a few lines of code using the new `QueryBuilder.Launch` package; the 2.0.0 documentation is in
place.

**Architecture:** The host is thin: configuration binding, transport security (HTTPS, HSTS, forwarded headers, security
headers), a rate limiter on `/querybuilder/launch`, and an OpenIdConnect scheme whose `SignInScheme` is the package's
`QueryBuilder.HubExternal`. Everything security-deciding stays in `QueryBuilder.Editor` (Parts 1–3). The host's
wiring lives in a static `HubHost` class so tests can build it on `TestServer`. `QueryBuilder.Launch` is a
dependency-free `netstandard2.0` library that builds an RS256/ES256 JWT with `System.Security.Cryptography` and renders
the auto-submitting form.

**Tech Stack:** .NET 10 (host), `netstandard2.0` (Launch), `Microsoft.AspNetCore.Authentication.OpenIdConnect`
10.0.x (MIT, Microsoft; host only — **new package, see Decisions**), ASP.NET Core built-in rate limiting.

**Spec:** `docs/superpowers/specs/2026-11-10-multi-app-hub-design.md` §3, §5.1–§5.2, §6, §9, §10, §11; CLAUDE.md
"Part 4 must" and "Release docs".

## Global Constraints

- The host is `src/QueryBuilder.Hub`, `net10.0` only, `IsPackable=false`, references `QueryBuilder.Editor` by
  ProjectReference. It is a deployable app, never a NuGet package.
- Host must (CLAUDE.md "Part 4 must", verbatim): HTTPS + HSTS, `UseForwardedHeaders`, `UseExceptionHandler`, **no
  `UsePathBase`**, `frame-ancestors 'self'`, a rate limit on `/launch`; OIDC registered with
  `SignInScheme = QueryBuilder.HubExternal`; **no Data Protection persistence configured by the host** (hub mode takes it
  over); the same `EncryptionKey` and `PreviousEncryptionKeys` on every instance.
- Secrets (Entra client secret, data-source encryption key, connection strings) only in environment variables or a
  server-only file; never in the repo. Committed `appsettings.json` holds placeholders only.
- `QueryBuilder.Launch`: `netstandard2.0`, no dependency on QueryBuilder; never handles a private key it didn't receive
  from the caller; whole-second `iat`/`exp`; default lifetime 60 s, maximum 120 s; refuses a scope whose serialised form
  exceeds 6,144 UTF-8 bytes (the hub's own limit) before signing; RS256 (RSA 2048–8192 bits) or ES256 (P-256) only;
  `kid` must match `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`; role ∈ {Admin, Author, Viewer}; exactly one of scope /
  unrestricted; token claims exactly as spec §5.1.
- Embedded mode and every Part 1–3 behaviour unchanged; all existing tests pass.
- Conventions: why-only comments; both READMEs document every package-facing behaviour; no paid libraries.
- Branch `feature/hub`, local commits only, never push; explicit `git add` paths. Version bump, DBA-script regeneration
  and packing for release happen only when the user asks (CLAUDE.md release workflow).
- Done = full server suite on net8.0 and net10.0 with both DB variables (0 skipped if Docker is available; otherwise
  say so), client tests, Launch round-trip tests, host tests, and an end-to-end run (below).

## Decisions this plan makes (for the user to confirm — recorded in `docs/superpowers/plans/2026-11-12-hub-part4-questions.md`)

1. **`QueryBuilder.Launch` has no package dependencies** (the spec suggested `System.IdentityModel.Tokens.Jwt`). A JWT is
   three base64url parts and one signature from `RSA.SignData`/`ECDsa.SignData`, which `netstandard2.0` has. No
   dependency means no binding-redirect trouble in .NET Framework 4.8 apps. Cost if wrong: swap the internals later; the
   public API doesn't change.
2. **New package in the host: `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.x** (MIT, Microsoft), not
   Microsoft.Identity.Web (bigger, more moving parts). Host only, never in the NuGet package.
3. **Rate limit on `/querybuilder/launch`: 30 requests per minute per client IP, fixed window, 429 with `Retry-After`.**
   Configurable (`Hub:LaunchRateLimit:PermitLimit`, `WindowSeconds`).
4. **Forwarded headers are trusted only from configured proxies** (`Hub:ForwardedHeaders:KnownProxies` /
   `KnownNetworks`); with none configured, only loopback is trusted (ASP.NET Core default). Wrong proxy settings make
   every request look like plain http and break the `__Host-` cookies; the deployment guide says how to check this.
5. **Entra client authentication by client secret** from configuration (`Hub:Entra:ClientSecret`); certificate support is
   left for later.
6. **`/healthz`** returns 200 `ok` (no details) for load balancers; anonymous.
7. **`QueryBuilder.Launch` version 1.0.0**, separate from QueryBuilder.Editor's version.

## Review Focus

1. **A host behind a reverse proxy that terminates TLS** — without correct forwarded headers the hub sees http, the
   `__Host-` cookies aren't sent back, and every launch looks like it worked then 401s → Task 2 test
   `Forwarded_proto_from_a_known_proxy_makes_the_request_https` and the deployment guide.
2. **A .NET Framework 4.8 app using Launch** — `RSA.Create()` on 4.8 returns `RSACryptoServiceProvider`, which can't sign
   with SHA-256 unless created with the right provider → Task 1 uses `RSA.SignData(data, HashAlgorithmName.SHA256,
   RSASignaturePadding.Pkcs1)` and the guide shows loading the key as `RSACng`; test signing with an
   `RSACryptoServiceProvider`-shaped key is impossible on net10, so the guide states it and acceptance builds a net48
   console if the targeting pack is installed.
3. **Clock drift and fractional seconds** — `iat`/`exp` must be whole seconds; lifetime must stay ≤ 120 s even when the
   caller passes 150 s → Task 1 tests.
4. **Launch scope just over 6 KB** — refused by Launch before signing with a clear message, not a confusing hub refusal →
   Task 1 test at 6,144 and 6,145 bytes, using the same serialisation as the hub.
5. **OIDC returning without the marker** (a sign-in the host started for something else) → covered by Part 2's
   `.AuthScheme` check; Task 2 test confirms the host's scheme name equals `Hub:SuperAdminSignInScheme`.

---

### Task 1: `QueryBuilder.Launch` package

**Files:**
- Create: `src/QueryBuilder.Launch/QueryBuilder.Launch.csproj` (`netstandard2.0`, `LangVersion` latest, nullable on,
  `PackageId QueryBuilder.Launch`, `Version 1.0.0`, MIT licence expression, README packed),
  `LaunchToken.cs`, `LaunchSigningKey.cs`, `LaunchUser.cs`, `LaunchRole.cs`, `LaunchScope.cs`, `LaunchForm.cs`,
  `Base64Url.cs`, `JsonWriter.cs` (minimal ordered JSON writer: strings, arrays, objects, bool, long), `README.md`
- Modify: `QueryBuilder.slnx`; `tests/QueryBuilder.Tests/QueryBuilder.Tests.csproj` (ProjectReference to Launch)
- Test: `tests/QueryBuilder.Tests/Launch/LaunchTokenTests.cs`

**Interfaces:**
- Produces:
  - `public enum LaunchRole { Admin, Author, Viewer }`
  - `public sealed class LaunchSigningKey : IDisposable { static LaunchSigningKey Rsa(string kid, RSA key); static LaunchSigningKey EcdsaP256(string kid, ECDsa key); string Kid { get; } string Algorithm { get; } }` — validates kid regex, RSA size 2048–8192, EC curve P-256 (key size 256); does not dispose the caller's key.
  - `public sealed class LaunchUser { LaunchUser(string id, string? name = null, string? email = null) }` — id required, ≤ 450; name ≤ 200; email ≤ 320.
  - `public sealed class LaunchScope { static LaunchScope Unrestricted { get; } static LaunchScope For(IDictionary<string, IEnumerable<string>> values) }` — ≤ 20 keys, key regex `^[A-Za-z][A-Za-z0-9_]{0,127}$`, ≤ 1,000 values per key, each value ≤ 200 chars, no empty list (the hub's own rules — check `LaunchTokenValidator` and copy them exactly); serialised `{"k":["v",…]}` (keys in given order) ≤ 6,144 UTF-8 bytes else `ArgumentException("The scope is larger than the hub accepts (6 KB) …")`.
  - `public static class LaunchToken { static string Create(string appKey, string audience, LaunchSigningKey key, LaunchUser user, LaunchRole role, LaunchScope scope, string? returnUrl = null, TimeSpan? lifetime = null, DateTimeOffset? now = null) }` — header `{"alg","typ":"JWT","kid"}`; payload `iss, aud, sub, name?, email?, role, scope|unrestricted:true, returnUrl?, iat, exp, jti` (jti = 32 hex from `RandomNumberGenerator`); `iat`/`exp` whole seconds; lifetime default 60 s, must be 1–120 s; RS256 = `RSA.SignData(…, SHA256, Pkcs1)`; ES256 = `ECDsa.SignData(…, SHA256)` (IEEE P1363 r‖s, 64 bytes — assert length).
  - `public static class LaunchForm { static string Html(string hubBaseUrl, string token) }` — `<form method="post" action="{hub}/querybuilder/launch">` with hidden `token`, a `<noscript>` submit button, and an inline submit script; every value HTML-attribute-encoded; `hubBaseUrl` must be absolute `https://` (or `http://localhost`) without a path other than `/`.

- [ ] **Step 1: Failing tests:** `Rs256_token_is_accepted_by_the_hub_validator` and `Es256_token_is_accepted_by_the_hub_validator`
  (`[MetadataSqlFact]`: register the public key with the existing `MetadataDb`/registry helpers used by
  `LaunchTokenValidatorTests`, create with Launch, validate with the real `LaunchTokenValidator` → claims match);
  `Iat_and_exp_are_whole_seconds`; `Default_lifetime_is_60_seconds`; `Lifetime_over_120_seconds_is_refused`;
  `Scope_of_6144_bytes_is_accepted_and_6145_refused` (and the hub accepts the 6,144 one — same validator);
  `Weak_or_wrong_keys_are_refused` (RSA 1024, P-384); `Bad_kid_is_refused`; `Form_html_encodes_the_token_and_url`;
  `Form_refuses_a_non_https_hub`; `Unrestricted_and_scope_are_exclusive_by_construction`.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS on net8.0 and net10.0 (the test project consumes the
  netstandard2.0 build). **Step 5:** `dotnet pack src/QueryBuilder.Launch -c Release -o <scratch>` succeeds with no
  dependencies in the nuspec. Commit `Add the QueryBuilder.Launch package`.

### Task 2: The hub host

**Files:**
- Create: `src/QueryBuilder.Hub/QueryBuilder.Hub.csproj` (`net10.0`, `IsPackable=false`, ProjectReference Editor,
  PackageReference `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.x), `Program.cs` (one line: `HubHost.Build(args).Run()`),
  `HubHost.cs`, `HubHostOptions.cs`, `SecurityHeaders.cs`, `appsettings.json` (placeholders), `appsettings.Development.json`,
  `Properties/launchSettings.json` (https profile), `README.md` (deployment guide, Task 3)
- Modify: `QueryBuilder.slnx`, `tests/QueryBuilder.Tests/QueryBuilder.Tests.csproj` (ProjectReference to Hub on net10.0 only — conditional)
- Test: `tests/QueryBuilder.Tests/HubHost/HubHostTests.cs` (compiled for net10.0 only)

**Interfaces:**
- Produces:
  - `public static class HubHost { static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null) }` — `configure` lets tests swap the server for `TestServer` and add a fake OIDC handler.
  - Config (section `Hub`): `ConnectionString` (metadata), `Audience`, `Entra { Instance (default https://login.microsoftonline.com/), TenantId, ClientId, ClientSecret, CallbackPath (default /signin-oidc) }`, `SuperAdmins { TenantIds, ObjectIds, Emails, GroupIds }`, `DataSourceKey`, `PreviousDataSourceKeys`, `LaunchRateLimit { PermitLimit 30, WindowSeconds 60 }`, `ForwardedHeaders { KnownProxies, KnownNetworks }`, `DefaultMaxRecords`.
  - Startup refuses (exception naming the setting, never a value) when `ConnectionString`, `Entra:TenantId`, `Entra:ClientId` or `Entra:ClientSecret` is missing (the package already refuses missing Audience/allowlist/key).
  - OIDC scheme name `entra` = `Hub.SuperAdminSignInScheme`; `SignInScheme = "QueryBuilder.HubExternal"`, `ResponseType = code`, `UsePkce = true`, `MapInboundClaims = false`, scopes `openid profile email`, `SaveTokens = false`, `GetClaimsFromUserInfoEndpoint = false`.
  - Pipeline order: `UseForwardedHeaders` → `UseExceptionHandler` → `UseHsts` (non-Development) → `UseHttpsRedirection` → security headers → `UseRateLimiter` → `UseAuthentication` → `UseQueryBuilderEditorUI` → `MapQueryBuilderEditor` → `/healthz`. No `UsePathBase`. No `AddDataProtection` call in the host.
  - Security headers on every response: `Content-Security-Policy: frame-ancestors 'self'`, `X-Frame-Options: SAMEORIGIN`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`.
  - Rate limiter: fixed window per remote IP on `POST /querybuilder/launch` only → 429 + `Retry-After`.
  - `GET /` → 302 `/querybuilder/`.

- [ ] **Step 1: Failing tests** (TestServer via `configure`, a fake OIDC handler registered under `entra` that signs into
  `QueryBuilder.HubExternal` like Part 3's acceptance host, a `MetadataDb` database): `Security_headers_on_every_response`;
  `Launch_is_rate_limited_per_ip` (31st POST in a window → 429, other routes unaffected); `Missing_entra_settings_refuse_startup`
  (3 cases, message names the setting); `Oidc_scheme_signs_into_the_hub_external_scheme` (resolve
  `IOptionsMonitor<OpenIdConnectOptions>` for `entra` → SignInScheme, PKCE, MapInboundClaims=false);
  `Super_admin_sign_in_round_trip_with_fake_entra`; `Forwarded_proto_from_a_known_proxy_makes_the_request_https`
  (and from an unknown address is ignored); `Host_does_not_configure_data_protection_persistence` (the registered
  `IXmlRepository` is the package's); `Healthz_is_ok_and_root_redirects`; `No_path_base` (request to `/querybuilder/api/me`
  under a `X-Forwarded-Prefix` header is not rewritten).
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS (net10.0). **Step 5:** commit `Add the QueryBuilder hub host`.

### Task 3: Guides and 2.0.0 release documentation

**Files:**
- Modify: `src/QueryBuilder.Launch/README.md` (integration guide), `src/QueryBuilder.Hub/README.md` (deployment guide),
  `README.md` (root), `src/QueryBuilder.Editor/README.md`, `src/QueryBuilder.Editor/Scripts/README.md`, `MANAGING-KEYS.md`
  (one section: in hub mode the key is a host setting, rotation from the console)

**Interfaces:** documentation only; every statement must match the code (reviewer checks against source).

- Launch guide: generate keys (PowerShell 5.1 and 7 with `RSA`/`ECDsa` export; openssl), register the public key in the
  console (kid, fingerprint check), the button in ASP.NET Core (minimal API + Razor) and MVC 5 on .NET Framework 4.8
  (load the key as `RSACng`/`ECDsaCng` from a PFX or the Windows key store, never from web.config), the raw token spec for
  non-.NET apps (claims table from spec §5.1, whole-second `iat`/`exp`, 60 s default, ≤ 120 s, 6 KB scope limit, form
  POST only), troubleshooting (generic refusal page → ask the hub's super admin to read the `LaunchRefused` audit reason).
- Hub deployment guide: Entra app registration (redirect URI `https://<hub>/signin-oidc`, "Assignment required" on,
  group/object-id allowlist), settings table, secrets in environment variables, HTTPS + reverse proxy (forwarded headers),
  several instances (same metadata DB and same `DataSourceKey`/`PreviousDataSourceKeys` everywhere; the package stores
  Data Protection keys in the metadata DB — don't configure your own), first run (empty database; hub claims it), creating
  the first app.
- Both READMEs — new "Hub mode (2.0.0)" section and the release items from CLAUDE.md "Release docs (2.0.0)" and Part 1:
  hub takes over the host's Data Protection; after a key rotation keep the old key ≥ 90 days + 8 h; HTTPS required; group
  super admins re-checked against Entra only at sign-in; worst-case cookie ≈ 13–15 KB; `ServicesStartConcurrently` breaks
  startup ordering; email claims can be changed by tenant admins — prefer object ids or groups; fractional `iat`/`exp`
  refused; Apps / AppId / MetadataSettings; the mode refusal (hub needs its own empty DB); the AuditLogEntries backfill
  rewrites the whole table; INSERT grant on `MetadataSettings`; raw-SQL registration still works; plus Part 3 items:
  roles table, Viewer allowlist, publishing, dashboard-context charts, sharing to known people, user-id case rule,
  data-source lock/switch/move, app record ceiling (≤ 30 s to apply), metadata-DB refusal (fails open when the candidate
  is unreachable at check time; the catalog load re-checks), `datascope:{id}` cache up to 5 min on other instances after
  a move, picker limited to the first 500 directory rows, logout is POST.
- `Scripts/README.md`: the 2.0.0 migrations (`AddApps`, `AddHubRegistry`, `AddAppSessionsValidAfter`,
  `AddHubRolesAndPublishing`), the backfill cost, the MetadataSettings grant. The script file itself is regenerated at
  the version bump, not now.

- [ ] **Step 1:** write. **Step 2:** a reviewer cross-checks every setting name, default, route and limit against source.
  **Step 3:** commit `Document hub mode, the hub host and QueryBuilder.Launch`.

### Task 4: Acceptance for Part 4

- [ ] **Step 1:** full server suite net10.0 then net8.0 (`-m:1`, both DB variables if Docker is available), client tests.
- [ ] **Step 2:** run `src/QueryBuilder.Hub` (https, throwaway metadata DB, fake Entra is impossible in the real host —
  use the test-only `configure` hook via a tiny throwaway launcher project that calls `HubHost.Build(args, b => add fake
  entra)`); a throwaway app on net10.0 that references the packed `QueryBuilder.Launch` 1.0.0 from a scratch feed renders
  the form; Playwright follows it into the hub as Admin and Viewer; plus a net48 console signing a token with Launch if
  the .NET Framework 4.8 targeting pack is installed (else note it). Check headers, rate limit (31 POSTs), `/healthz`.
- [ ] **Step 3:** Opus whole-branch review of Part 4; one fix wave; re-review.
- [ ] **Step 4:** update CLAUDE.md "Where we left off" (all four parts done; what the 2.0.0 release still needs: version
  bump, DBA script, PostgreSQL run if skipped, pushes — all on the user's ask); local commit; report.
