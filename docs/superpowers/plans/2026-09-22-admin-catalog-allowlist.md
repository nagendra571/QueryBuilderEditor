# Admin UI: Catalog Table/View Allowlist Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an admin (role-gated via a new `QueryBuilderEditorOptions.AdminAuthorization`) control, per data source, whether the catalog shows views only / tables only / both, and which specific tables/views are allowed — replacing the current raw-SQL-only path for this one concern.

**Architecture:** `DataSource.ViewsOnly` (bool) becomes `DataSource.CatalogScope` (enum) plus a new `DataSource.AllowedObjects` (list, same storage pattern as the existing `AllowedSchemas`). A new `/api/admin` route group, nested under the existing authorized `/querybuilder` group and carrying its own `AdminAuthorization` gate (composes via ASP.NET Core's AND semantics — no new authorization plumbing), exposes read/write endpoints for this. The React SPA gets a role-gated "Admin" nav item and two new pages.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core (SQL Server), the project's hand-rolled mediator (`IRequest`/`IRequestHandler`/`ISender`), React + TanStack Query + shadcn/Radix + Tailwind.

**Spec:** `docs/superpowers/specs/2026-09-22-admin-catalog-allowlist-design.md`

## Global Constraints

- No automated test project exists in this repo (see `CLAUDE.md`'s Testing section). Every task's
  verification is: `dotnet build`/`npm run build` clean, plus a scripted HTTP check (via
  `mcp__plugin_context-mode_context-mode__ctx_execute` with `language: "javascript"`, using
  `fetch` against the locally running sample app — the pattern already used successfully for the
  saved-query-disable feature) and/or a Playwright pass against `http://localhost:5080`. There are
  no unit test steps in this plan — do not add a test project as part of this work.
- Every new endpoint uses `QueryBuilderJson.Options` explicitly via
  `HttpContext.Request.ReadFromJsonAsync<T>(QueryBuilderJson.Options, ct)` and
  `Results.Json(x, QueryBuilderJson.Options)` — never implicit minimal-API body binding.
- Don't reintroduce MediatR — use the existing `IRequest`/`IRequestHandler`/`ISender` in
  `Application/Common/Mediator.cs`.
- This plan stops at "implemented and verified." Do not bump `<Version>`, run `dotnet pack`, `dotnet
  nuget push`, or `git push` — those happen only when the user explicitly asks, per this repo's
  established release workflow (`CLAUDE.md`).
- Before any local test run, and after finishing this plan's manual verification, stop stray
  `dotnet` processes before rebuilding:
  `powershell -Command "Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object { \$_.StartTime -gt (Get-Date).AddHours(-N) } | Stop-Process -Force"`.
- Run the sample app with `dotnet run --project src/QueryBuilder.Api --no-launch-profile --urls
  http://localhost:5080` (background it) — `--no-launch-profile` matters, see `CLAUDE.md`.
- Leave the demo "Sales Sample" data source in a working, unrestricted state
  (`CatalogScope = TablesAndViews`, `AllowedObjects = []`) after every manual verification step
  that changes it, so later tasks and the user's own testing aren't left broken.

---

## File Structure

Backend:
- `src/QueryBuilder.Domain/Enums/Enums.cs` — modify: add `CatalogScope` enum, add
  `AuditAction.DataSourceCatalogPolicyUpdated`.
- `src/QueryBuilder.Domain/Entities/DataSource.cs` — modify: `ViewsOnly` → `CatalogScope` +
  `AllowedObjects`.
- `src/QueryBuilder.Infrastructure/Persistence/Configurations/DataSourceConfiguration.cs` —
  modify: EF conversion for `AllowedObjects`.
- `src/QueryBuilder.Infrastructure/Catalog/SqlServerDataCatalogService.cs` — modify: shared raw
  object query, `CatalogScope`/`AllowedObjects` filtering, `GetRawObjectsAsync`,
  `InvalidateCatalogCache`.
- `src/QueryBuilder.Application/Abstractions/IDataCatalogService.cs` — modify: new interface
  members.
- `src/QueryBuilder.Application/Abstractions/Repositories.cs` — modify: `IDataSourceRepository`
  gets `GetAllAsync`, `Update`, `SaveChangesAsync`.
- `src/QueryBuilder.Infrastructure/Repositories/DataSourceRepository.cs` — modify: implement the
  above.
- `src/QueryBuilder.Application/Dtos/SavedQueryDtos.cs` — modify: `DataSourceDto.ViewsOnly` →
  `CatalogScope`.
- `src/QueryBuilder.Application/DataSources/GetDataSourcesQuery.cs` — modify: use
  `s.CatalogScope`.
- `src/QueryBuilder.Infrastructure/Persistence/Migrations/` — new migration
  `AddCatalogScopeAndAllowedObjects`.
- `src/QueryBuilder.Editor/QueryBuilderEditorOptions.cs` — modify: add `AdminAuthorization`.
- `src/QueryBuilder.Editor/EndpointRouteBuilderExtensions.cs` — modify: map + authorize the admin
  group.
- `src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs` — new: `/api/admin/*` routes (built up
  across Tasks 2–4).
- `src/QueryBuilder.Application/Dtos/AdminDtos.cs` — new: admin DTOs.
- `src/QueryBuilder.Application/Queries.Admin/GetAdminDataSourcesQuery.cs` — new.
- `src/QueryBuilder.Application/Queries.Admin/GetAdminDataSourceDetailQuery.cs` — new.
- `src/QueryBuilder.Application/Commands.Admin/UpdateCatalogPolicyCommand.cs` — new.

Frontend:
- `client/src/types/index.ts` — modify: `CatalogScope`, `DataSourceProvider` types,
  `DataSourceDto` rename, new admin DTOs.
- `client/src/lib/api.ts` — modify: `adminApi`.
- `client/src/hooks/useAdmin.ts` — new: admin query/mutation hooks.
- `client/src/components/layout/AppShell.tsx` — modify: role-gated "Admin" nav item.
- `client/src/pages/AdminDataSourcesPage.tsx` — new.
- `client/src/pages/AdminDataSourcePolicyPage.tsx` — new.
- `client/src/App.tsx` — modify: `/admin` and `/admin/data-sources/:id` routes.

Docs:
- `README.md`, `src/QueryBuilder.Editor/README.md` — modify: Admin UI section + feature bullets.

---

### Task 1: Data model — `CatalogScope` + `AllowedObjects`

**Files:**
- Modify: `src/QueryBuilder.Domain/Enums/Enums.cs`
- Modify: `src/QueryBuilder.Domain/Entities/DataSource.cs`
- Modify: `src/QueryBuilder.Infrastructure/Persistence/Configurations/DataSourceConfiguration.cs`
- Modify: `src/QueryBuilder.Infrastructure/Catalog/SqlServerDataCatalogService.cs`
- Modify: `src/QueryBuilder.Application/Abstractions/IDataCatalogService.cs`
- Modify: `src/QueryBuilder.Application/Dtos/SavedQueryDtos.cs`
- Modify: `src/QueryBuilder.Application/DataSources/GetDataSourcesQuery.cs`
- Modify: `client/src/types/index.ts`
- Create: `src/QueryBuilder.Infrastructure/Persistence/Migrations/<timestamp>_AddCatalogScopeAndAllowedObjects.cs` (generated, then hand-edited)

**Interfaces:**
- Produces: `QueryBuilder.Domain.Enums.CatalogScope { Views = 0, Tables = 1, TablesAndViews = 2 }`;
  `DataSource.CatalogScope` (`CatalogScope`, default `Views`); `DataSource.AllowedObjects`
  (`List<string>`, default `[]`); `IDataCatalogService.GetRawObjectsAsync(Guid dataSourceId,
  CancellationToken ct) : Task<List<SchemaObjectMetadata>>`; `IDataCatalogService
  .InvalidateCatalogCache(Guid dataSourceId) : void`; `AuditAction.DataSourceCatalogPolicyUpdated`.
  Later tasks depend on all of these exact names.

- [ ] **Step 1: Add the `CatalogScope` enum and new `AuditAction` value**

In `src/QueryBuilder.Domain/Enums/Enums.cs`, add the enum right after `DataSourceProvider`:

```csharp
public enum DataSourceProvider
{
    SqlServer = 0,
    PostgreSql = 1,
    MySql = 2,
    Sqlite = 3
}

/// <summary>Which object kinds a data source's catalog surfaces. Combined with
/// <see cref="Entities.DataSource.AllowedObjects"/> (if non-empty) for finer-grained control.</summary>
public enum CatalogScope
{
    Views = 0,
    Tables = 1,
    TablesAndViews = 2
}
```

And add to the end of `AuditAction`:

```csharp
public enum AuditAction
{
    QueryCreated = 0,
    QueryUpdated = 1,
    QueryDeleted = 2,
    QueryRun = 3,
    QueryExported = 4,
    QueryShared = 5,
    QueryUnshared = 6,
    QueryDisabled = 7,
    QueryEnabled = 8,
    DataSourceCatalogPolicyUpdated = 9
}
```

- [ ] **Step 2: Replace `ViewsOnly` with `CatalogScope` + `AllowedObjects` on `DataSource`**

In `src/QueryBuilder.Domain/Entities/DataSource.cs`, replace:

```csharp
    /// <summary>When true, only Views are surfaced in the catalog (recommended — business logic lives in views).</summary>
    public bool ViewsOnly { get; set; } = true;
```

with:

```csharp
    /// <summary>Restricts the catalog by object kind. Combined with <see cref="AllowedObjects"/>
    /// (if non-empty) for finer-grained control.</summary>
    public CatalogScope CatalogScope { get; set; } = CatalogScope.Views;

    /// <summary>Restrict the catalog to these specific "SchemaName.ObjectName" entries only
    /// (empty = every object matching <see cref="CatalogScope"/>/<see cref="AllowedSchemas"/> is
    /// exposed) — same "empty = no restriction" convention as <see cref="AllowedSchemas"/>.</summary>
    public List<string> AllowedObjects { get; set; } = [];
```

Add `using QueryBuilder.Domain.Enums;` at the top of the file if not already present (it already is
— `Provider` uses it).

- [ ] **Step 3: EF conversion for `AllowedObjects`**

In `src/QueryBuilder.Infrastructure/Persistence/Configurations/DataSourceConfiguration.cs`, add
right after the existing `AllowedSchemas` block (same file, `Configure` method):

```csharp
        builder.Property(x => x.AllowedObjects)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Length == 0 ? new List<string>() : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
                v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                v => v.ToList()));
```

- [ ] **Step 4: `IDataCatalogService` — add `GetRawObjectsAsync` and `InvalidateCatalogCache`**

Replace the full contents of `src/QueryBuilder.Application/Abstractions/IDataCatalogService.cs`
with:

```csharp
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Abstractions;

/// <summary>Introspects a registered data source and returns its browsable schema/table/column tree.</summary>
public interface IDataCatalogService
{
    Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="Exceptions.CatalogValidationException"/> if any referenced table/column does not exist.</summary>
    Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken);

    /// <summary>Every table/view the data source's connection can see (system-schema-excluded,
    /// <see cref="Entities.DataSource.AllowedSchemas"/>-scoped), bypassing
    /// <see cref="Entities.DataSource.CatalogScope"/>/<see cref="Entities.DataSource.AllowedObjects"/>
    /// entirely — for the admin picker, which needs to see everything to choose from. No column
    /// metadata is populated (not needed for picking, and this path isn't cached).</summary>
    Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken);

    /// <summary>Evicts the cached catalog for a data source so the next read reflects a recent
    /// change (e.g. an admin catalog-policy update) immediately instead of waiting out the cache TTL.</summary>
    void InvalidateCatalogCache(Guid dataSourceId);
}
```

(Note: `Entities` isn't imported in this file — the `<see cref>` tags above are documentation only
and don't need to resolve; leave them as-is, consistent with how the rest of the codebase writes
doc comments that reference sibling-namespace types without importing them.)

- [ ] **Step 5: `SqlServerDataCatalogService` — shared object query, scope/allowlist filtering, new methods**

In `src/QueryBuilder.Infrastructure/Catalog/SqlServerDataCatalogService.cs`, replace the object-loading
block inside `BuildCatalogAsync` — everything from `var objects = new List<SchemaObjectMetadata>();`
through the closing `}` of the `objectsSql` reader loop (i.e. replace this whole block):

```csharp
        var objects = new List<SchemaObjectMetadata>();

        const string objectsSql = """
            SELECT s.name AS SchemaName, t.name AS ObjectName, 'Table' AS Kind
            FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT s.name AS SchemaName, v.name AS ObjectName, 'View' AS Kind
            FROM sys.views v JOIN sys.schemas s ON v.schema_id = s.schema_id
            WHERE v.is_ms_shipped = 0
            ORDER BY SchemaName, ObjectName
            """;

        await using (var cmd = new SqlCommand(objectsSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var schemaName = reader.GetString(0);
                var objectName = reader.GetString(1);
                var kind = reader.GetString(2) == "View" ? SchemaObjectKind.View : SchemaObjectKind.Table;

                if (SystemSchemas.Contains(schemaName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (dataSource.AllowedSchemas.Count > 0 &&
                    !dataSource.AllowedSchemas.Contains(schemaName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (dataSource.ViewsOnly && kind != SchemaObjectKind.View)
                {
                    continue;
                }

                objects.Add(new SchemaObjectMetadata { SchemaName = schemaName, Name = objectName, Kind = kind });
            }
        }
```

with:

```csharp
        var objects = await QueryObjectsAsync(connection, dataSource, applyCatalogScopeAndAllowlist: true, cancellationToken);
```

Then add these two new public methods to the class (anywhere after `ValidateAsync`, before
`BuildCatalogAsync`) and one new private helper (after `BuildCatalogAsync`, replacing the removed
inline block's logic):

```csharp
    public async Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(dataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), dataSourceId);

        var connectionString = connectionStringResolver.Resolve(dataSource);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await QueryObjectsAsync(connection, dataSource, applyCatalogScopeAndAllowlist: false, cancellationToken);
    }

    public void InvalidateCatalogCache(Guid dataSourceId) => cache.Remove($"catalog:{dataSourceId}");
```

```csharp
    private static async Task<List<SchemaObjectMetadata>> QueryObjectsAsync(
        SqlConnection connection, DataSource dataSource, bool applyCatalogScopeAndAllowlist, CancellationToken cancellationToken)
    {
        var objects = new List<SchemaObjectMetadata>();

        const string objectsSql = """
            SELECT s.name AS SchemaName, t.name AS ObjectName, 'Table' AS Kind
            FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT s.name AS SchemaName, v.name AS ObjectName, 'View' AS Kind
            FROM sys.views v JOIN sys.schemas s ON v.schema_id = s.schema_id
            WHERE v.is_ms_shipped = 0
            ORDER BY SchemaName, ObjectName
            """;

        await using var cmd = new SqlCommand(objectsSql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var schemaName = reader.GetString(0);
            var objectName = reader.GetString(1);
            var kind = reader.GetString(2) == "View" ? SchemaObjectKind.View : SchemaObjectKind.Table;

            if (SystemSchemas.Contains(schemaName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            if (dataSource.AllowedSchemas.Count > 0 &&
                !dataSource.AllowedSchemas.Contains(schemaName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (applyCatalogScopeAndAllowlist)
            {
                if (dataSource.CatalogScope == CatalogScope.Views && kind != SchemaObjectKind.View)
                {
                    continue;
                }
                if (dataSource.CatalogScope == CatalogScope.Tables && kind != SchemaObjectKind.Table)
                {
                    continue;
                }
                if (dataSource.AllowedObjects.Count > 0 &&
                    !dataSource.AllowedObjects.Contains($"{schemaName}.{objectName}", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            objects.Add(new SchemaObjectMetadata { SchemaName = schemaName, Name = objectName, Kind = kind });
        }

        return objects;
    }
```

- [ ] **Step 6: Rename `DataSourceDto.ViewsOnly` → `CatalogScope`**

In `src/QueryBuilder.Application/Dtos/SavedQueryDtos.cs`, replace:

```csharp
public sealed record DataSourceDto(Guid Id, string Name, string? Description, bool ViewsOnly);
```

with:

```csharp
public sealed record DataSourceDto(Guid Id, string Name, string? Description, CatalogScope CatalogScope);
```

In `src/QueryBuilder.Application/DataSources/GetDataSourcesQuery.cs`, replace:

```csharp
        return sources.Select(s => new DataSourceDto(s.Id, s.Name, s.Description, s.ViewsOnly)).ToList();
```

with:

```csharp
        return sources.Select(s => new DataSourceDto(s.Id, s.Name, s.Description, s.CatalogScope)).ToList();
```

- [ ] **Step 7: Frontend type rename**

In `client/src/types/index.ts`, add near the other enum-mirroring types at the top (after
`QueryAccessLevel`):

```ts
export type CatalogScope = 'views' | 'tables' | 'tablesAndViews'
```

Then find the `DataSourceDto` interface and replace:

```ts
export interface DataSourceDto {
  id: string
  name: string
  description?: string | null
  viewsOnly: boolean
}
```

with:

```ts
export interface DataSourceDto {
  id: string
  name: string
  description?: string | null
  catalogScope: CatalogScope
}
```

- [ ] **Step 8: Generate and hand-edit the EF Core migration**

Run from the repo root:

```bash
dotnet tool run dotnet-ef migrations add AddCatalogScopeAndAllowedObjects --project src/QueryBuilder.Infrastructure --startup-project src/QueryBuilder.Api -o Persistence/Migrations
```

This creates `<timestamp>_AddCatalogScopeAndAllowedObjects.cs` and `.Designer.cs`, and updates
`AppDbContextModelSnapshot.cs` automatically — do not hand-edit the `.Designer.cs` or the
snapshot file. Open the new `<timestamp>_AddCatalogScopeAndAllowedObjects.cs` and replace its
`Up`/`Down` method bodies (keep the class name, namespace, and `[Migration("...")]`/
`[DbContext(...)]` attributes exactly as generated) with:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CatalogScope",
                table: "DataSources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE DataSources SET CatalogScope = CASE WHEN ViewsOnly = 1 THEN 0 ELSE 2 END");

            migrationBuilder.DropColumn(
                name: "ViewsOnly",
                table: "DataSources");

            migrationBuilder.AddColumn<string>(
                name: "AllowedObjects",
                table: "DataSources",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedObjects",
                table: "DataSources");

            migrationBuilder.AddColumn<bool>(
                name: "ViewsOnly",
                table: "DataSources",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("UPDATE DataSources SET ViewsOnly = CASE WHEN CatalogScope = 0 THEN 1 ELSE 0 END");

            migrationBuilder.DropColumn(
                name: "CatalogScope",
                table: "DataSources");
        }
```

- [ ] **Step 9: Build and fix any fallout**

```bash
dotnet build src/QueryBuilder.Api/QueryBuilder.Api.csproj -c Debug
```

Expected: builds clean. If anything else in the solution referenced `DataSource.ViewsOnly` or
`DataSourceDto.ViewsOnly`, the compiler will point at it — fix by using `CatalogScope` instead
(there should be no other references; `GetDataSourcesQuery.cs` was the only production usage
found during design).

```bash
cd client && npx tsc -b
```

Expected: builds clean (no other file in `client/src` reads `viewsOnly` — confirmed during design
via a repo-wide search).

- [ ] **Step 10: Apply the migration locally and verify the scope/allowlist filter**

Stop stray `dotnet` processes, then start the sample app in the background:

```bash
dotnet run --project src/QueryBuilder.Api --no-launch-profile --urls http://localhost:5080
```

Wait for it to come up (poll `http://localhost:5080/querybuilder/_setup` until it responds), which
also applies the new migration (default `ApplyMigrations = true`). Then verify with a script (use
`mcp__plugin_context-mode_context-mode__ctx_execute`, `language: "javascript"`):

```javascript
const base = 'http://localhost:5080/querybuilder/api';
const j = async (url, opts = {}) => {
  const res = await fetch(url, { ...opts, headers: { 'Content-Type': 'application/json', ...(opts.headers || {}) } });
  const text = await res.text();
  let body; try { body = JSON.parse(text); } catch { body = text; }
  return { status: res.status, body };
};

(async () => {
  const ds = await j(`${base}/data-sources`);
  const dataSourceId = ds.body[0].id;
  console.log('catalogScope on business DTO:', ds.body[0].catalogScope);

  const before = await j(`${base}/data-sources/${dataSourceId}/catalog`);
  const beforeCount = before.body.schemas.flatMap(s => s.objects).length;
  console.log('objects before any restriction:', beforeCount);
})().catch(e => console.error('ERROR', e));
```

Expected: `catalogScope` prints `"views"` (the default, matches the pre-existing seeded
`ViewsOnly = true` backfilled to `Views`), and `beforeCount` matches whatever the demo schema
currently exposes (established in the prior session: 52, all views). This confirms the migration
applied and the rename round-trips correctly. There is no admin endpoint yet to change the policy
via HTTP — that lands in Tasks 3–4; do not hand-edit the database directly here.

Stop the background app afterward (see Global Constraints).

- [ ] **Step 11: Commit**

```bash
git add src/QueryBuilder.Domain/Enums/Enums.cs src/QueryBuilder.Domain/Entities/DataSource.cs \
  src/QueryBuilder.Infrastructure/Persistence/Configurations/DataSourceConfiguration.cs \
  src/QueryBuilder.Infrastructure/Catalog/SqlServerDataCatalogService.cs \
  src/QueryBuilder.Application/Abstractions/IDataCatalogService.cs \
  src/QueryBuilder.Application/Dtos/SavedQueryDtos.cs \
  src/QueryBuilder.Application/DataSources/GetDataSourcesQuery.cs \
  client/src/types/index.ts \
  src/QueryBuilder.Infrastructure/Persistence/Migrations/
git commit -m "Replace DataSource.ViewsOnly with CatalogScope + AllowedObjects

Data-model groundwork for the admin catalog-allowlist feature: a 3-way
object-kind scope (views/tables/both) plus a specific table/view
allowlist, using the same comma-joined storage convention as the
existing AllowedSchemas. Migration backfills existing ViewsOnly values.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Admin authorization plumbing + `/api/admin/access`

**Files:**
- Modify: `src/QueryBuilder.Editor/QueryBuilderEditorOptions.cs`
- Modify: `src/QueryBuilder.Editor/EndpointRouteBuilderExtensions.cs`
- Create: `src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs`

**Interfaces:**
- Consumes: `QueryBuilderAuthorizationOptions` (existing type, `Mode`/`RoleNames`/`PolicyName`),
  `ApplyAuthorization(RouteGroupBuilder, QueryBuilderAuthorizationOptions)` (existing private
  method in `EndpointRouteBuilderExtensions`).
- Produces: `QueryBuilderEditorOptions.AdminAuthorization` (`QueryBuilderAuthorizationOptions`,
  defaults `Anonymous`); `AdminEndpoints.MapAdminEndpoints(this IEndpointRouteBuilder) :
  RouteGroupBuilder` — later tasks add routes to the group this returns/creates.

- [ ] **Step 1: Add `AdminAuthorization` to the options**

In `src/QueryBuilder.Editor/QueryBuilderEditorOptions.cs`, add after the existing `Authorization`
property:

```csharp
    /// <summary>Controls who can reach QueryBuilder's admin routes (under <c>/api/admin</c>) —
    /// catalog policy management today, more admin features later. Applied IN ADDITION to
    /// <see cref="Authorization"/>: a request must satisfy both. Defaults to
    /// <see cref="QueryBuilderAuthorizationMode.Anonymous"/>, like <see cref="Authorization"/> —
    /// set this before going to production, since the admin area lets a caller see a data
    /// source's full unfiltered schema and change what business users can query.</summary>
    public QueryBuilderAuthorizationOptions AdminAuthorization { get; } = new();
```

- [ ] **Step 2: Create `AdminEndpoints.cs` with just the access probe**

Create `src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs`:

```csharp
namespace QueryBuilder.Editor.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin");

        // Trivial 204 if the caller passes AdminAuthorization — lets the SPA decide whether to
        // show the Admin nav item without needing a real admin payload.
        group.MapGet("/access", () => Results.NoContent());

        return group;
    }
}
```

- [ ] **Step 3: Wire the admin group with its own authorization into `MapQueryBuilderEditor`**

In `src/QueryBuilder.Editor/EndpointRouteBuilderExtensions.cs`, add
`using QueryBuilder.Editor.Endpoints;` if not already present (it is — `AdminEndpoints` lives in
that namespace like `SavedQueryEndpoints`), then replace:

```csharp
        group.MapDataSourceEndpoints();
        group.MapSavedQueryEndpoints();
        group.MapAuditEndpoints();

        return endpoints;
