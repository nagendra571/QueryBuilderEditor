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

Clean Architecture, four internal layers plus the package root and a sample host:

```
src/
  QueryBuilder.Domain           entities, enums, the QueryDefinition model — no dependencies
  QueryBuilder.Application      commands/queries, validation, service abstractions
  QueryBuilder.Infrastructure   EF Core, SQL generation, catalog introspection, audit log, export
  QueryBuilder.Editor           ← the published package: DI/endpoint wiring, access control,
                                  setup diagnostics, and the built React app embedded as a resource
  QueryBuilder.Api              sample host app — consumes QueryBuilder.Editor like any real
                                  integration would, plus demo data seeding
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
- Row-level data scoping: the host declares scope keys (`ProgramId`, `ModuleId`, ...) and a
  resolver returning each user's values or `DataScope.Unrestricted`; admins map each key to a view
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
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

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

## Roadmap

Not yet built, tracked for a future version:

- Admin UI editing of `AllowedSchemas` and other providers (data sources can now be added, edited, tested and
  turned off in the admin UI for SQL Server and PostgreSQL; hard delete is deliberately not offered)
- Manually-written SQL as an alternative to the visual builder

## Contributing

Issues and pull requests welcome at [github.com/nagendra571/QueryBuilderEditor](https://github.com/nagendra571/QueryBuilderEditor).

## License

MIT — see [LICENSE](LICENSE).
