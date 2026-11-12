# QueryBuilder.Editor

[![NuGet](https://img.shields.io/nuget/v/QueryBuilder.Editor.svg)](https://www.nuget.org/packages/QueryBuilder.Editor)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/nagendra571/QueryBuilderEditor/blob/main/LICENSE)

A visual, no-SQL-required query builder for business users, embeddable as a Razor-free ASP.NET
Core component: browse a catalog of your SQL Server or PostgreSQL views and tables, build a query by dragging
columns and setting filters/sorts, preview the generated SQL, run it, export to CSV/Excel, and
share it — all backed by a full audit trail of who did what and when.

The UI ships as a built React app embedded directly in this package. There's no separate frontend
process to run or deploy — one call wires up the API, the UI, and the database.

## Requirements

- .NET 8 (uses EF Core 9) or .NET 10 (uses EF Core 10)
- SQL Server for QueryBuilder's own metadata store (always), and for business data sources
- PostgreSQL is supported for business data sources too (see "PostgreSQL data sources")

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
you. Navigate to `/querybuilder`.

QueryBuilder always mounts under `/querybuilder` — it never takes over your app's root, so it's
safe to drop into an app that already has its own home page. Add a nav link wherever makes sense
in your own layout: `<a href="/querybuilder">Queries</a>`.

QueryBuilder's API routes serialize and parse JSON with their own fixed contract (camelCase
properties, string enums) — independent of whatever you've configured (or not configured) in your
own `ConfigureHttpJsonOptions`. You never need to set up JSON options for QueryBuilder to work.

On first run with zero data sources registered, QueryBuilder automatically registers a **"Default"**
one pointed at its own connection string above — so the catalog isn't empty out of the box. This
only fires once (it never runs again once at least one data source exists), and it's silent, not
fatal, if it can't (e.g. the schema isn't provisioned yet in a DBA-managed setup).