```

with:

```csharp
        group.MapDataSourceEndpoints();
        group.MapSavedQueryEndpoints();
        group.MapAuditEndpoints();

        var adminGroup = group.MapAdminEndpoints();
        ApplyAuthorization(adminGroup, options.AdminAuthorization);

        return endpoints;
```

- [ ] **Step 4: Build**

```bash
dotnet build src/QueryBuilder.Api/QueryBuilder.Api.csproj -c Debug
```

Expected: builds clean.

- [ ] **Step 5: Verify the default (Anonymous) admin gate lets requests through**

Stop stray `dotnet` processes, start the sample app in the background as in Task 1 Step 10, wait
for it to come up, then:

```javascript
const res = await fetch('http://localhost:5080/querybuilder/api/admin/access');
console.log('access status (default Anonymous):', res.status);
```

Expected: `204`.

- [ ] **Step 6: Verify `Mode = Role` with no `RoleNames` throws the same guard the main
      `Authorization` already has**

Temporarily edit `src/QueryBuilder.Api/Program.cs`. Add `using QueryBuilder.Editor.Authorization;`
to the top import block (it is not there today — only referenced inside a comment — so this is a
real addition, not a no-op):

```csharp
using QueryBuilder.Api.Seed;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Authorization;
using Serilog;
```

Then add, inside the `AddQueryBuilderEditor` options callback (after the `ConnectionString` line):

```csharp
    options.AdminAuthorization.Mode = QueryBuilderAuthorizationMode.Role;
