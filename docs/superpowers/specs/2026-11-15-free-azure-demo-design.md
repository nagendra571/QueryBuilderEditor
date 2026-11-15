# Free public demo: QueryBuilder Hub + "Northwind Programs" (App A simulator) on Azure free tiers

Status: draft for approval (2026-11-15).

## Order of work (user's decision)

**Phase 1, now: the simulator and the deployment.** Scenes 2–5 working on the real Azure URLs, scene 1 possible
(sign-in code, App A registered), the starting set in place, and the demo guide. **Phase 2, later: Reset demo, the
App B cleanup script, and the data-source lock decision.** The Reset demo button is not built in phase 1 (no
placeholder button either); the "Reset demo" and "App B cleanup" sections below describe phase 2 and are kept so phase 1
doesn't make choices that block them.

**Data source lock (decided by the user 2026-11-15: stays locked; Reset demo and the App B cleanup are built):** the demo data source is added by the super admin and
left **locked**, so prospect Admins can query it but can't change its connection, catalog settings or scope rules. Without
Reset in phase 1, an unlocked source could be broken or pointed at an outside server with no way back. Unlocking later is
a phase 2 decision (it would let prospects try the scope-mapping screen, at the cost of a bigger reset).

**As built (2026-11-15, phase 1 done):** region **West US 3** (the trial subscription has no free-plan quota in East US 2
or four other US regions); the data source uses the **raw-SQL route** (`ConnectionStrings__Programs` app setting on the hub,
row inserted with `LockedBySuperAdmin = 1`) instead of the admin form, because that route was verified locally and the form
route was not. Everything else as below. Guide: `demo/README.md`.

## Why

The user wants a public demo of the multi-app hub that prospects can click through on their own for weeks, and that a
video agent can later explore (together with the docs) to make a marketing video. It must cost nothing: the user's
Azure account is free tier only.

## The demo story

1. **Video only:** a super admin signs in to the hub, walks through App A's existing registration (signing key, return
   URL, enabled), then registers a second app, "App B", from scratch. App B is never launched and stays turned off.
2. A Northwind user opens the hub as **Admin, unrestricted** (sees every program).
3. A Northwind user opens the hub as **Admin, ProgramId = 10** (same views, only program 10's rows).
4. Same as 3 with **ProgramId = 20**.
5. A Northwind user opens the hub as **Viewer, ProgramId = 10** (published dashboards only, still program 10).

Prospects start at the simulator (scenes 2–5). They never get super admin.

## Goals

- Two public https URLs on Azure: the hub and the App A simulator, up for weeks, $0.
- Scenes 2–5 work for an anonymous prospect with no instructions beyond the page itself.
- Any visitor can put App A's content back to its starting state ("Reset demo").
- A demo guide the user can hand to a video agent.

## Non-goals

- No change to the `QueryBuilder.Editor` or `QueryBuilder.Launch` packages (no new versions).
- No real authentication in the simulator; the user switcher is the identity.
- No Entra / OIDC provider for the hub.
- No custom domain, no load capacity beyond 10–20 prospects a month (arriving in bunches).
- Deploying the user's real .NET Framework 4.8 App A.

## Anti-goals (would be a failure even if it technically worked)

- Any charge on the Azure account. Everything must sit on free offers, with "pause, don't bill" where Azure offers the choice.
- A prospect being able to become super admin, or reach anything outside App A.
- Demo-only code shipped inside the NuGet packages (a reset endpoint in the hub, demo seeders in the Editor, etc.).
- Reusing the user's real hub keys or databases. The demo gets its own `Hub:DataSourceKey` (with codes on it is a super
  admin credential), its own launch key pair, its own databases.
- Real or personal-looking data. Everything shown is made up.
- Committing secrets: keys, connection strings, passwords stay in Azure app settings only.

## Constraints and facts