To register additional business data sources you can use the admin UI (see "Adding data sources in the
admin UI" below), or insert a row into the `DataSources` table (or run your own seeding code) pointing `ConnectionStringName` at another
entry under `ConnectionStrings` in your configuration. For SQL Server the catalog sidebar reads
directly from that source's `sys.tables`/`sys.views`.

```sql
INSERT INTO DataSources (Id, Name, Description, Provider, ConnectionStringName, AllowedSchemas, AllowedObjects, CatalogScope, IsActive, CreatedBy, CreatedAtUtc)
VALUES (NEWID(), 'Reporting', 'Views over the reporting database', 0, 'ReportingDb', 'reporting', '', 0, 1, 'system-seed', SYSUTCDATETIME());
```

`ConnectionStringName` ('ReportingDb' above) must match a key under `ConnectionStrings` in your
own configuration — QueryBuilder resolves it through your app's `IConfiguration`, never stores the
raw string itself. `Provider` is `0` for SQL Server and `1` for PostgreSQL. `AllowedSchemas` and
`AllowedObjects` are comma-separated lists; an empty string means no restriction. `CatalogScope` is an
integer: `0` = views only (recommended: keep business logic in SQL views and let business users only
ever pick from those, not raw tables), `1` = tables only, `2` = tables and views.

## PostgreSQL data sources

A data source can be a PostgreSQL database instead of SQL Server. Business users get the same
catalog, builder, parameters, run, export, charts, dashboards, sharing, record limits and row-level
scope. One host can use both kinds of source at the same time, and a dashboard can mix tiles from both.

**QueryBuilder's own tables stay on SQL Server.** `options.ConnectionString` (the metadata store) must
still point at SQL Server, so a company that only runs PostgreSQL still needs a SQL Server database
for QueryBuilder itself. Only the business data sources can be PostgreSQL.

**1. Add an Npgsql connection string.** The entry uses Npgsql syntax, including its SSL options:

```json
{
  "ConnectionStrings": {
    "SalesPg": "Host=db.example.com;Port=5432;Database=sales;Username=qb_reader;Password=...;SSL Mode=Require"
  }
}
```

**2. Register the data source** in the metadata database (SQL Server), with `Provider = 1`:

```sql
INSERT INTO DataSources (Id, Name, Provider, ConnectionStringName, AllowedSchemas, AllowedObjects, CatalogScope, IsActive, CreatedBy, CreatedAtUtc)
VALUES (NEWID(), 'Sales PG', 1, 'SalesPg', '', '', 2, 1, 'admin', SYSUTCDATETIME());
```

`AllowedSchemas` and `AllowedObjects` are empty here, which means no restriction. `CatalogScope` `2`
shows tables and views; use `0` for views only. Table kinds shown: ordinary, partitioned (parent
only), and foreign tables as tables; views and materialized views as views. Partition children, the
`information_schema` and `pg_*` schemas are hidden, and so is anything the login can't read.

**Use a read-only login.** Give the `Username` above only `SELECT` on what business users should see.
QueryBuilder also runs every query in a read-only transaction with a 60-second timeout, but that
guards against mistakes, not against a hostile database.

**Sharing** uses an opt-in view `public.app_users` in that PostgreSQL database, with the columns
`id`, `display_name` and `email`. `id` is read as text and must equal what `ActorResolver` returns.
Without the view, sharing is reported as not available for that data source.

```sql
CREATE VIEW public.app_users AS
SELECT id, display_name, email FROM your_users_table;
```

**Limits to know about**

- `money` columns display as the database formats them (`$12.50`) and can't be totaled or charted.
  Workaround: a view that casts the column to `numeric`.
- `numeric` values beyond what .NET's `decimal` holds display exactly in the grid and exports, but
  charts plot them at the nearest double.
- Objects whose `schema.name` clash, ignoring case, with another object (for example `public.Orders`
  and `public.orders`) are shown disabled and can't be used. Scoped users don't see them at all. Add a
  uniquely named view over the one you want to expose.
- A column with a nondeterministic collation can't be a scope column.
- `NaN` and `Infinity` display as blank.
- Equals on a `citext` column is case-sensitive. Contains, Starts with and Ends with ignore case.
- Filtering an enum column goes through `::text`, so it doesn't use the column's index.
- Column aliases longer than 63 bytes are refused.
- The login should be read-only anyway (above).
- PostgreSQL 12 is the minimum: the catalog reads `collisdeterministic`.
- A table the login can read only through column-level grants doesn't appear; grant `SELECT` on the whole table or add a view.
- An object name containing a comma can't be allowlisted.
- Sessions are pinned to UTC, so a `timestamptz` displays and filters as UTC, and `current_date` or `::date` inside a view follows UTC rather than the database's time zone.
- A value over Excel's 32,767-character cell limit refuses the Excel export with the row and column named; CSV export still works.
- PgBouncer is untested.

Result ordering follows PostgreSQL: `NULL`s sort last ascending, and `AVG` of integers keeps its
fraction.

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

## Sharing

Saved queries are private to their owner by default. To let an owner share a query with someone
else — Viewer (open/run/export) or Editor (can also overwrite the saved definition) — add a
`dbo.AppUsers` view to **that query's data source's own database** (not QueryBuilder's metadata
DB, unless that happens to be the same database):

```sql
CREATE VIEW dbo.AppUsers AS
SELECT Id, DisplayName, Email FROM YourExistingUsersTable;
```

- `Id` **must equal exactly what `ActorResolver` returns for that person** — it's what access
  checks match against, the same way `CreatedBy` is stamped.
- `DisplayName` and `Email` are just for the picker UI.

(PostgreSQL data sources use `public.app_users` instead, see "PostgreSQL data sources".)

The Share dialog only appears once this view is queryable for a query's data source — no separate
setting to flip. Since each data source can define its own `AppUsers`, sharing is naturally scoped:
someone only shows up as shareable if their data source's own admin has vouched for them there.
Access is a durable grant recorded when you share, independent of `AppUsers` afterward — removing
someone from `AppUsers` doesn't revoke an existing share (the owner still needs to remove it
explicitly), it just flags that share as stale in the manage-sharing view and stops the person
appearing as a new pick.

