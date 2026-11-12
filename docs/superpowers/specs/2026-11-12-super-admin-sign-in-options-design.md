# Super admin sign-in options — design

Status: draft for review · 2026-11-12 · decided in chat with the product owner. Amends §6 of
`docs/superpowers/specs/2026-11-10-multi-app-hub-design.md`. Builds on hub Parts 1–4 (branch `feature/hub`, local).

## 1. Intent

QueryBuilder Hub will be sold to many clients. Their app users already arrive through each app's signed launch button
(roles Admin / Author / Viewer) — that stays exactly as built. Only **super admins** (the client's staff who run the
hub) need another way in, because many clients have no Microsoft Entra ID. Each deployment chooses how its super
admins sign in, in configuration:

1. **Identity provider** — Entra ID (as today) **or any OpenID Connect provider** (Google Workspace, Okta, Auth0,
   Keycloak, Ping…).
2. **One-time sign-in codes** — the hub generates a code; the super admin pastes it on the sign-in page.

Either or both can be on. Success = a client without Entra can deploy the hub and sign its super admins in within
minutes, with no new database tables and no username/password store.

## 2. Goals, non-goals, anti-goals

Goals
- Deployer picks identity provider (Entra or generic OpenID Connect), sign-in codes, or both.
- Sign-in codes are short-lived, single use, and only for people on the configured super admin list.
- No new tables; no password storage; no email server.

Non-goals
- Signing in super admins through an app's launch token (the product owner's "option 3") — explicitly dropped.
- Passkeys, Windows (Negotiate) sign-in, emailed magic links, client certificates.
- Changing how app users (Admin / Author / Viewer) sign in.

Anti-goals (failure even if it "works")
- Any way a launch token, a browser value or an app can make someone a super admin.
- A code that works twice, works after its expiry, works on another hub deployment, or works for a name no longer on
  the list (once the hub has restarted with that list).
- A standing secret that acts as a permanent password.
- Storing codes or anything password-like in the database or a log.
- A sign-in page that tells an attacker which part of a bad code was wrong.

## 3. Decided in chat (product owner)

- Two options for super admins: identity provider and a hub-generated token pasted to sign in; both may be enabled.
- "Option 3" (super admins via their own apps) is dropped.
- Whoever can run commands on the hub's server may create super admin codes.
- Not forms authentication, no new tables.
- Accepted recommendations: the identity-provider option covers any OpenID Connect provider, Entra being one setup;
  the pasted token is a one-time code, not a standing token.

## 4. Option 1 — identity provider (Entra or any OpenID Connect)

- Host settings (replacing `Hub:Entra`; 2.0.0 is unreleased, so no compatibility shim): `Hub:SignIn:Provider`
  `{ Kind: "Entra" | "OpenIdConnect", Authority, TenantId (Entra only), ClientId, ClientSecret, CallbackPath
  (default /signin-oidc), Scopes (default openid profile email), GroupsClaim (default "groups") }`. For Entra the
  authority is built from Instance + TenantId as today.
- Same scheme wiring as today: code flow + PKCE, `SignInScheme = QueryBuilder.HubExternal`, `MapInboundClaims=false`.
- Allowlist (package `SuperAdminOptions`) by kind:
  - **Entra**: unchanged — `TenantIds` required, then `ObjectIds` (`oid`), `Emails`, `GroupIds`.
  - **OpenIdConnect**: `UserIds` (the `sub` claim; meaningful because the handler validates the issuer against the
    configured authority), `Emails` — **only when the token says `email_verified: true`**, `GroupIds` (from
    `GroupsClaim`). `TenantIds` not used.
- The existing re-check of the session's match against current options on every request applies to both kinds.

## 5. Option 2 — one-time sign-in codes

**Who:** names listed in `SuperAdmins:CodeNames` (short ids, e.g. `"priya"`). A code can only be made for a listed
name. Removing a name takes effect when the hub restarts (settings are read once per process, as for the Entra
allowlist today) — documented; live reload is a possible later change (product owner: "pick the easy one for now").

