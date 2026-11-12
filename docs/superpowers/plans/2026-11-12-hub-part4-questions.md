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
8. **Release 2.0.0**: version bump, DBA script regeneration, packing and pushes are waiting for your ask, as always.

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