- **Hosting (decided):** one Azure resource group (e.g. `qb-demo`, one region) holding everything, so it can be deleted
  in one step.
  - One **App Service plan, Free (F1)**, with two web apps: the hub and the simulator. HTTPS on `*.azurewebsites.net`.
    F1: no Always On (apps sleep when idle, ~10–20 s wake), 60 CPU minutes/day, 1 GB memory. Check .NET 10 is offered
    on the chosen OS/region; the builder picks Windows or Linux (Windows makes `dotnet QueryBuilder.Hub.dll` in the Kudu
    console straightforward and avoids the Linux forwarded-header question; see below).
  - Two **Azure SQL Database free offer** databases (serverless, 100,000 vCore-seconds + 32 GB per database per month;
    the user's portal shows the offer): `hub` metadata and `programs` sample data. Two, because the hub refuses a data
    source that points at its own metadata database. Set the free-limit behaviour to **auto-pause until next month**,
    never "continue with charges". Confirm the offer allows two databases on this subscription before creating them.
  - Budget estimate: min 0.5 vCore ≈ 55 awake hours/month/database; each visit bunch costs ~1 h (auto-pause delay).
    10–20 prospects in bunches fits.
- **Cold start:** app wake + database resume (up to ~1 min). The hub's connection string gets a long `Connect Timeout`
  (~120 s) so the first visitor waits rather than sees an error; the simulator shows a "first visit can take up to a
  minute while the demo wakes up" note. The hub has no background timers touching the database (only startup hosted
  services: migration, mode guard), so the database can pause.
- **Hub settings** (see `src/QueryBuilder.Hub/README.md` settings table and `docs/hub-setup-guide.md`, including its
  App Service notes in C5): `Hub:ConnectionString`, `Hub:Audience` = `https://<hub-app>.azurewebsites.net` exactly (no
  trailing slash), `Hub:DataSourceKey` (new, demo-only), `Hub:ApplyMigrations` = true (the hub's SQL user needs
  `db_ddladmin` + data roles, or use the server admin for this throwaway DB), `Hub:SignIn:Codes:Enabled` = true with
  `Hub:SuperAdmins:CodeNames` = e.g. `["video"]`, no provider. The launch rate limit is per client IP: the builder must
  make sure the IP the hub sees is the visitor's, not App Service's front end (Windows/IIS gives the real client;
  Linux needs `Hub:ForwardedHeaders`), or one busy visitor locks everyone out.
- **Super admin sign-in (decided):** one-time codes only. The user opens the Kudu console and runs
  `dotnet QueryBuilder.Hub.dll signin-code --name video --minutes 60` (1–60, single use; session lasts up to 8 h), and
  gives the code to the video agent. Verify the command works on F1 from Kudu with the app's settings.
- **Hub source:** deployed from this repo's `src/QueryBuilder.Hub` (not on NuGet), current `main`.

## The App A simulator: "Northwind Programs" (decided: layout B)

A made-up business app that looks like a customer's real app.

- Header: app name, and a **user switcher** top right with four people (names are the builder's choice; placeholders
  were Alex / Mary / Raj / Lee):
  - Admin, unrestricted (`DataScope.Unrestricted` equivalent in the launch token)
  - Admin, ProgramId = 10
  - Admin, ProgramId = 20
  - Viewer, ProgramId = 10
- Left menu: Home, Enrolments, Budgets, **Reports ↗**. The screens show the current person's program rows read from the
  same `programs` database, so the numbers match what QueryBuilder later shows.
- **"Open reports in QueryBuilder →"** signs a launch token for the current person with the published
  `QueryBuilder.Launch` NuGet package and posts it to the hub (`LaunchForm.Html`), exactly as a customer integration
  would (see `src/QueryBuilder.Launch/README.md`).
- Footer: the warming-up note, and in phase 2 **Reset demo** (with a confirmation: "puts App A's queries, charts and
  dashboards back to the starting set for everyone").
- Tech (my call): ASP.NET Core on .NET 10, in this repo under `demo/` (not packed, not part of the solution's release
  flow). The launch private key lives only in an Azure app setting; the hub holds only the public key.
- The identity is whatever the switcher says (a cookie is fine). This is acceptable because the demo is all made-up
  data and the hub limits everything to App A.

## Demo data

- `programs` database: from the user's `Sample-DataScope.sql` (copy it into `demo/`; the original at the repo root stays
  untracked). Gives `sales.Programs` (10–50), `ProgramEnrollments` (20 per program), `ProgramBudgets`
  (`OwningProgramId`) and the `vw_Program*` views. Plus an app-users view for the share picker if the builder finds it
  useful (made-up names only).
