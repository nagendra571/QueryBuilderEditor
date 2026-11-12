# QueryBuilder

[![NuGet](https://img.shields.io/nuget/v/QueryBuilder.Editor.svg)](https://www.nuget.org/packages/QueryBuilder.Editor)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A visual, no-SQL-required query builder for business users. Developers put the hard logic in SQL
views; business users browse a catalog of those views, drag columns onto a canvas, set filters and
sorts, preview the generated SQL, run it, and export the results — no SQL knowledge required. Full
audit trail of who created, edited, ran, or exported every query.

Ships two ways:

| | |
|---|---|
| **As a package** | [`QueryBuilder.Editor`](https://www.nuget.org/packages/QueryBuilder.Editor) on NuGet — drop it into your own ASP.NET Core app. See that package's [README](src/QueryBuilder.Editor/README.md) for the full integration guide. |
| **As this repo** | Clone it and run the sample app directly (below) to try the whole thing without writing any integration code first. |

## Screenshots

*(Run the sample app locally — see below — to see the catalog sidebar, visual query builder, results grid, and audit history in action.)*

## Repository layout

Clean Architecture, four internal layers plus the package root, a sample host, the hub host and the launch package:

```
src/
  QueryBuilder.Domain           entities, enums, the QueryDefinition model — no dependencies
  QueryBuilder.Application      commands/queries, validation, service abstractions
  QueryBuilder.Infrastructure   EF Core, SQL generation, catalog introspection, audit log, export
  QueryBuilder.Editor           ← the published package: DI/endpoint wiring, access control,
                                  setup diagnostics, and the built React app embedded as a resource
  QueryBuilder.Api              sample host app — consumes QueryBuilder.Editor like any real
                                  integration would, plus demo data seeding
  QueryBuilder.Hub              deployable host for hub mode (net10.0, not a package) — see
                                  "Hub mode (2.0.0)" and its README
  QueryBuilder.Launch           small netstandard2.0 package apps use to launch users into a
                                  hub (signed token + form) — see its README
client/                         the React/TypeScript/Tailwind frontend (source for what's
                                  embedded into QueryBuilder.Editor at pack time)
tests/
  QueryBuilder.Tests            xUnit tests (SQL generation, row-level data scoping, validation) —
                                  `dotnet test tests/QueryBuilder.Tests`
```

`Domain`/`Application`/`Infrastructure` are never published on their own — `QueryBuilder.Editor`
bundles their compiled assemblies directly into its package so a consumer only ever installs one
package. See [`src/QueryBuilder.Editor/QueryBuilder.Editor.csproj`](src/QueryBuilder.Editor/QueryBuilder.Editor.csproj)
for how.

## Running the sample app locally

**Prerequisites:** .NET 10 SDK, plus the .NET 8 runtime to run the tests on both frameworks, Node.js 18+,
SQL Server (LocalDB is fine for development; PostgreSQL is optional, only for the PostgreSQL integration tests). The package targets .NET 8 (EF Core 9) and .NET 10 (EF Core 10);
the sample app runs on .NET 10.

```bash
# Backend — migrations and demo data seed automatically on first run
dotnet run --project src/QueryBuilder.Api
```

This serves the whole app — API and built frontend — from a single process. Navigate to
`/querybuilder` on whatever URL `dotnet run` prints (check
`src/QueryBuilder.Api/Properties/launchSettings.json`) — QueryBuilder always mounts under that
path rather than your app's root. A "Sales Sample" data source with a demo schema is seeded
automatically so there's something to query immediately.

**For frontend development** with hot reload instead of the pre-built bundle:

```bash
cd client
npm install
npm run dev
```

This runs the frontend on `:5173` proxying API calls to the backend (CORS is already configured
for this in the sample app's `Program.cs`). Rebuild the embedded bundle for the packaged app with
`npm run build` in `client/`, or pass `-p:BuildClientApp=true` to `dotnet build`/`pack` on
`QueryBuilder.Editor` to do it automatically.

**Verify your setup** anytime at `GET /_setup` (Development only) — checks database connectivity,
migrations, API/UI wiring, JSON options, and the authorization pipeline in one page.

## Features

- Hub mode (2.0.0, opt-in): one QueryBuilder serving several apps, each handing users over with a signed launch
  token, with Admin/Author/Viewer roles, published dashboards and super admins who sign in with an identity provider or a one-time code. See "Hub mode (2.0.0)" below
- Business data sources on SQL Server or PostgreSQL, mixed freely on one host (QueryBuilder's own
  tables stay on SQL Server). See "PostgreSQL data sources" below
- Searchable catalog sidebar (schemas → tables/views → columns) with type-aware icons, sourced
  live from the database's own catalog (`sys.tables`/`sys.views`, or `pg_class` on PostgreSQL) — no
  manual schema configuration
- Drag-and-drop column selection, reordering, and aliasing
- Column totals (Sum, Average, Count, Distinct count, Min, Max) with automatic grouping and a
  totals-only filter (HAVING) — no separate "Group By" step
- Visual filter builder with type-aware inputs (date picker, checkbox, number, text) and
  typed run-time parameters (text, number, date, date range with relative dates, fixed or
  query-based dropdowns, multi-select) in a parameter bar with Apply, values bound as SQL parameters
- Read-only generated SQL preview with syntax highlighting
- Run queries and browse results with client-side pagination, search, column show/hide, reorder,
  and sort
- Export results to CSV or Excel
- Save, reopen, and re-run queries; per-query audit history
- Share a saved query as Viewer or Editor, scoped per data source via an optional `AppUsers` view
- Disable a saved query to block it from being run or exported by anyone (owner or shared users)
  without deleting it; owner-only toggle, reversible, logged to the audit history
- Admin UI (role-gated via `AdminAuthorization`, separate from the main `Authorization` gate): per
  data source, control whether the catalog shows views only / tables only / both, and allowlist
  specific tables/views — replaces the previous raw-SQL-only path for this one concern
- Optional data source management in the admin UI (`options.DataSourceManagement`): add, edit, test and
  turn off SQL Server and PostgreSQL data sources; the connection is stored encrypted with a host-provided key
- Row-level data scoping: the host declares scope keys (`ProgramId`, `ModuleId`, ...; admins can add
  more per data source in the admin UI, and `Keys` may be empty when a resolver is set) and a
  resolver returning each user's values or `DataScope.Unrestricted` (values for every key always come
  from the resolver; removing a key hides the views mapped to it from scoped users until it is
  added back); admins map each key to a view
  column (or mark a view "not scoped"). Enforced server-side in the generated SQL's `WHERE` on every
  path (catalog, run, export, preview, save); undecided views and failing resolvers fail closed.
  See the package README's "Row-level Data Scoping" section
- Record limits: `options.DefaultMaxRecords` plus a per-data-source "Maximum records per query" in
  the admin UI. Past the limit the grid shows the first N rows with a warning to add filters, and
  exports are refused instead of being silently cut off. Unset = unchanged behavior. See the package
  README's "Record Limits" section
- Saved visualizations: add Bar, Horizontal bar, Combo, Waterfall, Line, Area, Trend, Pie, Funnel,
  Scatter/bubble, Table, Pivot or Counter charts to a saved query, with number formats. Charts are drawn
  server-side from the saved query with the *viewer's own* row-level scope, so nobody sees rows
  they couldn't get by running the query; charts over too many rows are refused rather than
  drawn partially (Table shows the first rows with a banner). See the package README's
  "Visualizations" section
- Dashboards: put charts from several saved queries (plus plain-text notes and section headings) on one page, arrange
  them freely on a 12-column grid (drag, corner-resize, automatic gap closing, fit-to-content height,
  full keyboard control), and share the page with named colleagues. Sharing a
  dashboard never shares data: each viewer sees a chart only if they already have access to its
  query, drawn with their own row-level scope; other charts show as "restricted" without revealing
  their names. See the package README's "Dashboards" section
- Dashboard parameters: a bar of shared filters above the tiles (with Apply and `?p_<name>=` links)
  that charts from different queries can all follow, plus per-chart value sources in edit mode
  (Parameters... on a tile): a dashboard parameter, a small control in the tile header, a hidden
  fixed value, or the query's own default. Every value is validated and bound server-side on each
  render, so a viewer's row-level scope still applies
- "Add to dashboard" on every chart of a saved query, and a "Zoning Map" visual design: a navy
  sidebar naming the signed-in user's data zone, a "Your zone" key on each chart tile, hatched
  no-access tiles, and the query builder shown as numbered steps (Source → Columns → Filters →
  Totals filter → Sort). Light and dark themes; fonts are bundled (Public Sans, Barlow Condensed,
  both SIL OFL), so nothing loads from a font CDN.
- Configurable actor identity (`ActorResolver`) and access control (anonymous / authenticated /
  role / custom policy), applied uniformly across every route
- DBA-friendly database story: auto-migration by default, or an idempotent SQL script for
  DDL-restricted environments
- Auto-registers a default data source (against its own connection string) on first run so the
  catalog is never empty out of the box
- Compact/comfortable density modes, light/dark theme

## PostgreSQL data sources

A data source can be a PostgreSQL database instead of SQL Server, with the same catalog, builder,
parameters, run, export, charts, dashboards, sharing, record limits and row-level scope. One host can
use both kinds at once.

**QueryBuilder's own tables stay on SQL Server.** `options.ConnectionString` must point at SQL Server,
so a company that only runs PostgreSQL still needs SQL Server for QueryBuilder itself.

1. Add an Npgsql connection string (including its SSL options) under `ConnectionStrings`:
   `"SalesPg": "Host=db.example.com;Port=5432;Database=sales;Username=qb_reader;Password=...;SSL Mode=Require"`.
2. Insert a row into the metadata database's `DataSources` table with `Provider = 1`:

```sql
INSERT INTO DataSources (Id, Name, Provider, ConnectionStringName, AllowedSchemas, AllowedObjects, CatalogScope, IsActive, CreatedBy, CreatedAtUtc)
VALUES (NEWID(), 'Sales PG', 1, 'SalesPg', '', '', 2, 1, 'admin', SYSUTCDATETIME());
```

Empty `AllowedSchemas`/`AllowedObjects` mean no restriction. `CatalogScope` is an integer: `0` views
only, `1` tables only, `2` both. Partition children, `information_schema`, `pg_*` schemas and anything
the login can't read are hidden.

Use a read-only login: queries run in a read-only transaction with a 60-second timeout, but that
guards against mistakes, not a hostile database. Sharing on a PostgreSQL source uses an opt-in view
`public.app_users` with columns `id`, `display_name` and `email`; without it sharing is reported as
not available.

Limits to know about:

- `money` displays as the database formats it (`$12.50`) and can't be totaled or charted. Workaround:
  a view casting it to `numeric`.
- `numeric` beyond what `decimal` holds displays exactly, but charts plot it at the nearest double.
- Objects whose `schema.name` clash, ignoring case (`public.Orders` and `public.orders`), are shown
  disabled and can't be used. Scoped users don't see them. Use a uniquely named view.
- Columns with a nondeterministic collation can't be scope columns.
- `NaN` and `Infinity` display as blank.
- Equals on `citext` is case-sensitive.
- Enum columns filtered through `::text` don't use their index.
- The login should be read-only anyway.
- PostgreSQL 12 is the minimum: the catalog reads `collisdeterministic`.
- A table the login can read only through column-level grants doesn't appear; grant `SELECT` on the whole table or add a view.
- An object name containing a comma can't be allowlisted.
- Sessions are pinned to UTC, so a `timestamptz` displays and filters as UTC, and `current_date` or `::date` inside a view follows UTC rather than the database's time zone.
- A value over Excel's 32,767-character cell limit refuses the Excel export with the row and column named; CSV export still works.
- PgBouncer is untested.

The package README has the full guide.

## Adding data sources in the admin UI

Off by default. Turn it on and admins can add a SQL Server or PostgreSQL data source from `/admin` by typing its
host, port, database, username and password, test the connection, edit it later, and turn it off. Nothing is ever
hard-deleted. QueryBuilder stores the details as one encrypted value (AES-256-GCM, bound to that data source's row)
using a key you provide.

```csharp
options.DataSourceManagement.EncryptionKey = builder.Configuration["QueryBuilder:EncryptionKey"];
options.DataSourceManagement.Enabled = true;
options.AdminAuthorization.Mode = QueryBuilderAuthorizationMode.Role;   // required, see below
options.AdminAuthorization.RoleNames = ["QueryBuilderAdmin"];
```

Generate a key (base64 of exactly 32 random bytes) with either of:

```bash
openssl rand -base64 32
```
```powershell
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

The PowerShell line works on Windows PowerShell 5.1 as well as PowerShell 7. Once the feature is running, the admin
page's **Generate a new key** button makes one in the browser too (see "Changing the key").

- **Without `app.UseExceptionHandler()` in Development, refusals show up as 500s.** The package turns a refusal (a
  validation message, a turned-off source) into a 4xx response through that handler, so a host that never calls it
  sees them as 500s.
- **Startup checks.** A key that is set must be valid, or the app refuses to start. `Enabled` needs a key. `Enabled`
  with an `AdminAuthorization` of `Anonymous` is refused too, unless you set `AllowAnonymousAdmins = true` (local
  development only).
- **The key alone is enough to query.** Set `EncryptionKey` without `Enabled` and managed data sources that already
  exist keep working; `Enabled` only governs adding, editing, testing and turning off.
- **Keep the key out of source control** (user secrets, environment variable, key vault). Lose it, or change it, and
  each managed data source's details can no longer be read: its admin page says so and the details are typed in
  again. Other data sources are unaffected.
- **Use HTTPS.** The password travels from the browser to your app once, when it is typed. The page warns when it
  isn't on HTTPS. The password is never sent back to the browser; the edit form shows only whether one is saved.
- **An admin can make the server connect to any host they type.** Restrict `AdminAuthorization` to the few people who
  should be able to, and give each data source a read-only database login.
- **Re-enter the password when changing the host, port, username or lowering encryption.** Leaving the password blank
  keeps the saved one only while those are unchanged and encryption isn't weakened (Off < Required < Required and
  verify certificate), so a saved password can't be sent to a different server or in the clear. If a managed data
  source's details can't be read with the current key, a blank password is refused rather than replacing them.
- **Hosts are limited to names, IP addresses (IPv6 in brackets) and, for SQL Server, `host\instance`.** Prefixes such
  as `np:` and `tcp:`, paths, and `(localdb)` are refused: a named pipe could make this server start an outbound
  login with its own identity. So **LocalDB can't be added through the form**; use the raw SQL route for local
  development.
- **SQL Server is always reached over TCP** (`tcp:host,port`), never shared memory or named pipes, so a named instance
  needs its port (`host\\instance` plus the port); SQL Browser lookups are not used.
- **Rows already turned off** (`IsActive = 0`) are now refused for queries, charts, the catalog and the user list
  even when this feature is off. Turning them back on needs `Enabled`, or a SQL update.
- Encryption choices are `Off`, `Required` (encrypted, certificate not checked) and
  `Required and verify certificate`.
- **A data source that is turned off** disappears from the data source list, and its saved queries, charts and
  dropdowns report that it is turned off. Admin pages still open it. Rules, record limit and allowlist are kept.
- A new data source starts as views only, and scoped users see nothing until data scope rules exist for it, so the
  admin UI opens its policy page right after you create it.
- The raw SQL route (`ConnectionStringName` resolved from configuration) still works. Those data sources can be
  renamed and described in the UI but their connection stays in host configuration.
- Adds one nullable column (`DataSources.EncryptedConnection`) through a migration, and the application reads it.
  **DBA-managed hosts must run the new schema script before deploying this release, even with the feature off.**

### Changing the key

For a plain-language walkthrough of the key (setup, where to keep it, rotation, losing it, troubleshooting), see
[`MANAGING-KEYS.md`](MANAGING-KEYS.md), which is also included in the NuGet package.

Rotating the key never means retyping every data source. The old key is kept for reading, and the admin page saves
each source again under the new one.

1. In `/admin`, choose **Generate a new key** (or make one with the commands above). It is made in the browser, never
   sent to the server, and gone when you close the dialog. Store a copy in a password manager.
2. Set it as `EncryptionKey` and move the key that was there into `PreviousEncryptionKeys`, then restart:

   ```csharp
   options.DataSourceManagement.EncryptionKey = builder.Configuration["QueryBuilder:EncryptionKey"];
   options.DataSourceManagement.PreviousEncryptionKeys = [builder.Configuration["QueryBuilder:PreviousEncryptionKey"]!];
   ```

3. Open `/admin` again. The **Encryption key** section shows a short fingerprint of the current and previous keys and
   how many managed sources are on each. Choose **Re-encrypt with the current key**.
4. When every source is on the current key, remove the old key from `PreviousEncryptionKeys` and restart.

- **Every app that shares the QueryBuilder database must have the new key (as `EncryptionKey`, or as a previous
  key) before you re-encrypt.** Re-encrypted values can't be read with the old key alone, so an app still running
  only the old key stops being able to connect to those sources until it is updated.
- **`PreviousEncryptionKeys` are for reading only.** Anything saved is written with `EncryptionKey`. Each entry must be
  base64 of 32 bytes, must differ from `EncryptionKey` and from the other entries, and needs `EncryptionKey` to be
  set, or the app refuses to start. The error names the entry's position, never its value.
- **Only fingerprints leave the server.** A fingerprint is the first 8 hex characters of a SHA-256 over a fixed label
  and the key; the page, the API and the audit log show these and counts, never a key, a saved value or a host.
- **Re-encrypting works with `Enabled` off.** A host can keep the add and edit form turned off and still rotate its
  key: re-encrypting connects to no database, and running it again changes nothing once everything is current. It is
  still limited to whoever passes `AdminAuthorization`, and it is recorded once in the audit log with the counts.
  Like the form, it is refused while `AdminAuthorization` is Anonymous unless `AllowAnonymousAdmins` is set, because
  running it before every app sharing the database has the new key leaves those apps unable to read the sources.
  Such a host can still be rotated from another app that shares its database.
- **A source edited during re-encryption is skipped, not overwritten.** Each source is replaced only if its saved
  value is still the one that was read, so an admin saving a source at that moment keeps what they typed. The result
  reports it as "skipped because it changed"; run it again.
- **Sources no key can open** (listed as unreadable) are left as they are. Open each one and re-enter its password.
  Turned-off sources are included in all of this, because they still hold a saved password.

## Hub mode (2.0.0)

Hub mode turns one QueryBuilder into a **hub** that serves several apps. Each app (a tenant) hands its signed-in users
over with a short-lived, signed **launch token**; your own staff, the **super admins**, sign in with Microsoft Entra ID,
any other OpenID Connect provider, or a one-time code an operator creates on the server (either way, or both).
It is opt-in: with `options.Hub.Enabled` left `false`, QueryBuilder behaves as in every earlier release, and an existing
embedded installation needs nothing but the database upgrade described below.

Three pieces make it work:

| Piece | What it is | Guide |
|---|---|---|
| `QueryBuilder.Editor` | the package; hub mode is switched on with `options.Hub` | this section |
| `QueryBuilder.Hub` | a ready-made `net10.0` host you deploy (not a NuGet package): identity provider and sign-in code sign-in for super admins, HTTPS and HSTS, forwarded headers, a launch rate limit | [deployment guide](src/QueryBuilder.Hub/README.md) |
| `QueryBuilder.Launch` | a small `netstandard2.0` package your apps use to sign the token and render the launch form; works from .NET Framework 4.7.2+ | [integration guide](src/QueryBuilder.Launch/README.md) |

### Turning it on in your own host

Most teams should deploy `QueryBuilder.Hub` as it is. To build a host yourself:

```csharp
builder.Services.AddQueryBuilderEditor(options =>
{
    options.ConnectionString = hubConnectionString;               // the hub's OWN, empty database
    options.Hub.Enabled = true;
    options.Hub.Audience = "https://hub.example.com";             // every launch token's `aud` must equal this
    options.Hub.SuperAdminSignInScheme = "entra";                 // your OpenID Connect scheme, see below
    options.Hub.SuperAdmins.TenantIds = ["<your Entra tenant id>"];
    options.Hub.SuperAdmins.ObjectIds = ["<a super admin's object id>"];   // and/or Emails, GroupIds
    options.DataSourceManagement.EncryptionKey = hubKey;          // required: base64 of 32 bytes
});

builder.Services.AddAuthentication().AddOpenIdConnect("entra", o =>
{
    // ... authority, client id and secret ...
    o.SignInScheme = "QueryBuilder.HubExternal";                  // a short-lived cookie scheme the package registers
});
```

Hub mode refuses to start without `Audience` and `DataSourceManagement.EncryptionKey`, and without a way for a super admin
to sign in (below). `options.Hub.CookieName` (default `__Host-qb-hub`),
`AbsoluteLifetime` (8 hours) and `IdleTimeout` (1 hour) can be changed. A super admin signs in at `/querybuilder/signin`.
A host of your own must use HTTPS, `UseForwardedHeaders` behind a proxy, `UseExceptionHandler`, no `UsePathBase`, and send
`frame-ancestors 'self'`; `QueryBuilder.Hub` does all of that.

### How super admins sign in

Either way works, or both; with neither the host refuses to start (the message names `HubOptions.SuperAdminSignInScheme`).

- **An identity provider.** Set `options.Hub.SuperAdminSignInScheme` to your OpenID Connect scheme (its `SignInScheme` must be
  `"QueryBuilder.HubExternal"`) and `options.Hub.SuperAdmins.ProviderKind`:
  - `Entra` (the default): `TenantIds` (required, one tenant: never a multi-tenant authority) plus `ObjectIds`, `Emails` or
    `GroupIds`; the `sub` claim never matches.
  - `OpenIdConnect`: `UserIds` (the provider's `sub`), `Emails` (counted only when the provider reports `email_verified` as
    `true`) or `GroupIds` (from the claim named by `GroupsClaim`, default `groups`); `TenantIds` and `ObjectIds` are ignored
    with a startup warning. Set `MapInboundClaims = false` on the handler so claim names arrive as the provider sent them, and
    map `email_verified` and the groups claim if you read userinfo.
  - `options.Hub.ProviderDisplayName` is the text of the button ("Sign in with ...").
- **One-time sign-in codes.** Set `options.Hub.SignInCodes.Enabled = true` (and optionally `DefaultMinutes`, 15 by default, 1 to 60)
  and name who may be given one in `options.Hub.SuperAdmins.CodeNames` (compared exactly, case-sensitive; required when codes are
  on). A code is created only on the server, by your own code calling `ISuperAdminCodeIssuer.IssueAsync(name, minutes, ct)`
  (`QueryBuilder.Hub` wraps it as the command `signin-code --name <name> [--minutes <1-60>]`). It is single use, tied to
  `Hub.Audience` and a key derived from the encryption key and the audience, and signs the person in as the session user `super:code:<name>`.
  The person pastes it at `/querybuilder/signin`, which posts to `POST /querybuilder/signin/code` (form field `code`, the
  request's `Origin` must equal the hub's own origin). Rate-limit that route in your host: `QueryBuilder.Hub` allows 10 attempts per
  minute per address. With codes on, the encryption key is a super admin credential: anyone who holds it can create a code, so keep it
  as secret as an admin password and use a different key for every deployment. Removing a name from `CodeNames` and restarting ends that name's sessions.

### Who can do what

A person's role comes from the **latest launch token only**; it is never stored as a permanent grant.

| Role | Comes from | Can |
|---|---|---|
| Super admin | Identity provider sign-in plus the allowlist, or a one-time sign-in code; never from a launch token | Everything in every app: the app registry (apps, signing keys, return URLs, scope keys, record ceiling), locking, unlocking and moving data sources, the encryption key, the hub audit log. Unrestricted data scope. |
| Admin | token `role` = `Admin` | Everything inside their app: sees and manages every query, dashboard and share, runs the Admin page for the app's data sources (except locked ones), publishes dashboards. |
| Author | `Author` | The normal user, inside their app: build, save, chart, run, export, share, build dashboards. |
| Viewer | `Viewer` | Dashboards only: the ones published to the app and the ones shared with them. Parameter controls and refresh work; there is no query list, builder, SQL, chart settings, sharing or export. |

- **Isolation.** A user is the pair (app, user id): the same email from two apps is two people. A request that names another
  app's query, dashboard, share or data source is answered 404 and changes nothing.
- **Viewer allowlist.** A Viewer can call exactly seven endpoints: the dashboard list, one dashboard, a dashboard tile's chart,
  a dashboard's parameter options, a query's parameter options (through a dashboard), the chart render for a query, and
  `/api/me`. The Viewer role wins over any share: sharing a query with a Viewer gives them nothing beyond dashboards.
- **Publishing.** Admins (and super admins) can **Publish** a dashboard to the app and **Unpublish** it; every Viewer and
  Author of the app then sees it read-only.
- **Dashboard-context charts.** A chart tile on a dashboard the viewer can see renders with the **viewer's own data scope**.
  They see the chart, its settings and its parameter controls, never the SQL, the query definition or the source query's id.
  Values an Admin fixed on a tile (a fixed mapping) cannot be overridden by the viewer.
- **Sharing to known people.** In hub mode a query or dashboard can be shared only with people the app already knows: those who
  have launched (the 2,000 most recent are offered) plus the data source's `AppUsers` view (the picker reads its first 500
  rows). Changing the level of an existing share still works for someone who has since left the directory.
- **User-id case rule.** Ids are compared exactly. A launch is refused when the app already knows the same id in a different
  case (as a user, an owner or a share target), and sharing with a case variant of a known user is refused, so two spellings
  can never merge. A user id starting with `super:` is refused (reserved for super admins).
- **Data sources.** A data source added by a super admin is **locked**: the app's admins and users can use it, but only a
  super admin changes it, until a super admin unlocks it (and a super admin can lock it again). The per-app switch **App admins
  can add data sources** (off by default) lets app admins add, edit, test and turn off the unlocked sources of their own app.
  A super admin can **move** a data source to another app (refused while a saved query, a dashboard tile, a data scope rule or a parameter dropdown
  uses it, when the target app already has one with that name, and when the target app is turned off). A data source that points at the hub's own metadata database is refused,
  compared by server identity; if the target is unreachable at that moment the check lets it pass, and the catalog load
  checks again. After a move, other instances can keep the old data scope rules for up to 5 minutes.
- **Record limits.** A super admin can give each app a **record ceiling**. The limit that applies is the lower of the ceiling
  and the data source's own limit (or the host default when the data source has none); a change takes up to 30 seconds to apply.
- **Scope.** The app's declared **scope keys** replace the host's `DataScope.Keys` (a data source can still add its own). The
  token's scope replaces `DataScope.Resolver`. `unrestricted` means no row restriction; super admins are unrestricted.
- **The banner and logout.** A launched user sees a banner with the app's name and a "Back to" link to the return URL from the
  token. Logging out is a `POST` to `/querybuilder/logout` with the header `X-QueryBuilder-Request: 1`; it answers
  `{ "redirectUrl": ... }` (the registered return URL, else `/querybuilder/`).

### Sessions and the launch endpoint

`POST /querybuilder/launch` takes a form with one field, `token` (never a query string or header). A refused launch shows one
generic page and records the reason (`LaunchRefused`) in the hub audit log; the reason is never shown to the user and the
token is never logged. On success the hub sets the cookie `__Host-qb-hub` (HttpOnly, Secure, SameSite=Lax) and redirects to
`/querybuilder/`. A session lasts 8 hours at most and ends after 1 hour unused; a new launch replaces it. Turning an app off,
or revoking one of its signing keys, ends that app's sessions within about 30 seconds. The cookie carries the user, role and
scope, so a token's scope may not exceed 6,144 UTF-8 bytes (about 150 to 300 values); the worst-case cookie is roughly 13 to
15 KB, so check your proxy's request header size limit. A token's `iat` and `exp` must be whole seconds: a fractional value is
refused.

### Things to know before you deploy it

- **HTTPS is required.** Browsers refuse the `__Host-` cookie over plain http.
- **Hub mode takes over Data Protection.** Session keys are stored in the metadata database and encrypted with
  `DataSourceManagement.EncryptionKey`. Do not configure Data Protection persistence for the host, and give every instance the
  same metadata database and the same `EncryptionKey` and `PreviousEncryptionKeys`.
- **After rotating the key, keep the old one for at least 90 days plus 8 hours** in `PreviousEncryptionKeys` (the key ring's
  lifetime plus the longest session), not only until Re-encrypt finishes.
- **Group-based super admins are re-checked against the identity provider only at sign-in.** The allowlist itself is checked on every request,
  but removing someone from a provider group ends their session only when it expires (8 hours at most) or when you remove the
  group id and restart. Prefer `ObjectIds`: an email can be changed by a tenant administrator.
- **Do not start hosted services concurrently** (`HostOptions.ServicesStartConcurrently`): the steps that migrate and claim the
  database depend on starting in order.
- **The hub needs its own empty metadata database.** See the next section.

### Upgrading the database to 2.0.0

2.0.0 adds four migrations: `AddApps`, `AddHubRegistry`, `AddAppSessionsValidAfter` and `AddHubRolesAndPublishing`. **DBA-managed
hosts must run the new schema script before deploying, even if they never use hub mode** (see
[`Scripts/README.md`](src/QueryBuilder.Editor/Scripts/README.md)).

- **Apps and `AppId`.** A new `Apps` table and an `AppId` column on the app-owned tables (saved queries, visualizations, shares,
  data sources, data scope rules, dashboards, tiles, dashboard shares and the audit log). Every existing row goes to the built-in
  **Default** app, the one embedded mode uses. The columns keep a database default of the Default app on purpose, so raw-SQL
  data source registration (`INSERT INTO DataSources ...`), rolling back to 1.5.0 and a mix of old and new versions keep working.
- **`AuditLogEntries` backfill.** Setting `AppId` on existing audit rows rewrites the whole table in one statement, so a large
  audit log means a long run and a lot of transaction log. Plan the window.
- **`MetadataSettings` and the mode marker.** A small table records which mode owns the database: `Embedded` or `Hub`. The first
  start writes it. A database already marked for the other mode is refused ("use a separate metadata database for the hub"),
  and a hub refuses a database that already holds queries, dashboards or data sources but has no marker. Embedded mode only
  logs a warning when the table is missing or cannot be written. **The application login needs `INSERT` on `MetadataSettings`**,
  or your DBA inserts `('Mode', 'Embedded')` (or `'Hub'`) once.
- **Hub registry tables and columns.** `AppSigningKeys`, `AppReturnUrls`, `SeenUsers`, `LaunchReplays` and `DataProtectionKeys`,
  plus `PublishedToApp` on dashboards, `LockedBySuperAdmin` on data sources, and on apps the record ceiling, the "app admins can
  add data sources" switch, the scope keys and a session-validity stamp.

## Roadmap

Not yet built, tracked for a future version:

- Admin UI editing of `AllowedSchemas` and other providers (data sources can now be added, edited, tested and
  turned off in the admin UI for SQL Server and PostgreSQL; hard delete is deliberately not offered)
- Manually-written SQL as an alternative to the visual builder

## Contributing

Issues and pull requests welcome at [github.com/nagendra571/QueryBuilderEditor](https://github.com/nagendra571/QueryBuilderEditor).

## License

MIT — see [LICENSE](LICENSE).