```

Restart the app (stop the background process, rerun the `dotnet run` command from Step 5).
Expected: the app fails to start with
`InvalidOperationException: QueryBuilderAuthorizationOptions.RoleNames must contain at least one
role when Mode is Role.` — confirming `AdminAuthorization` reuses the exact same guard as
`Authorization`. Revert the `Program.cs` edit (remove both the `using QueryBuilder.Editor
.Authorization;` line and the `options.AdminAuthorization.Mode = ...` line just added — this is a
real diff the user never asked for, matching `CLAUDE.md`'s guidance on temporary test edits) and
restart the app once more to confirm it comes back up cleanly. Stop the background app afterward.

- [ ] **Step 7: Commit**

```bash
git add src/QueryBuilder.Editor/QueryBuilderEditorOptions.cs \
  src/QueryBuilder.Editor/EndpointRouteBuilderExtensions.cs \
  src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs
git commit -m "Add AdminAuthorization option and /api/admin route group

Mirrors the existing Authorization option's shape (Mode/RoleNames/
PolicyName), defaulting to Anonymous like the rest of the package.
Admin routes compose it with the app-wide Authorization via nested
route-group authorization (AND semantics) - no new plumbing needed.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Admin data-source list + detail (with raw catalog objects)

**Files:**
- Modify: `src/QueryBuilder.Application/Abstractions/Repositories.cs`
- Modify: `src/QueryBuilder.Infrastructure/Repositories/DataSourceRepository.cs`
- Create: `src/QueryBuilder.Application/Dtos/AdminDtos.cs`
- Create: `src/QueryBuilder.Application/Queries.Admin/GetAdminDataSourcesQuery.cs`
- Create: `src/QueryBuilder.Application/Queries.Admin/GetAdminDataSourceDetailQuery.cs`
- Modify: `src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs`