## Admin UI

At `/admin` in the SPA, an admin can control, per data source, whether the catalog shows views
only / tables only / both, and allowlist specific tables/views — replacing direct SQL edits to
`DataSources.CatalogScope`/`AllowedObjects` for this one concern. Gated by a second, independent
authorization option:

```csharp
options.AdminAuthorization.Mode = QueryBuilderAuthorizationMode.Role;
options.AdminAuthorization.RoleNames = ["QueryBuilderAdmin"];
```

Same shape as `Authorization` (`Mode`/`RoleNames`/`PolicyName`), and composes with it — a request
must satisfy both. Defaults to `Anonymous` like the rest of the package; set this explicitly before
production, since the admin area exposes a data source's full unfiltered schema.

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
`MANAGING-KEYS.md` at the root of this package (open the `.nupkg` as a zip, or find it under
`~/.nuget/packages/querybuilder.editor/<version>/`).

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

## Row-level Data Scoping

Limit which **rows** a user sees — e.g. a Program-admin with `ProgramId = 10` only ever sees rows
where `ProgramId = 10`, while Super admins see everything. The package stays generic: you name the
scope keys (`ProgramId`, `ModuleId`, `SystemId`, ...) and supply each user's values; admins decide
which view column each key filters on.

```csharp
options.DataScope.Keys = ["ProgramId"];
options.DataScope.Resolver = ctx =>
    ctx.User.IsInRole("SuperAdmin") || ctx.User.IsInRole("Admin")
        ? DataScope.Unrestricted
        : DataScope.For("ProgramId", GetProgramIds(ctx));   // one value, several, or a collection
// Several keys: DataScope.For("ProgramId", 10).And("RegionId", "EU")
// Need a DB/API lookup? Use options.DataScope.ResolverAsync instead (set one or the other).
```

`Keys` can also be left empty: admins can add keys per data source in the admin UI (**Row-level data
scope** > **Scope keys**), next to the ones set in code, which show there as locked. Keys only name
what a view can be mapped to; **the values for every key always come from your resolver**, so a view
mapped to a key the resolver doesn't supply is hidden from scoped users. A key added to one data source
doesn't apply to another. Removing a key hides the views mapped to it from scoped users until the key is
added back (the mappings are kept) or the views are mapped again. The admin page needs a resolver in
code; without one it only says data scoping is turned off.

Then, in the admin UI (**Row-level data scope** on a data source's page), mark each view as:

| Admin decision | Scoped user | Unrestricted user |
|---|---|---|
| **Undecided** (default) | hidden | all rows |
| **Not scoped** | all rows | all rows |
| **Scoped** — key → column (e.g. `ProgramId` → `ProgramId`, or `Country` → `ShippingCountry`) | only rows where the column is in their values (`IN`, ANDed across keys); hidden if they have no value for a mapped key | all rows |

Fail-closed by design:

- A view nobody has decided on is hidden from scoped users, so a newly created view can't leak.
- A resolver that returns `null` or throws means **denied** (only "not scoped" views are visible),
  never unrestricted. Return `DataScope.Unrestricted` explicitly for users who see everything.
- A mapping to a key that is no longer a scope key (removed in code or in the admin page), or to a column the view
  no longer has, hides the view until an admin fixes it.

The filter is added server-side to the generated SQL's `WHERE` (before grouping, so totals only
count in-scope rows) as bound parameters, on every path: catalog, run, export, SQL preview, and
saving. Shared queries always run with the *runner's* scope. The SQL preview shows the filter, and
the audit log records the scope for every run and export. With no resolver configured the feature
is off and nothing changes.

## Query Parameters

Any filter can be turned into a parameter with the sparkle button ("Ask the user at run time"). Its
settings (gear icon) choose how it is asked, limited by the filter's column and condition:

| Type | Filter it fits |
|---|---|
| Text, Number, Date, Date and time, True/false | a normal comparison on a matching column |
| Dropdown list (fixed options) | a comparison, or *is any of* / *is none of* |
| Dropdown from a query | same, with choices from another saved query |
| Date range / Date-and-time range | *between* on a date or date-time column |

