# QueryBuilder.Editor

[![NuGet](https://img.shields.io/nuget/v/QueryBuilder.Editor.svg)](https://www.nuget.org/packages/QueryBuilder.Editor)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/nagendra571/QueryBuilderEditor/blob/main/LICENSE)

A visual, no-SQL-required query builder for business users, embeddable as a Razor-free ASP.NET
Core component: browse a catalog of your SQL Server views and tables, build a query by dragging
columns and setting filters/sorts, preview the generated SQL, run it, export to CSV/Excel, and
share it — all backed by a full audit trail of who did what and when.

The UI ships as a built React app embedded directly in this package. There's no separate frontend
process to run or deploy — one call wires up the API, the UI, and the database.

## Requirements

- .NET 8
- SQL Server (for both QueryBuilder's own metadata store and the business data sources it queries)

## Install

```bash
dotnet add package QueryBuilder.Editor
```

## Quick Start

**1. Add a connection string**

```json
// appsettings.json
{
  "ConnectionStrings": {
    "QueryBuilderDb": "Server=.;Database=QueryBuilder;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

**2. Register in Program.cs**

```csharp
using QueryBuilder.Editor;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddQueryBuilderEditor(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("QueryBuilderDb")!;
});

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseQueryBuilderEditorUI();   // serves the embedded React app + SPA fallback routing
app.MapQueryBuilderEditor();     // maps the API (data sources, saved queries, audit log)

app.Run();
```

**3. Run**

```bash
dotnet run
```

EF Core migrations run automatically on first startup — the database and schema are created for
you. Navigate to `/`.

No admin UI registers a business data source yet in this release — insert a row into the
`DataSources` table (or run your own seeding code) pointing `ConnectionStringName` at another
entry under `ConnectionStrings` in your configuration. The catalog sidebar reads directly from
that source's `sys.tables`/`sys.views`.

```sql
INSERT INTO DataSources (Id, Name, Description, Provider, ConnectionStringName, AllowedSchemas, ViewsOnly, IsActive, CreatedBy, CreatedAtUtc)
VALUES (NEWID(), 'Reporting', 'Views over the reporting database', 0, 'ReportingDb', 'reporting', 1, 1, 'system-seed', SYSUTCDATETIME());
```

`ConnectionStringName` ('ReportingDb' above) must match a key under `ConnectionStrings` in your
own configuration — QueryBuilder resolves it through your app's `IConfiguration`, never stores the
raw string itself. `ViewsOnly = 1` is recommended: keep business logic in SQL views and let
business users only ever pick from those, not raw tables.

## Author Identity (CreatedBy)

Every row that records an author (`SavedQuery.CreatedBy`/`OwnerId`, and the audit log's `Actor`)
is stamped with the current user, resolved in this order:

1. `options.ActorResolver` (your custom resolver, if set)
2. `User.Identity.Name`
3. `"anonymous"`

```csharp
builder.Services.AddQueryBuilderEditor(options =>
{
    options.ConnectionString = connectionString;
    options.ActorResolver = ctx => ctx.User?.FindFirst("sub")?.Value;
});
```

The resolver receives the request's `HttpContext`, runs once per request, and its exceptions
propagate rather than being swallowed. A `null`/blank result falls through to the next step in
the chain.

## Access Control

By default the editor is **open to all users** — no authentication required.

```csharp
using QueryBuilder.Editor.Authorization;

// Any signed-in user
options.Authorization.Mode = QueryBuilderAuthorizationMode.Authenticated;

// One or more roles (a user in any of them is granted access)
options.Authorization.Mode = QueryBuilderAuthorizationMode.Role;
options.Authorization.RoleNames = ["Admin", "Analyst"];

// Escape hatch: delegate to a policy you registered yourself
options.Authorization.PolicyName = "MyCustomPolicy";
```

Applied uniformly to every mapped route. For anything other than `Anonymous`, your pipeline needs
`app.UseAuthentication()` then `app.UseAuthorization()`, in that order, before `MapQueryBuilderEditor()`.

## Database

`AddQueryBuilderEditor()` registers a hosted service that runs EF Core migrations on startup — no
manual migration steps required.

If your SQL login has no DDL rights (a DBA-managed database), set `options.ApplyMigrations = false`
and have your DBA run the idempotent script shipped at `scripts/QueryBuilder.schema.<version>.sql`
in this package instead. It's safe to run against a fresh database or re-run against an
already-migrated one.

## Setup Diagnostics

Once registered, visit `GET /_setup` in Development to check database connectivity, migration
status, API/UI wiring, JSON serialization options, and the authorization pipeline all at once.
Every failing check includes a one-line fix. Returns 404 outside Development.

## What's included

- Searchable catalog sidebar (schemas → tables/views → columns) with type-aware icons
- Drag-and-drop column selection, reordering, and aliasing
- Visual filter builder with type-aware inputs (date picker, checkbox, number, text) and
  run-time-prompted parameters
- Read-only generated SQL preview
- Run queries and browse results with client-side pagination, search, column show/hide, reorder,
  and sort
- Export results to CSV or Excel
- Save, reopen, and re-run queries; per-query history tab
- Full audit log (created/updated/deleted/run/exported) with a configurable actor identity and
  access control

## Links

- [Source & sample app](https://github.com/nagendra571/QueryBuilderEditor)
- [Issues](https://github.com/nagendra571/QueryBuilderEditor/issues)

## License

MIT