**Interfaces:**
- Consumes: `IDataCatalogService.GetRawObjectsAsync` (Task 1); `AdminEndpoints.MapAdminEndpoints`
  (Task 2, the `group` this task adds routes to).
- Produces: `AdminDataSourceSummaryDto(Guid Id, string Name, DataSourceProvider Provider, bool
  IsActive)`; `AdminDataSourceDetailDto(Guid Id, string Name, CatalogScope CatalogScope,
  List<string> AllowedObjects, List<SchemaObjectMetadata> Objects)`; `IDataSourceRepository
  .GetAllAsync(CancellationToken) : Task<List<DataSource>>`. Task 4 depends on
  `GetAdminDataSourceDetailQuery`'s validation shape indirectly (reuses `GetRawObjectsAsync`
  directly, not this query).

- [ ] **Step 1: Extend `IDataSourceRepository`**

In `src/QueryBuilder.Application/Abstractions/Repositories.cs`, replace:

```csharp
public interface IDataSourceRepository
{
    Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken);
}
```

with:

```csharp
public interface IDataSourceRepository
{
    Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken);
    Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken);
    void Update(DataSource dataSource);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implement the new members**

Replace the full contents of `src/QueryBuilder.Infrastructure/Repositories/DataSourceRepository.cs`
with:

```csharp
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Repositories;

public sealed class DataSourceRepository(AppDbContext db) : IDataSourceRepository
{
    public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.DataSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken) =>
        db.DataSources.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken) =>
        db.DataSources.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public void Update(DataSource dataSource) => db.DataSources.Update(dataSource);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 3: Admin DTOs**

Create `src/QueryBuilder.Application/Dtos/AdminDtos.cs`:

```csharp
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Dtos;

public sealed record AdminDataSourceSummaryDto(Guid Id, string Name, DataSourceProvider Provider, bool IsActive);

public sealed record AdminDataSourceDetailDto(
    Guid Id,
    string Name,
    CatalogScope CatalogScope,
    List<string> AllowedObjects,
    List<SchemaObjectMetadata> Objects);

public sealed record UpdateCatalogPolicyRequest(CatalogScope CatalogScope, List<string> AllowedObjects);
```

- [ ] **Step 4: List query**

Create `src/QueryBuilder.Application/Queries.Admin/GetAdminDataSourcesQuery.cs`:

```csharp
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dtos;

namespace QueryBuilder.Application.Queries.Admin;

public sealed record GetAdminDataSourcesQuery : IRequest<List<AdminDataSourceSummaryDto>>;

public sealed class GetAdminDataSourcesQueryHandler(IDataSourceRepository repository)
    : IRequestHandler<GetAdminDataSourcesQuery, List<AdminDataSourceSummaryDto>>
{
    public async Task<List<AdminDataSourceSummaryDto>> Handle(GetAdminDataSourcesQuery request, CancellationToken cancellationToken)
    {
        var sources = await repository.GetAllAsync(cancellationToken);
        return sources.Select(s => new AdminDataSourceSummaryDto(s.Id, s.Name, s.Provider, s.IsActive)).ToList();
    }
}
```