- **Parameter bar:** parameters appear above the results with an **Apply** button; nothing runs until
  Apply. Required parameters without a value disable Apply. Editors can **Save as defaults** (then Save).
- **Several values:** a list parameter on an *is any of* / *is none of* filter can allow multi-select.
- **Relative dates:** Today, Yesterday, This/Last week/month/year, Last 7/14/30/60/90 days and
  Last 12 months are stored as tokens and worked out **on the server** when the query runs (server
  local date, weeks start Monday, ranges include both ends).
- **Links:** the bar's values are mirrored in the address as `p_<name>=...` (several values repeat the
  key, ranges are `start~end`, relative dates are tokens); opening such a link pre-fills the bar.
- **Safe by construction:** every value is validated on the server (type, list membership, at most
  1,000 values, unknown names rejected, one error listing every invalid parameter) and then sent to
  the database as a bound SQL parameter, never as SQL text. Row-level data scope and record limits
  apply on every path (run, export, SQL preview, charts, dashboard tiles).
- **Query-based dropdowns:** `GET .../api/queries/{id}/parameters/{name}/options` runs the source
  query live as the caller (up to 1,000 distinct choices). The caller must be able to view the
  source query (otherwise 403), the source must not be disabled, and the caller's row-level scope and
  record limits apply, so a dropdown never shows values outside the caller's data. Columns named
  `name`/`value` supply label/value, otherwise the first column; both can be chosen in the settings.
- Queries saved before parameter types existed keep working unchanged. A filter left without a value
  on an optional (not required) parameter is simply not applied.

## Record Limits

Stop business users pulling huge result sets by capping how many records a query may return:

```csharp
options.DefaultMaxRecords = 5000;   // package-wide default, 1 to 100,000
```

