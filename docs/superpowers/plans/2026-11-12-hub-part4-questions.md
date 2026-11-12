# Hub Parts 3 and 4 — questions and decisions for the product owner

Written while working without stopping (2026-11-11/12, at the user's request). Each item says what I did, why, and what
it costs to change. Answer any you disagree with; nothing here is pushed or released.

## Needs your answer

1. **New package in the hub host: `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.x** (MIT, Microsoft). Your
   standing rule is to approve packages first; you asked me not to wait, so it is added to `src/QueryBuilder.Hub` only
   (never in the NuGet package). Alternative: Microsoft.Identity.Web (larger). Undo cost: one project file.
2. **`QueryBuilder.Launch` uses no packages** instead of `System.IdentityModel.Tokens.Jwt` (spec §10). It signs with
   `System.Security.Cryptography` directly, which avoids binding-redirect problems in .NET Framework 4.8 apps. Its
   public API doesn't depend on this, so it can be swapped later.
3. **Launch rate limit: 30 per minute per client IP** (configurable). Too low for a NAT-ed office of hundreds of users
   all clicking at 9:00? Raise `Hub:LaunchRateLimit:PermitLimit`.
4. **Entra client authentication by client secret** (certificate later). Your security team may require a certificate.
5. **Where will the hub run** (IIS, Azure App Service, containers)? The deployment guide covers a generic reverse proxy;
   a specific target can get its own section.
6. **PostgreSQL test group not run for Parts 3–4**: Docker Desktop was not running on this machine, and starting it
   uses a lot of memory, so I didn't. Required before the 2.0.0 release.
7. **Drop net8.0?** (open since 2026-11-10; .NET 8 support ended then). Still multi-targeted.
8. **Release 2.0.0**: version bump, DBA script regeneration, packing and pushes are waiting for your ask, as always. A root `dotnet pack` now also packs `QueryBuilder.Launch` (independent version 1.0.0), so the push command must name both packages.
9. **Merge `feature/hub` to `main` (or link a tag) before pushing 2.0.0 to NuGet.** The NuGet-facing `QueryBuilder.Editor` README links to the hub and launch guides at `github.com/nagendra571/QueryBuilderEditor/blob/main/...`, which do not exist until then (likewise the links to `docs/hub-setup-guide.md` in the NuGet-facing `QueryBuilder.Editor` and `QueryBuilder.Launch` READMEs).

10. **Sign-in codes make the data-source key a super admin credential.** With sign-in codes on, the data-source key can
   create super admin codes: keep it as secret as an admin password and use a different key per deployment. OK?

## Decided by me in Part 3 (review when convenient)

- Record limit = min(app ceiling, data-source limit ?? host default) — not "lowest of three", because that would change
  embedded apps (a data-source limit above the host default has overridden it since 1.0.13).
- Viewers may call 7 endpoints (the 5 you approved + the tile-chart and dashboard-parameter-options endpoints, needed for
  dashboards to work). A granted tile shows the chart, its settings and its parameter controls, never SQL, the query
  definition or the source query ids.
- Through a dashboard, values an Admin fixed on a tile (fixed/default mappings) can't be overridden by the viewer.
- A launch whose user id starts with `super:` is refused (reserved for super admins).
- User-id case rule extended: a launch is also refused when the app already has an owner or share for the same id in a
  different case; sharing with a case variant of a known user is refused.
- Shares in hub mode only to known people of the app; changing the level of an existing share still works for people who
  have left the directory.
- Renaming an unlocked data source doesn't need the "App admins can add data sources" switch (connections only).
- Moving a data source into an app that already has one with the same name is refused.
- Switching the viewed app reloads the page.
- A 401 from `/api/me` shows "Your session has ended" in embedded mode too.
- App admins see super admins as "Super admin" (actor, owner names, audit IP hidden).

## Decided by me in Part 4 (review when convenient)

- **`Hub:ApplyMigrations` (hub host, default true):** set false and the host never runs migrations, for DBA-run upgrades from the schema script. Undo cost: one setting.
- **`Hub:ForwardedHeaders:ForwardLimit` (hub host, int, default 1, at least 1):** how many `X-Forwarded-For` hops are read from the right, for chained proxies (list every hop in `KnownProxies`/`KnownNetworks`, set the limit to the hop count). Undo cost: one setting.
- **`Hub:Audience` is scheme + host (+ port) only** — no path or trailing slash — and must equal the `aud` apps send;
  the host refuses to start otherwise (a one-character mismatch would refuse every launch).
- **`QueryBuilder.Launch` mirrors the hub exactly:** user id ≤ 256 characters (the hub's limit, not 450), ids starting
  `super:` refused, scope key and kid patterns identical, scope size measured the way the hub measures it, return-URL
  shape checked up front. `LaunchSigningKey` is not `IDisposable` (the caller owns the RSA/ECDsa key).
- **The launch form always shows a "Continue to QueryBuilder" button** (works when the app's CSP blocks inline
  scripts) and takes an optional CSP nonce.
- **DBA-managed hub databases:** the docs tell the DBA to turn the Default app off themselves
  (`UPDATE Apps SET IsEnabled = 0 WHERE Id = '…00d'`) next to inserting `('Mode','Hub')`; the package does it only when it
  claims the database itself.
- **Verification gaps before 2.0.0:** the net8.0 full suite hasn't run since Part 3's final fixes (the system stopped it
  for low memory; focused net8 tests passed), the PostgreSQL group wasn't run (Docker not running), and real Entra ID
  sign-in hasn't been tried (a stand-in handler was used).