- [ ] **Step 5: Detail query**

Create `src/QueryBuilder.Application/Queries.Admin/GetAdminDataSourceDetailQuery.cs`:

```csharp
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Queries.Admin;

public sealed record GetAdminDataSourceDetailQuery(Guid Id) : IRequest<AdminDataSourceDetailDto>;

public sealed class GetAdminDataSourceDetailQueryHandler(
    IDataSourceRepository repository, IDataCatalogService catalogService)
    : IRequestHandler<GetAdminDataSourceDetailQuery, AdminDataSourceDetailDto>
{
    public async Task<AdminDataSourceDetailDto> Handle(GetAdminDataSourceDetailQuery request, CancellationToken cancellationToken)
    {
        var dataSource = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.Id);

        var objects = await catalogService.GetRawObjectsAsync(request.Id, cancellationToken);

        return new AdminDataSourceDetailDto(
            dataSource.Id,
            dataSource.Name,
            dataSource.CatalogScope,
            dataSource.AllowedObjects,
            objects);
    }
}
```

- [ ] **Step 6: Wire the two new endpoints**

In `src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs`, add
`using QueryBuilder.Application.Common;`, `using QueryBuilder.Application.Queries.Admin;` at the
top, and replace:

```csharp
        // Trivial 204 if the caller passes AdminAuthorization — lets the SPA decide whether to
        // show the Admin nav item without needing a real admin payload.
        group.MapGet("/access", () => Results.NoContent());

        return group;
```

with:

```csharp
        // Trivial 204 if the caller passes AdminAuthorization — lets the SPA decide whether to
        // show the Admin nav item without needing a real admin payload.
        group.MapGet("/access", () => Results.NoContent());

        group.MapGet("/data-sources", async (ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAdminDataSourcesQuery(), ct), QueryBuilderJson.Options));

        group.MapGet("/data-sources/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAdminDataSourceDetailQuery(id), ct), QueryBuilderJson.Options));

        return group;
```

- [ ] **Step 7: Build**

```bash
dotnet build src/QueryBuilder.Api/QueryBuilder.Api.csproj -c Debug
```

Expected: builds clean.

- [ ] **Step 8: Verify both endpoints**

Start the sample app in the background as before, wait for it to come up, then:

```javascript
const base = 'http://localhost:5080/querybuilder/api';
const j = async (url) => { const res = await fetch(url); return { status: res.status, body: await res.json() }; };

(async () => {
  const list = await j(`${base}/admin/data-sources`);
  console.log('admin list status:', list.status, 'count:', list.body.length, JSON.stringify(list.body[0]));

  const id = list.body[0].id;
  const detail = await j(`${base}/admin/data-sources/${id}`);
  console.log('admin detail status:', detail.status);
  console.log('catalogScope:', detail.body.catalogScope, 'allowedObjects:', JSON.stringify(detail.body.allowedObjects));
  console.log('raw object count:', detail.body.objects.length, 'sample:', JSON.stringify(detail.body.objects[0]));
})().catch(e => console.error('ERROR', e));
```

Expected: list returns the one seeded "Sales Sample" data source with `provider: "sqlServer"`,
`isActive: true`; detail returns `catalogScope: "views"`, `allowedObjects: []`, and
`objects.length` equal to the full raw object count from Task 1 (52) — confirming
`GetRawObjectsAsync` bypasses the `views`-only restriction that the business-facing `/catalog`
endpoint would apply. Stop the background app afterward.

- [ ] **Step 9: Commit**

```bash
git add src/QueryBuilder.Application/Abstractions/Repositories.cs \
  src/QueryBuilder.Infrastructure/Repositories/DataSourceRepository.cs \
  src/QueryBuilder.Application/Dtos/AdminDtos.cs \
  src/QueryBuilder.Application/Queries.Admin/ \
  src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs
git commit -m "Add admin data-source list/detail endpoints

GET /api/admin/data-sources and .../{id} expose every data source
(including inactive ones, unlike the business-facing list) and, for
detail, the full unfiltered catalog object list via the new
GetRawObjectsAsync - what the admin picks the allowlist from.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Update catalog policy (validation, cache eviction, audit)

**Files:**
- Create: `src/QueryBuilder.Application/Commands.Admin/UpdateCatalogPolicyCommand.cs`
- Modify: `src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs`

**Interfaces:**
- Consumes: `IDataCatalogService.GetRawObjectsAsync`/`InvalidateCatalogCache` (Task 1);
  `IDataSourceRepository.Update`/`SaveChangesAsync` (Task 3); `UpdateCatalogPolicyRequest` (Task 3).
- Produces: `UpdateCatalogPolicyCommand(Guid DataSourceId, CatalogScope CatalogScope, List<string>
  AllowedObjects) : IRequest<Unit>` — nothing downstream in this plan depends on it beyond the
  endpoint wired in this same task.

- [ ] **Step 1: The command**

Create `src/QueryBuilder.Application/Commands.Admin/UpdateCatalogPolicyCommand.cs`:

```csharp
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.Admin;

public sealed record UpdateCatalogPolicyCommand(Guid DataSourceId, CatalogScope CatalogScope, List<string> AllowedObjects)
    : IRequest<Unit>;