- The hub reaches it as an admin-managed data source in App A, through a **read-only** SQL user.
- Starting set in App A (builder may refine): a few program queries and charts (e.g. "Enrolments by program", "Budget by
  program"), data-scope rules (Enrolments/Summary → `ProgramId`, Budgets → `OwningProgramId`, `vw_Programs` not
  scoped), and a "Program overview" dashboard **published** so the Viewer sees it.

## Reset demo

- My call: the simulator runs SQL against the hub's metadata database directly (it holds that connection string as an
  app setting). No reset endpoint is added to the hub.
- Restores App A's content to the starting set: queries, visualizations, dashboards, tiles, shares, publish state, and
  the demo data source's policy (catalog scope/allowlist, record limit, data-scope rules, scope keys, turned on). A
  prospect Admin can change all of these, so all of them must come back.
- Must not touch: App A's and App B's registration (keys, return URLs, enabled), other apps, the hub's audit log,
  Data Protection keys, `SeenUsers`.
- The data source's encrypted connection: a prospect Admin could edit it. The blob is AES-GCM bound to the row Id, so
  restoring the original blob onto the same row is valid; the builder decides how to keep that baseline (never in git).
- How the starting set is captured (seed script vs snapshot of rows after the initial setup) is the builder's call; it
  must respect `AppId` and the hub's app-ownership rules, and must be idempotent.
- Visitors mid-session during a reset may see "not found" until they go back to a list. Accepted.
- The hub caches the catalog, catalog policy and `datascope:{id}` (up to ~5 min). With the source locked, reset only
  touches content, which isn't cached, so it shows at once. If the source is unlocked in phase 2, "shows within ~5
  minutes" is acceptable (my call); the simulator never gets Azure credentials to restart the hub.

## App B cleanup (phase 2, my call)

Scene 1 registers App B, and Reset demo leaves it alone, so a retake needs App B gone. A small script under
`demo/` that only the user runs (never on the simulator page) removes App B and its keys/return URLs, so a retake starts
clean.

## Free allowance running out (my call)

Accepted: the demo pauses until the 1st of next month. To make it unlikely: App A gets a modest record ceiling (a few
thousand rows), the launch rate limit stays at its default, and acceptance runs against the live URLs stay short
(do the heavy testing locally first).

## Deployment (decided)

- The user runs `az login`; the builder does the rest from this machine with the Azure CLI (installed, 2.82.0):
  resource group, F1 plan, two web apps, two free databases, firewall rules, app settings, keys, deploy, App A
  registration and starting set.
- Before creating anything, show the user the list of resources to be created and confirm it is all on free offers.
- Build and test the simulator locally against a local hub first; deploy once it works.
- After deployment the builder clicks through scenes 1–5 (and, in phase 2, Reset demo) on the real URLs (Playwright), including plain
  first-visit after idle (cold start).

## Handover

- `demo/README.md`, a guide for the user and the video agent: both URLs, the four people and what each should see, the
  five scenes step by step (scene 1 = walk through App A, register App B), how to make a sign-in code in Kudu, the
  warming-up wait, when to press Reset demo, links to the existing docs (both READMEs, `docs/hub-setup-guide.md`,
  `src/QueryBuilder.Launch/README.md`, `src/QueryBuilder.Hub/README.md`).
- A teardown note: delete the `qb-demo` resource group.
- Git: commit on a new branch (with the pending `CLAUDE.md` notes); never commit secrets; push only when asked.

## Left to the builder

Names (resource group, apps, people), region, Windows vs Linux, exact starting-set contents, how the simulator stores
the current person, the reset mechanics, page styling (plain and believable as a business app; not the QueryBuilder
"Zoning Map" look, since it's meant to be the customer's app).