**Creating a code** — only on the hub's server: `QueryBuilder.Hub signin-code --name priya [--minutes 15]` (runs with
the host's configuration, checks only the settings it needs, never runs migrations, writes the audit entry, prints
the code, exits; the web server is not started; if the audit write fails no code is printed). Audited as
`SuperAdminCodeIssued` (hub-level: name, lifetime, jti — never the code). There is no console button for codes (it
would let two code users keep issuing each other codes without the server).

**Identity:** a code session's user id is `super:code:<name>`, separate from provider identities (`super:<oid or
sub>`); one person signing in both ways is two identities (accepted: super admins rarely own queries).

**The code:** compact `base64url(payload).base64url(HMAC-SHA256)`; payload `{ purpose:"qb-super-signin", aud:
<Hub:Audience>, name, jti, iat, exp }` — `aud` ties a code to one hub deployment (staging and production sharing a key
still can't use each other's codes). HMAC key derived (HKDF-SHA256, fixed label) from the hub's existing `Hub:DataSourceKey`, so no new
secret and every instance verifies the same codes. Lifetime default 15 minutes, maximum 60. Rotating the
data-source key invalidates outstanding codes (acceptable; they are short-lived).

**Using it:** `GET /querybuilder/signin` shows a small page with whichever options are on (a "Sign in with <provider>"
button and/or a paste box). The paste box posts `code` to `POST /querybuilder/signin/code` (form post; same-origin
check on `Origin`; rate-limited per IP like `/launch`). The server checks signature, purpose, expiry (30 s skew),
the name is still in `CodeNames`, and single use by inserting `jti` into the existing `LaunchReplay` table with
`AppId = App.NoneId` (hub-level). Success → the normal super admin session (8 h absolute / 1 h idle), match kind
`Code`, value = name; audit `SuperAdminSignedIn` with method `code`. Any failure → one generic refusal page (no
reason shown) and audit `SuperAdminSignInRefused` with the reason.

## 6. Startup rules

The hub refuses to start (message names the setting, never a value) when: neither option is configured; the
provider is configured with no allowlist entry for its kind (Entra additionally without `TenantIds`); codes are
enabled with no `CodeNames`; `--minutes` or the configured lifetime is outside 1–60.

## 7. Constraints

- Everything security-deciding (code format and verification, recognition, single use) lives in the package
  (`QueryBuilder.Editor`); the host only wires settings, the OIDC scheme, the CLI verb and rate limits.
- No new tables, no migration. Existing audit action numbering continues (`SuperAdminCodeIssued` = next free value).
- Conventions in CLAUDE.md (hand-rolled mediator, `QueryBuilderJson.Options`, why-only comments, both READMEs and
  the hub deployment guide updated, tests on net8.0 and net10.0).
- No new packages.

## 8. My calls (review these)

- Code lifetime default 15 minutes, maximum 60.
- No console code creation (server command only).
- Code identities in their own namespace `super:code:<name>`; codes carry `aud`.
- Removing a name needs a restart (product owner chose the easy option).
- `email_verified` (boolean or the string "true") required for email matches on generic providers.
- Generic OpenID Connect reads the userinfo endpoint (Okta/Keycloak put email and groups there); a multi-tenant Entra
  authority (`/common`, `/organizations`) is refused.
- Codes are enabled by an explicit `Hub:SignIn:Codes:Enabled` switch with `DefaultMinutes`; a codes-only hub doesn't
  need `SuperAdminSignInScheme`; the session's tenant id is optional for non-Entra kinds.
- The code post has its own per-IP rate-limit bucket; a post without an `Origin` header is refused; the provider
  button starts the challenge with a POST.
- HMAC key derived from `Hub:DataSourceKey` rather than a separate setting.
- Single-use store reuses `LaunchReplay` with `AppId = None`.
- `Hub:Entra` settings are replaced by `Hub:SignIn:Provider` (no shim; unreleased).

## 9. Left to the builder

Exact class names; the sign-in page's markup (plain, in the Zoning Map style, no SPA needed); how the CLI verb is
detected in `Program.cs`; test layout.