public sealed class UpdateCatalogPolicyCommandHandler(
    IDataSourceRepository repository, IDataCatalogService catalogService, IAuditLogger auditLogger)
    : IRequestHandler<UpdateCatalogPolicyCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCatalogPolicyCommand request, CancellationToken cancellationToken)
    {
        var dataSource = await repository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        var rawObjects = await catalogService.GetRawObjectsAsync(request.DataSourceId, cancellationToken);
        var validKeys = rawObjects.Select(o => $"{o.SchemaName}.{o.Name}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = request.AllowedObjects.Where(key => !validKeys.Contains(key)).ToList();
        if (unknown.Count > 0)
        {
            throw new CatalogValidationException($"Unknown table/view reference(s): {string.Join(", ", unknown)}.");
        }

        dataSource.CatalogScope = request.CatalogScope;
        dataSource.AllowedObjects = request.AllowedObjects;
        repository.Update(dataSource);
        await repository.SaveChangesAsync(cancellationToken);

        catalogService.InvalidateCatalogCache(request.DataSourceId);

        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.DataSourceCatalogPolicyUpdated,
                nameof(DataSource),
                dataSource.Id,
                dataSource.Name,
                dataSource.Id,
                $"Updated catalog policy for '{dataSource.Name}' (scope: {request.CatalogScope}, {request.AllowedObjects.Count} allowed object(s) explicitly listed)"),
            cancellationToken);

        return Unit.Value;
    }
}
```

- [ ] **Step 2: Wire the PUT endpoint**

In `src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs`, add
`using QueryBuilder.Application.Commands.Admin;` and `using QueryBuilder.Application.Dtos;` at the
top (the latter for `UpdateCatalogPolicyRequest`), and replace:

```csharp
        group.MapGet("/data-sources/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAdminDataSourceDetailQuery(id), ct), QueryBuilderJson.Options));

        return group;
```

with:

```csharp
        group.MapGet("/data-sources/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetAdminDataSourceDetailQuery(id), ct), QueryBuilderJson.Options));

        group.MapPut("/data-sources/{id:guid}/catalog-policy", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var request = await http.Request.ReadFromJsonAsync<UpdateCatalogPolicyRequest>(QueryBuilderJson.Options, ct)
                ?? throw new BadHttpRequestException("Request body is required.");
            await sender.Send(new UpdateCatalogPolicyCommand(id, request.CatalogScope, request.AllowedObjects), ct);
            return Results.NoContent();
        });

        return group;
```

- [ ] **Step 3: Build**

```bash
dotnet build src/QueryBuilder.Api/QueryBuilder.Api.csproj -c Debug
```

Expected: builds clean.

- [ ] **Step 4: Verify validation, save, cache eviction, and audit logging end-to-end**

Start the sample app in the background, wait for it to come up, then:

```javascript
const base = 'http://localhost:5080/querybuilder/api';
const j = async (url, opts = {}) => {
  const res = await fetch(url, { ...opts, headers: { 'Content-Type': 'application/json', ...(opts.headers || {}) } });
  const text = await res.text();
  let body; try { body = JSON.parse(text); } catch { body = text; }
  return { status: res.status, body };
};

(async () => {
  const list = await j(`${base}/admin/data-sources`);
  const id = list.body[0].id;

  const invalid = await j(`${base}/admin/data-sources/${id}/catalog-policy`, {
    method: 'PUT', body: JSON.stringify({ catalogScope: 'tablesAndViews', allowedObjects: ['sales.not_a_real_object'] }),
  });
  console.log('invalid object PUT status:', invalid.status, JSON.stringify(invalid.body));

  const detail = await j(`${base}/admin/data-sources/${id}`);
  const oneObject = `${detail.body.objects[0].schemaName}.${detail.body.objects[0].name}`;

  const valid = await j(`${base}/admin/data-sources/${id}/catalog-policy`, {
    method: 'PUT', body: JSON.stringify({ catalogScope: 'tablesAndViews', allowedObjects: [oneObject] }),
  });
  console.log('valid PUT status:', valid.status);

  const catalog = await j(`${base}/data-sources/${id}/catalog`);
  const objectCount = catalog.body.schemas.flatMap(s => s.objects).length;
  console.log('business catalog object count after restricting to 1 (no cache wait):', objectCount, 'expected: 1');

  const audit = await j(`${base}/audit?entityType=DataSource&entityId=${id}`);
  console.log('audit actions for this data source:', JSON.stringify(audit.body.map(a => a.action)));

  // Reset to unrestricted, per the Global Constraints in this plan.
  const reset = await j(`${base}/admin/data-sources/${id}/catalog-policy`, {
    method: 'PUT', body: JSON.stringify({ catalogScope: 'tablesAndViews', allowedObjects: [] }),
  });
  console.log('reset PUT status:', reset.status);
  const afterReset = await j(`${base}/data-sources/${id}/catalog`);
  console.log('object count after reset:', afterReset.body.schemas.flatMap(s => s.objects).length);
})().catch(e => console.error('ERROR', e));
```

Expected: the invalid PUT returns `400` with a `CatalogValidationException`-shaped `ProblemDetails`
body naming `sales.not_a_real_object`; the valid PUT returns `204`; the business catalog
immediately reflects exactly 1 object (proving the cache was evicted, not just expired); the audit
list includes `"dataSourceCatalogPolicyUpdated"`; and after the reset PUT the object count returns
to the full 52. Stop the background app afterward.

- [ ] **Step 5: Commit**

```bash
git add src/QueryBuilder.Application/Commands.Admin/ src/QueryBuilder.Editor/Endpoints/AdminEndpoints.cs
git commit -m "Add PUT /api/admin/data-sources/{id}/catalog-policy

Validates every requested allowed-object key exists in the data
source's raw catalog, saves, evicts the cached catalog entry so
business users see the change immediately, and logs
DataSourceCatalogPolicyUpdated to the audit trail.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Frontend — types, API client, admin nav gating, data-source list page

**Files:**
- Modify: `client/src/types/index.ts`
- Modify: `client/src/lib/api.ts`
- Create: `client/src/hooks/useAdmin.ts`
- Modify: `client/src/components/layout/AppShell.tsx`
- Create: `client/src/pages/AdminDataSourcesPage.tsx`
- Modify: `client/src/App.tsx`

**Interfaces:**
- Consumes: `AdminDataSourceSummaryDto`/`AdminDataSourceDetailDto`/`UpdateCatalogPolicyRequest`
  (Task 3/4's C# DTOs, mirrored here); `GET /api/admin/access`, `GET /api/admin/data-sources`,
  `GET /api/admin/data-sources/{id}` (Tasks 2–3); `CatalogScope` (Task 1).
- Produces: `useAdminAccess()`, `useAdminDataSources()`, `useAdminDataSourceDetail(id)`,
  `useUpdateCatalogPolicy(id)` hooks and `adminDataSourcesKey` query key — Task 6's
  `AdminDataSourcePolicyPage` consumes `useAdminDataSourceDetail`/`useUpdateCatalogPolicy` by
  these exact names.

- [ ] **Step 1: Types**

In `client/src/types/index.ts`, add `DataSourceProvider` near the other mirrored enum types (after
`QueryAccessLevel`):

```ts
export type DataSourceProvider = 'sqlServer' | 'postgreSql' | 'mySql' | 'sqlite'
```

Then add, near the end of the file (after `AuditLogEntryDto`):

```ts
// ---- Admin ----

export interface AdminDataSourceSummaryDto {
  id: string
  name: string
  provider: DataSourceProvider
  isActive: boolean
}

export interface AdminDataSourceDetailDto {
  id: string
  name: string
  catalogScope: CatalogScope
  allowedObjects: string[]
  objects: SchemaObjectMetadata[]
}

export interface UpdateCatalogPolicyRequest {
  catalogScope: CatalogScope
  allowedObjects: string[]
}
```

- [ ] **Step 2: API client**

In `client/src/lib/api.ts`, add to the type import list:
`AdminDataSourceSummaryDto, AdminDataSourceDetailDto, UpdateCatalogPolicyRequest,` and add this new
export at the end of the file (after `auditApi`):

```ts
export const adminApi = {
  access: async (): Promise<void> => {
    await apiClient.get('/admin/access')
  },
  listDataSources: async (): Promise<AdminDataSourceSummaryDto[]> => (await apiClient.get('/admin/data-sources')).data,
  getDataSource: async (id: string): Promise<AdminDataSourceDetailDto> => (await apiClient.get(`/admin/data-sources/${id}`)).data,
  updateCatalogPolicy: async (id: string, request: UpdateCatalogPolicyRequest): Promise<void> => {
    await apiClient.put(`/admin/data-sources/${id}/catalog-policy`, request)
  },
}
```

- [ ] **Step 3: Hooks**

Create `client/src/hooks/useAdmin.ts`:

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { adminApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import type { UpdateCatalogPolicyRequest } from '@/types'

export const adminDataSourcesKey = ['admin-data-sources'] as const
const adminDataSourceDetailKey = (id: string) => ['admin-data-source', id] as const

export function useAdminAccess() {
  return useQuery({
    queryKey: ['admin-access'],
    queryFn: adminApi.access,
    retry: false,
  })
}

export function useAdminDataSources() {
  return useQuery({ queryKey: adminDataSourcesKey, queryFn: adminApi.listDataSources })
}

export function useAdminDataSourceDetail(id: string | undefined) {
  return useQuery({
    queryKey: adminDataSourceDetailKey(id ?? ''),
    queryFn: () => adminApi.getDataSource(id!),
    enabled: !!id,
  })
}

export function useUpdateCatalogPolicy(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateCatalogPolicyRequest) => adminApi.updateCatalogPolicy(id, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminDataSourceDetailKey(id) })
      toast.success('Catalog policy updated.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not update the catalog policy.'),
  })
}
```

- [ ] **Step 4: Role-gated nav item**

In `client/src/components/layout/AppShell.tsx`, replace the top import block:

```tsx
import { ChevronsLeft, ChevronsRight, LayoutGrid, Moon, Rows3, Squircle, Sun } from 'lucide-react'
import { useTheme } from 'next-themes'
import type { ReactNode } from 'react'
import { NavLink } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import { useDensityStore } from '@/state/density-store'
import { useSidebarStore } from '@/state/sidebar-store'
```

with:

```tsx
import { ChevronsLeft, ChevronsRight, LayoutGrid, Moon, Rows3, Settings2, Squircle, Sun } from 'lucide-react'
import { useTheme } from 'next-themes'
import type { ReactNode } from 'react'
import { NavLink } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { useAdminAccess } from '@/hooks/useAdmin'
import { cn } from '@/lib/utils'
import { useDensityStore } from '@/state/density-store'
import { useSidebarStore } from '@/state/sidebar-store'
```

Then replace:

```tsx
export function AppShell({ children }: { children: ReactNode }) {
  const { collapsed, toggle } = useSidebarStore()
```

with:

```tsx
export function AppShell({ children }: { children: ReactNode }) {
  const { collapsed, toggle } = useSidebarStore()
  const adminAccess = useAdminAccess()
```

Then replace:

```tsx
        <nav className={cn('flex flex-1 flex-col gap-1', collapsed ? 'p-2' : 'p-3')}>
          <NavItem to="/" label="Queries" icon={<LayoutGrid className="size-4 shrink-0" />} collapsed={collapsed} />
        </nav>
```

with:

```tsx
        <nav className={cn('flex flex-1 flex-col gap-1', collapsed ? 'p-2' : 'p-3')}>
          <NavItem to="/" label="Queries" icon={<LayoutGrid className="size-4 shrink-0" />} collapsed={collapsed} />
          {adminAccess.isSuccess && (
            <NavItem to="/admin" label="Admin" icon={<Settings2 className="size-4 shrink-0" />} collapsed={collapsed} />
          )}
        </nav>
```

- [ ] **Step 5: Data-source list page**

Create `client/src/pages/AdminDataSourcesPage.tsx`:

```tsx
import { Loader2 } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAdminDataSources } from '@/hooks/useAdmin'

export function AdminDataSourcesPage() {
  const { data, isLoading } = useAdminDataSources()
  const navigate = useNavigate()

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading data sources…
      </div>
    )
  }

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-(--space-section) p-(--space-section)">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Admin — Data Sources</h1>
        <p className="text-sm text-muted-foreground">Control which tables and views each data source exposes to the catalog.</p>
      </div>

      <div className="overflow-hidden rounded-lg border border-border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Provider</TableHead>
              <TableHead>Status</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {(data ?? []).map((ds) => (
              <TableRow key={ds.id} className="cursor-pointer" onClick={() => navigate(`/admin/data-sources/${ds.id}`)}>
                <TableCell className="font-medium">{ds.name}</TableCell>
                <TableCell className="text-xs text-muted-foreground">{ds.provider}</TableCell>
                <TableCell>
                  <Badge variant={ds.isActive ? 'secondary' : 'outline'} className="text-xs font-normal">
                    {ds.isActive ? 'Active' : 'Inactive'}
                  </Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  )
}
```

- [ ] **Step 6: Route**

In `client/src/App.tsx`, add `import { AdminDataSourcesPage } from '@/pages/AdminDataSourcesPage'`
and add a route inside `<Routes>`:

```tsx
        <Route path="/admin" element={<AdminDataSourcesPage />} />
```

(Placed anywhere in the `<Routes>` block, e.g. right after the `"/"` route.)

- [ ] **Step 7: Build**

```bash
cd client && npm run build
```

Expected: builds clean.

- [ ] **Step 8: Copy the fresh bundle and verify in the browser**

```bash
rm -rf src/QueryBuilder.Editor/wwwroot/assets
cp -r client/dist/assets src/QueryBuilder.Editor/wwwroot/assets
cp client/dist/index.html src/QueryBuilder.Editor/wwwroot/index.html
```

Stop stray `dotnet` processes, start the sample app in the background, wait for it to come up.
Using the Playwright MCP tools (per `CLAUDE.md` — `localhost` is unreliable in Claude-in-Chrome,
use `mcp__plugin_playwright_playwright__*` instead): navigate to
`http://localhost:5080/querybuilder`, take a snapshot, confirm an "Admin" nav item is present
(default `AdminAuthorization.Mode = Anonymous` means it's visible without any special setup),
click it, confirm it navigates to `/admin` and lists "Sales Sample". Stop the background app
afterward.

- [ ] **Step 9: Commit**

```bash
git add client/src/types/index.ts client/src/lib/api.ts client/src/hooks/useAdmin.ts \
  client/src/components/layout/AppShell.tsx client/src/pages/AdminDataSourcesPage.tsx \
  client/src/App.tsx src/QueryBuilder.Editor/wwwroot/
git commit -m "Add admin data-source list page and role-gated nav item

Admin nav item only renders once GET /api/admin/access succeeds, so
non-admins never see it. Data-source list is the entry point into the
per-source catalog policy editor (next task).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Frontend — catalog policy editor page

**Files:**
- Create: `client/src/pages/AdminDataSourcePolicyPage.tsx`
- Modify: `client/src/App.tsx`

**Interfaces:**
- Consumes: `useAdminDataSourceDetail`, `useUpdateCatalogPolicy` (Task 5); `CatalogScope`,
  `AdminDataSourceDetailDto`, `SchemaObjectMetadata` (Tasks 1/5).

- [ ] **Step 1: The page**

Create `client/src/pages/AdminDataSourcePolicyPage.tsx`:

```tsx
import { ArrowLeft, Loader2, Save, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAdminDataSourceDetail, useUpdateCatalogPolicy } from '@/hooks/useAdmin'
import type { CatalogScope } from '@/types'

export function AdminDataSourcePolicyPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data, isLoading } = useAdminDataSourceDetail(id)
  const updatePolicy = useUpdateCatalogPolicy(id!)

  const [scope, setScope] = useState<CatalogScope | null>(null)
  const [restrict, setRestrict] = useState<boolean | null>(null)
  const [selected, setSelected] = useState<Set<string> | null>(null)
  const [search, setSearch] = useState('')

  const effectiveScope = scope ?? data?.catalogScope ?? 'tablesAndViews'
  const effectiveRestrict = restrict ?? (data ? data.allowedObjects.length > 0 : false)
  const effectiveSelected = selected ?? new Set(data?.allowedObjects ?? [])

  const visibleObjects = useMemo(() => {
    if (!data) return []
    const term = search.trim().toLowerCase()
    return data.objects.filter((o) => {
      if (effectiveScope === 'views' && o.kind !== 'view') return false
      if (effectiveScope === 'tables' && o.kind !== 'table') return false
      if (term && !`${o.schemaName}.${o.name}`.toLowerCase().includes(term)) return false
      return true
    })
  }, [data, effectiveScope, search])

  if (isLoading || !data) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
      </div>
    )
  }

  function toggleObject(key: string, checked: boolean) {
    const next = new Set(effectiveSelected)
    if (checked) next.add(key)
    else next.delete(key)
    setSelected(next)
  }

  function handleSave() {
    updatePolicy.mutate({
      catalogScope: effectiveScope,
      allowedObjects: effectiveRestrict ? Array.from(effectiveSelected) : [],
    })
  }

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-(--space-section) p-(--space-section)">
      <div className="flex items-center gap-2">
        <Button variant="ghost" size="icon" className="size-8" onClick={() => navigate('/admin')}>
          <ArrowLeft className="size-4" />
        </Button>
        <div>
          <h1 className="text-xl font-semibold tracking-tight">{data.name}</h1>
          <p className="text-sm text-muted-foreground">Control which tables and views this data source exposes to the catalog.</p>
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold">Catalog scope</h2>
        <Tabs value={effectiveScope} onValueChange={(v) => setScope(v as CatalogScope)}>
          <TabsList>
            <TabsTrigger value="views">Views only</TabsTrigger>
            <TabsTrigger value="tables">Tables only</TabsTrigger>
            <TabsTrigger value="tablesAndViews">Tables + Views</TabsTrigger>
          </TabsList>
        </Tabs>
      </div>

      <div className="flex items-center gap-3">
        <Switch checked={effectiveRestrict} onCheckedChange={(checked) => setRestrict(checked)} />
        <div>
          <p className="text-sm font-medium">Restrict to selected objects</p>
          <p className="text-xs text-muted-foreground">
            Off: every table/view matching the scope above is exposed. On: only the ones checked below.
          </p>
        </div>
      </div>

      {effectiveRestrict && (
        <div className="flex flex-col gap-2">
          <div className="relative w-64">
            <Search className="absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground" />
            <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search tables, views…" className="h-8 pl-8 text-xs" />
          </div>
          <div className="max-h-96 overflow-auto rounded-lg border border-border">
            {visibleObjects.map((o) => {
              const key = `${o.schemaName}.${o.name}`
              return (
                <label key={key} className="flex items-center gap-2 border-b border-border px-3 py-2 text-sm last:border-b-0 hover:bg-muted/50">
                  <Checkbox checked={effectiveSelected.has(key)} onCheckedChange={(checked) => toggleObject(key, checked === true)} />
                  <span className="flex-1 truncate">{key}</span>
                  <Badge variant="secondary" className="text-[10px] font-normal">
                    {o.kind}
                  </Badge>
                </label>
              )
            })}
            {visibleObjects.length === 0 && (
              <div className="px-3 py-6 text-center text-sm text-muted-foreground">No tables/views match the current scope.</div>
            )}
          </div>
        </div>
      )}

      <div>
        <Button onClick={handleSave} disabled={updatePolicy.isPending}>
          {updatePolicy.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Save className="size-3.5" />}
          Save
        </Button>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Route**

