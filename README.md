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

**Prerequisites:** .NET 8 SDK, Node.js 18+, SQL Server (LocalDB is fine for development).

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

- Searchable catalog sidebar (schemas → tables/views → columns) with type-aware icons, sourced
  live from `sys.tables`/`sys.views` — no manual schema configuration
- Drag-and-drop column selection, reordering, and aliasing
- Column totals (Sum, Average, Count, Distinct count, Min, Max) with automatic grouping and a
  totals-only filter (HAVING) — no separate "Group By" step
- Visual filter builder with type-aware inputs (date picker, checkbox, number, text) and
  run-time-prompted parameters (`{{ selected_date }}`-style placeholders, filled in at run time)
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
- Row-level data scoping: the host declares scope keys (`ProgramId`, `ModuleId`, ...) and a
  resolver returning each user's values or `DataScope.Unrestricted`; admins map each key to a view
  column (or mark a view "not scoped"). Enforced server-side in the generated SQL's `WHERE` on every
  path (catalog, run, export, preview, save); undecided views and failing resolvers fail closed.
  See the package README's "Row-level Data Scoping" section
- Record limits: `options.DefaultMaxRecords` plus a per-data-source "Maximum records per query" in
  the admin UI. Past the limit the grid shows the first N rows with a warning to add filters, and
  exports are refused instead of being silently cut off. Unset = unchanged behavior. See the package
  README's "Record Limits" section
- Configurable actor identity (`ActorResolver`) and access control (anonymous / authenticated /
  role / custom policy), applied uniformly across every route
- DBA-friendly database story: auto-migration by default, or an idempotent SQL script for
  DDL-restricted environments
- Auto-registers a default data source (against its own connection string) on first run so the
  catalog is never empty out of the box
- Compact/comfortable density modes, light/dark theme

## Roadmap

Not yet built, tracked for a future version:

- Admin UI for registering/editing data sources themselves (name, connection string, provider,
  `AllowedSchemas`) — still raw-SQL-only; only the catalog scope/allowlist is admin-manageable so far
- Manually-written SQL as an alternative to the visual builder
- Turning a result set into a saved visualization
- Dashboards composed of multiple visualizations

## Contributing

Issues and pull requests welcome at [github.com/nagendra571/QueryBuilderEditor](https://github.com/nagendra571/QueryBuilderEditor).

## License

MIT — see [LICENSE](LICENSE).