Each data source can override it in the admin UI (**Maximum records per query** on the data
source's page; leave it empty to fall back to the default). When a query returns more than the limit:

- **Results grid:** shows only the first *N* rows with a warning — *"This query returns more than
  5,000 records. Only the first 5,000 are shown. Add filters to narrow down the results."*
- **Export (CSV/Excel):** refused (HTTP 409) with a message asking the user to add filters, rather
  than producing a file that silently stops at the limit.

Detection costs one extra row (the query fetches *N + 1*), never a separate `COUNT(*)`. With neither
`DefaultMaxRecords` nor a per-data-source limit set, behavior is unchanged from earlier versions: the
grid shows up to 1,000 rows (now with the same warning banner when there are more) and exports stop
at 100,000 rows without an error. Limit changes are recorded in the audit log.

## Visualizations

Turn a saved query's results into charts. Open a saved query, click **Add chart** next to the
tabs, choose a type, and map the query's output columns:

| Type | Needs |
|---|---|
| **Bar** (grouped, stacked or 100% stacked) | one category column + 1-5 number columns, or one number column split by a series column |
| **Horizontal bar** | same as Bar; better for long names and rankings |
| **Combo** | one category column + 2-5 number columns, each drawn as a bar or a line, on one shared axis |
| **Waterfall** | one category column + one signed number column, at most 30 bars, with an automatic final total |
| **Line** | one X column (date, number or text) + 1-5 number columns, or one number column split by a series column |
| **Area** (optionally stacked or 100% stacked) | same as Line |
| **Trend** | one date or number X column + one number column; the latest value, its change from the previous one, and a sparkline |
| **Pie** | one category column + one number column, at most 12 slices, no negative values |
| **Funnel** | one step column + one number column, at most 20 steps, no negative values; a table with in-cell bars, % of first and % of previous, in query order by default |
| **Scatter / bubble** | a number or date X column + a number Y column; optional size (not negative), label and series columns |
| **Table** | 1-30 columns in your order; paged 25 per page |
| **Pivot** | 1-3 row columns, an optional column column (at most 50 distinct values), 1-5 number columns; optional totals and cell shading (a heatmap) |
| **Counter** | one number column, shows the first row as a large figure; an optional target column adds "82% of target" and a thin bar |

Every type offers a number format from a list: number, compact, currency (a three-letter code such as
`USD`) or percent, with 0-4 decimals. Percent shows the value as stored: 42 reads `42%`.

**Pivot totals are never made up.** Pivot works on your query's rows, which may already be totals, so the
column's aggregate decides what is combined. Sum, Count and plain numbers add; Min and Max keep the
smallest or largest; Average and Count Distinct can't be combined, so several rows in one cell and every
total show "—" with a footnote.

Table, Pivot and Funnel cells are always plain text. There's no option to show data as HTML, images or
links, because data from a business table would then run or load in other users' browsers.

Each chart appears as its own tab beside **Results**. Charts belong to one saved query and are
listed for anyone who can open it:

- **Who can change them:** the owner and Editor shares can add, edit and delete charts; Viewer
  shares can only look (the server refuses the calls, not just the UI). At most 20 charts per query.
- **How they are drawn:** a chart stores no data. When opened, the server loads the *saved* query
  and runs it through the same catalog validation and row-level data scope as a normal run, using
  the **viewer's** scope, never the owner's. Only the columns the chart uses are sent to the
  browser. Results are not cached between users. Disabling the query, or revoking a share, takes
  effect on the next render.
- **Row limits:** a chart never draws a partial result. If the query returns more rows than the
  effective record limit (see Record Limits), or more than 1,000 points, the chart is refused
  (HTTP 409) with a message asking the user to add filters or totals. **Table is the one exception:**
  it shows the first rows with the same banner as the results grid, since a list with a clear banner
  can't mislead the way a partial chart can.
- **Same X value:** when rows share an X value and no series is set, a footnote says so and suggests
  a Split by column or a total.
- **After the query changes:** a chart whose column was removed, renamed (alias) or hidden shows
  "This chart needs updating" instead of a partial chart; owners and Editors can fix it with
  **Edit chart**. Charts are never deleted by editing a query; deleting the query deletes its charts.
- **Parameters:** on the query page a chart is drawn with the values applied in the parameter bar
  (or the query's defaults); when a required parameter has no default it waits for you to Apply.
- **Missing values:** a blank category is labelled "(blank)", and a missing number is left as a
  gap, never drawn as zero.
- **Accessibility:** every drawn chart has a text summary for screen readers and a **Show data**
  toggle that lists the plotted values as a table; Table, Pivot and Funnel are real tables; series are
  told apart by legend text and tooltips, not colour alone; animation is off under
  `prefers-reduced-motion`.

Adding the feature creates one new table, `QueryVisualizations` (migration `AddQueryVisualizations`),
applied by auto-migration or by the idempotent script. The types beyond Bar, Line, Pie and Counter need no
further migration, and charts saved earlier keep working unchanged. Chart create/update/delete are recorded in the
audit log, and each render is recorded as a query run with the chart's id and the viewer's scope.

## Dashboards

A dashboard is a page of **tiles**: charts from saved queries (see Visualizations) and plain-text
notes. Open **Dashboards** in the sidebar, choose **New dashboard**, then **Add chart** (pick from
every chart on queries you own or that were shared with you) or **Add text**. **Add section** inserts a section heading: a title (up to 80 characters) with an optional
one-line description (up to 160), drawn as bare text with a rule across the page rather than as a card; it is always
full width and one row high, and counts toward the 30-tile limit. Tiles sit on a 12-column
grid. In edit mode, drag a tile from anywhere on it and resize it from its bottom-right corner; the
other tiles move out of the way and gaps close up automatically. Without a mouse, each tile's
**Layout** menu moves it (left, right, up, down), makes it wider/narrower/taller/shorter, applies size
presets, or removes it, and every change is announced to screen readers. **Fit height to content**
lets a note or chart size its own height (resizing its height by hand turns that off). **Save** stores
the layout; **Cancel** discards your changes. On screens narrower than 768px the dashboard shows as a
single column and layout editing is off. At most 30 tiles; a note is plain text of up to 2,000
characters (never HTML or markdown). The server checks every position (width 2–12 columns, height
2–24 rows, inside the grid) and closes any gaps, so a malformed request cannot corrupt a layout;
dashboards saved before this version are converted on first open (migration
`AddDashboardGridPositions`). Grid library: `react-grid-layout` (MIT).

- **Sharing never grants data access.** Share a dashboard from its page (owner only) as Viewer or
  Editor, using the same `AppUsers` picker as query sharing. A dashboard stores only references to
  charts. Every chart tile is drawn through the normal chart endpoint, which re-checks the viewer's
  access to the query, whether it is disabled, the catalog policy, their row-level scope and the
  record limits on each render. The dashboard endpoints never return data rows.
- **Restricted tiles.** If a viewer cannot open a chart's query, the tile is returned as
  *restricted*: only its position and size are sent, with no chart name, query name or ids.
- **Add to dashboard from the query page.** Each chart on a saved query has **Add to dashboard**: pick
  any dashboard you can edit (or create one in the same dialog) and the chart is appended as a new
  tile (`POST /api/dashboards/{id}/tiles/chart`). The server requires edit rights on the dashboard and
  view rights on the chart's query, and applies the 30-tile limit.
- **Your zone is always visible.** When row-level data scoping is configured, the sidebar names the
  signed-in user and their scope (e.g. "ZONE · ProgramId 10"), and each chart tile carries a small
  "Your zone" key, so a viewer can tell the chart is drawn from their rows only. Restricted tiles are
  drawn with a diagonal hatch. The shell reads this from `GET /api/me`, which only ever describes the
  caller.
- **Also share the queries.** When you share a dashboard, the dialog lists the charts that person
  cannot see and offers (unchecked by default) **Also share these N queries as Viewer**. Only queries
  you own, and that are used on the dashboard, can be shared this way; an existing Editor share is
  never downgraded.
- **Who can do what.** Owner: everything, including delete and sharing. Editor: rename and change
  tiles (adding a chart needs access to that chart's query; charts an Editor cannot see are kept as
  they are and can only be moved, resized or removed). Viewer: look only. The server enforces this.
- **Concurrent edits.** Saving or renaming sends the dashboard's version; if someone else changed it
  first the server answers 409 and the page offers **Reload** instead of overwriting their work.
- **Refresh.** **Refresh all** re-runs every chart; auto-refresh can be Off, 1, 5, 15 or 30 minutes
  (never faster than a minute) and pauses while the browser tab is hidden. At most 4 charts load at
  once. A chart whose query needs a required value that no source supplies asks for it per tile (kept
  in the browser only); see "Dashboard parameters" below.
- **Deleted charts.** Deleting a chart (or its query) leaves the tile in place showing "This chart
  was removed"; deleting a dashboard removes its tiles and shares.

### Dashboard parameters

Charts whose queries have run-time parameters (see "Query Parameters") can be driven from the
dashboard. In **Edit** mode, open a chart tile's menu and choose **Parameters...**; for each
parameter of that chart's query pick where its value comes from:

| Source | Behaviour |
|---|---|
| Query default | The query's own default (today's behaviour; nothing stored). |
| Dashboard parameter | An existing compatible dashboard parameter, or **New dashboard parameter** (copies the query parameter's type, choices and default). One dashboard parameter can feed charts from different queries. |
| Tile input | A small labelled control with its own Apply in the chart's header. |
| Fixed value | A value you set; viewers see no control for it. |

Dashboard parameters appear as a bar above the tiles (same controls as a query's parameter bar). Edits
apply only on **Apply**, which redraws every chart that uses them and mirrors the values in the
address as `?p_<name>=...` (several values repeat the key, ranges are `start~end`, relative dates are
tokens such as `last_30_days`), so a link opens the dashboard with those values. In Edit mode the gear
beside a dashboard parameter renames it, sets the value viewers start from, or removes it. A dashboard
can have up to 20 parameters; names are letters, digits and underscore.

Compatibility: text, number, date/date-time, date range, dropdown (fixed or query-based) and true/false
parameters connect only to query parameters of the same kind; a multi-select dashboard parameter only
feeds a query parameter that allows several values. The server checks this on save, and checks that
every mapped query parameter exists and that fixed values pass the same validation as a run.

Security: nothing about a mapping weakens access. Every chart is still drawn by the render endpoint
as the viewer, and each value (from the bar, a link, a tile control or a fixed mapping) is validated
and bound as a SQL parameter exactly like a run, with the viewer's row-level scope and record limits
applied. A tile whose query the viewer cannot see keeps its stored mappings but returns none of them.
Dashboards saved before this version have no dashboard parameters and every chart uses its query
defaults, with the per-tile **Set values** prompt still available for required values with no source.

Stored as JSON: `Dashboards.ParametersJson` is
`[{ "name", "title", "type", "options", "optionsQueryId", "optionsLabelColumn", "optionsValueColumn", "allowMultiple", "value" }]`
and `DashboardTiles.ParameterMappingsJson` is
`{ "<queryParam>": { "source": "dashboard|tile|fixed", "dashboardParameter", "value", "title" } }`.
The save request (`PUT /api/dashboards/{id}/tiles`) carries `parameters` and a per-tile
`parameterMappings`; leaving either out keeps what is stored, an empty list/object clears it.

Adding the feature creates three tables, `Dashboards`, `DashboardTiles` and `DashboardShares`
(migration `AddDashboards`), applied by auto-migration or by the idempotent script. Dashboard
parameters add two nullable `nvarchar(max)` columns (migration `AddDashboardParameters`). Dashboard
create/update/delete/share/unshare are recorded in the audit log.

## Hub mode (2.0.0)

Hub mode turns one QueryBuilder into a **hub** that serves several apps. Each app (a tenant) hands its signed-in users
over with a short-lived, signed **launch token**; your own staff, the **super admins**, sign in with Microsoft Entra ID.
It is opt-in: with `options.Hub.Enabled` left `false`, QueryBuilder behaves as in every earlier release, and an existing
embedded installation needs nothing but the database upgrade described below.

Three pieces make it work:

| Piece | What it is | Guide |
|---|---|---|
| `QueryBuilder.Editor` | this package; hub mode is switched on with `options.Hub` | this section |
| `QueryBuilder.Hub` | a ready-made `net10.0` host you deploy (not a NuGet package): Entra sign-in, HTTPS and HSTS, forwarded headers, a launch rate limit | [deployment guide](https://github.com/nagendra571/QueryBuilderEditor/blob/main/src/QueryBuilder.Hub/README.md) |
| `QueryBuilder.Launch` | a small `netstandard2.0` package your apps use to sign the token and render the launch form; works from .NET Framework 4.7.2+ | [integration guide](https://github.com/nagendra571/QueryBuilderEditor/blob/main/src/QueryBuilder.Launch/README.md) |

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

Hub mode refuses to start without `Audience`, `SuperAdminSignInScheme`, at least one tenant id, at least one object id, email
or group id, and `DataSourceManagement.EncryptionKey`. `options.Hub.CookieName` (default `__Host-qb-hub`),
`AbsoluteLifetime` (8 hours) and `IdleTimeout` (1 hour) can be changed. A super admin signs in at `/querybuilder/signin`.
A host of your own must use HTTPS, `UseForwardedHeaders` behind a proxy, `UseExceptionHandler`, no `UsePathBase`, and send
`frame-ancestors 'self'`; `QueryBuilder.Hub` does all of that.

### Who can do what

A person's role comes from the **latest launch token only**; it is never stored as a permanent grant.

| Role | Comes from | Can |
|---|---|---|
| Super admin | Entra sign-in plus the allowlist; never from a launch token | Everything in every app: the app registry (apps, signing keys, return URLs, scope keys, record ceiling), locking, unlocking and moving data sources, the encryption key, the hub audit log. Unrestricted data scope. |
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
- **Group-based super admins are re-checked against Entra only at sign-in.** The allowlist itself is checked on every request,
  but removing someone from an Entra group ends their session only when it expires (8 hours at most) or when you remove the
  group id and restart. Prefer `ObjectIds`: an email can be changed by a tenant administrator.
- **Do not start hosted services concurrently** (`HostOptions.ServicesStartConcurrently`): the steps that migrate and claim the
  database depend on starting in order.
- **The hub needs its own empty metadata database.** See the next section.

### Upgrading the database to 2.0.0

2.0.0 adds four migrations: `AddApps`, `AddHubRegistry`, `AddAppSessionsValidAfter` and `AddHubRolesAndPublishing`. **DBA-managed
hosts must run the new schema script before deploying, even if they never use hub mode** (shipped at
`scripts/QueryBuilder.schema.<version>.sql` in this package; see [`Scripts/README.md`](https://github.com/nagendra571/QueryBuilderEditor/blob/main/src/QueryBuilder.Editor/Scripts/README.md)).

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
already-migrated one. With `sqlcmd`, pass `-I` (the script's filtered indexes need `QUOTED_IDENTIFIER`).

## Setup Diagnostics

Once registered, visit `GET /_setup` in Development to check database connectivity, migration
status, API/UI wiring, JSON serialization options, and the authorization pipeline all at once.
Every failing check includes a one-line fix. Returns 404 outside Development.

## What's included

- Hub mode (opt-in): one QueryBuilder serving several apps through signed launch tokens, with Admin/Author/Viewer roles,
  published dashboards and Entra-signed-in super admins (see "Hub mode (2.0.0)")
- Business data sources on SQL Server or PostgreSQL, mixed freely on one host
- Searchable catalog sidebar (schemas → tables/views → columns) with type-aware icons
- Drag-and-drop column selection, reordering, and aliasing
- Column totals (Sum, Average, Count, Distinct count, Min, Max) with automatic grouping and a
  totals-only filter (HAVING) — no separate "Group By" step
- Visual filter builder with type-aware inputs (date picker, checkbox, number, text) and
  typed run-time parameters (text, number, date, date range with relative dates, dropdowns from a
  list or another query, multi-select) in a parameter bar with Apply
- Read-only generated SQL preview
- Run queries and browse results with client-side pagination, search, column show/hide, reorder,
  and sort
- Export results to CSV or Excel
- Save, reopen, and re-run queries; per-query history tab
- Share a saved query as Viewer or Editor, scoped per data source via an optional `AppUsers` view
- Disable a saved query to block anyone from running or exporting it without deleting it —
  owner-only, reversible
- Admin UI (separately role-gated via `AdminAuthorization`) to control each data source's catalog
  scope (views/tables/both) and a specific table/view allowlist
- Optional admin UI to add, edit, test and turn off SQL Server and PostgreSQL data sources, with the
  connection stored encrypted using a key you provide (`options.DataSourceManagement`)
- Row-level data scoping: host-supplied scope keys/values per user (e.g. ProgramId), mapped to view
  columns by an admin; fail-closed for undecided views
- Record limits: a package-wide default plus a per-data-source maximum; oversized results show the
  first N rows with a "narrow it down" warning, oversized exports are refused
- Saved visualizations: Bar, Horizontal bar, Combo, Waterfall, Line, Area, Trend, Pie, Funnel,
  Scatter/bubble, Table, Pivot and Counter per saved query, with number formats, drawn with the
  viewer's own data scope; charts over too many rows are refused instead of drawn partially (Table
  shows the first rows with a banner)
- Dashboards: charts from several saved queries plus text notes on one page, shared with named
  users; each viewer sees only the charts their own query access and data scope allow
- Full audit log (created/updated/deleted/run/exported/shared/unshared/disabled/enabled/catalog
  policy updated/data scope updated/record limit updated/chart created/updated/deleted) with a configurable actor identity and
  access control

## Links

- [Source & sample app](https://github.com/nagendra571/QueryBuilderEditor)
- [Issues](https://github.com/nagendra571/QueryBuilderEditor/issues)

## License

MIT