In `client/src/App.tsx`, add
`import { AdminDataSourcePolicyPage } from '@/pages/AdminDataSourcePolicyPage'` and add:

```tsx
        <Route path="/admin/data-sources/:id" element={<AdminDataSourcePolicyPage />} />
```

- [ ] **Step 3: Build**

```bash
cd client && npm run build
```

Expected: builds clean.

- [ ] **Step 4: Copy the bundle and verify end-to-end in the browser**

```bash
rm -rf src/QueryBuilder.Editor/wwwroot/assets
cp -r client/dist/assets src/QueryBuilder.Editor/wwwroot/assets
cp client/dist/index.html src/QueryBuilder.Editor/wwwroot/index.html
```

Stop stray `dotnet` processes, start the sample app in the background, wait for it to come up.
Using Playwright MCP tools: navigate to `http://localhost:5080/querybuilder/admin`, click into
"Sales Sample", confirm the scope tabs and restrict switch render. Switch scope to "Tables only",
confirm the (empty, since the demo schema has no real tables) object list shows "No tables/views
match the current scope" when the restrict switch is on. Switch back to "Tables + Views", turn on
"Restrict to selected objects", check exactly one object (e.g. `sales.vw_CustomerOrderSummary`),
click Save, confirm the success toast. Navigate to `http://localhost:5080/querybuilder`, start a
new query against "Sales Sample", confirm the catalog sidebar now shows only that one object.
Return to the admin page, turn the restrict switch off, Save again, confirm the catalog sidebar is
back to the full object list (per this plan's Global Constraints — leave the demo source
unrestricted). Stop the background app afterward.

- [ ] **Step 5: Commit**

```bash
git add client/src/pages/AdminDataSourcePolicyPage.tsx client/src/App.tsx src/QueryBuilder.Editor/wwwroot/
git commit -m "Add the catalog policy editor page

Segmented control for CatalogScope, a restrict-to-selected-objects
switch, and a searchable checklist scoped to the current selection.
Closes the loop end-to-end: an admin's save here is reflected
immediately in the query builder's catalog sidebar.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: Docs + final regression pass

**Files:**
- Modify: `README.md`
- Modify: `src/QueryBuilder.Editor/README.md`

**Interfaces:**
- Consumes: nothing new — this task documents and re-verifies everything built in Tasks 1–6.

- [ ] **Step 1: Root `README.md`**

In `README.md`, replace:

```markdown
- Disable a saved query to block it from being run or exported by anyone (owner or shared users)
  without deleting it; owner-only toggle, reversible, logged to the audit history
- Configurable actor identity (`ActorResolver`) and access control (anonymous / authenticated /
  role / custom policy), applied uniformly across every route
```

with:

```markdown
- Disable a saved query to block it from being run or exported by anyone (owner or shared users)
  without deleting it; owner-only toggle, reversible, logged to the audit history
- Admin UI (role-gated via `AdminAuthorization`, separate from the main `Authorization` gate): per
  data source, control whether the catalog shows views only / tables only / both, and allowlist
  specific tables/views — replaces the previous raw-SQL-only path for this one concern
- Configurable actor identity (`ActorResolver`) and access control (anonymous / authenticated /
  role / custom policy), applied uniformly across every route
```

Update the `## Roadmap` section — remove the now-partially-done admin item and note what remains,
replacing:

```markdown
Not yet built, tracked for a future version:

- Manually-written SQL as an alternative to the visual builder
- Turning a result set into a saved visualization
- Dashboards composed of multiple visualizations
```

with:

```markdown
Not yet built, tracked for a future version:

- Admin UI for registering/editing data sources themselves (name, connection string, provider,
  `AllowedSchemas`) — still raw-SQL-only; only the catalog scope/allowlist is admin-manageable so far
- Manually-written SQL as an alternative to the visual builder
- Turning a result set into a saved visualization
- Dashboards composed of multiple visualizations
```

- [ ] **Step 2: Package `README.md`**

In `src/QueryBuilder.Editor/README.md`, replace:

```markdown
appearing as a new pick.

## Access Control
```

with:

````markdown
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

## Access Control
````

Update the `## What's included` list, replacing:

```markdown
- Full audit log (created/updated/deleted/run/exported/shared/unshared/disabled/enabled) with a
  configurable actor identity and access control
```

with:

```markdown
- Admin UI (separately role-gated via `AdminAuthorization`) to control each data source's catalog
  scope (views/tables/both) and a specific table/view allowlist
- Full audit log (created/updated/deleted/run/exported/shared/unshared/disabled/enabled/catalog
  policy updated) with a configurable actor identity and access control
```

- [ ] **Step 3: Full regression pass**

Stop stray `dotnet` processes. Rebuild everything clean:

```bash
dotnet clean src/QueryBuilder.Editor/QueryBuilder.Editor.csproj -c Debug
dotnet build src/QueryBuilder.Api/QueryBuilder.Api.csproj -c Debug
cd client && npm run build && cd ..
```

Copy the bundle (it should already be current from Task 6, but confirm):

```bash
rm -rf src/QueryBuilder.Editor/wwwroot/assets
cp -r client/dist/assets src/QueryBuilder.Editor/wwwroot/assets
cp client/dist/index.html src/QueryBuilder.Editor/wwwroot/index.html
```

Start the sample app in the background, wait for it to come up, then walk the full spec Testing
checklist (`docs/superpowers/specs/2026-09-22-admin-catalog-allowlist-design.md`, "## Testing"):
non-admin nav-hiding and direct-navigation 403 behavior (reuse Task 2 Step 6's temporary
`Mode = Role` edit-and-revert technique against `/admin` in the browser this time, not just the
`/access` probe), scope change reflected immediately, object-subset restriction reflected
immediately and previously-saved queries still opening, audit log entry present. Confirm the demo
data source ends this pass at `CatalogScope = tablesAndViews`, `AllowedObjects = []` — reset it via
the admin UI if the checklist left it otherwise. Stop the background app afterward.

- [ ] **Step 4: Commit**

```bash
git add README.md src/QueryBuilder.Editor/README.md
git commit -m "Document the admin UI: catalog table/view allowlist

Both READMEs' feature lists and roadmap updated; new Admin UI section
in the package README covering AdminAuthorization.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

Do not bump `<Version>`, run `dotnet pack`, push to NuGet, or `git push` — per this plan's Global
Constraints, those happen only when the user explicitly asks.
