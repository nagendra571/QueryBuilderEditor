# Admin UI: catalog table/view allowlist

## Motivation

`QueryBuilder.Editor` has no in-app way for an administrator to control what a data source
exposes to business users beyond two coarse, DB-managed-only knobs on `DataSource`
(`AllowedSchemas`, `ViewsOnly`) — today they can only be set via raw SQL against QueryBuilder's
metadata database (see `CLAUDE.md`'s "Discussed, not built" note). This spec adds the first slice
of an in-app admin area: a role-gated UI where an admin picks, per data source, whether the
catalog shows views only / tables only / both, and which specific tables/views are allowed.

Full data-source registration (name, connection string, provider) and schema-level allowlist
management stay out of scope for this slice — they remain raw-SQL-only for now, to be picked up
by a future admin feature.

## Data model

`QueryBuilder.Domain.Entities.DataSource`:

- Remove `bool ViewsOnly`. Add `CatalogScope CatalogScope { get; set; } = CatalogScope.Views`, a
  new enum in `QueryBuilder.Domain.Enums`:
  ```csharp
  public enum CatalogScope { Views = 0, Tables = 1, TablesAndViews = 2 }
  ```
- Add `List<string> AllowedObjects { get; set; } = []` — "SchemaName.ObjectName" keys. Empty list
  means "no restriction beyond `CatalogScope`/`AllowedSchemas`" — same convention as the existing
  `AllowedSchemas` ("empty = all").

EF configuration (`DataSourceConfiguration`): `AllowedObjects` uses the identical comma-joined
`HasConversion` + `ValueComparer` pattern already used for `AllowedSchemas` — copy, don't
abstract; there are only two of these and a shared helper isn't worth it yet.

**Migration** (`AddCatalogScopeAndAllowedObjects`):
1. Add `CatalogScope` column (`int`, not null, default `0`).
2. Backfill: `UPDATE DataSources SET CatalogScope = CASE WHEN ViewsOnly = 1 THEN 0 ELSE 2 END`
   (raw SQL via `migrationBuilder.Sql(...)`, between the add and the drop below).
3. Drop `ViewsOnly` column.
4. Add `AllowedObjects` column (`nvarchar(max)`, not null, default `''`).

This is a breaking change to `DataSourceDto.ViewsOnly` (bool) in the JSON contract — it becomes
`CatalogScope` (string enum). Confirmed acceptable: the field is declared in the frontend's
`types/index.ts` but not read anywhere in `client/src` today, so nothing breaks on the frontend
side, and this package has been iterating fast pre-1.0-API-stability (1.0.0 → 1.0.10 already).

## Catalog filtering (`SqlServerDataCatalogService.BuildCatalogAsync`)

Existing filter chain (schema exclusion → `AllowedSchemas` → `ViewsOnly`) becomes: schema
exclusion → `AllowedSchemas` → `CatalogScope` (skip `Table` when `Views`, skip `View` when
`Tables`) → `AllowedObjects` (skip when non-empty and `"{schema}.{name}"` not in the list,
case-insensitive).

## Admin authorization

New `QueryBuilderEditorOptions.AdminAuthorization` — a second instance of the existing
`QueryBuilderAuthorizationOptions` type (`Mode` / `RoleNames` / `PolicyName`), defaulting to
`QueryBuilderAuthorizationMode.Anonymous`, documented with an explicit warning to set it before
going to production (consistent with how the package already documents `Authorization`).

`EndpointRouteBuilderExtensions.MapQueryBuilderEditor`: admin routes are mapped as a nested group
under the already-`ApplyAuthorization`'d base group:

```csharp
var adminGroup = group.MapGroup("/api/admin").WithTags("Admin");
ApplyAuthorization(adminGroup, options.AdminAuthorization);
adminGroup.MapAdminEndpoints();
```

ASP.NET Core's endpoint authorization combines metadata from nested route groups with AND
semantics, so a request must satisfy both `options.Authorization` (the app-wide gate) and
`options.AdminAuthorization` — no new authorization-composition code needed, reusing
`ApplyAuthorization` as-is.

## Backend endpoints (new `Endpoints/AdminEndpoints.cs`, new `Queries.Admin`/`Commands.Admin` folders)

- `GET /api/admin/access` → `204 No Content` if the caller passes `AdminAuthorization` (the
  ASP.NET Core authorization middleware handles the 401/403 before the handler runs; the handler
  itself is a no-op). Purely a client-side "should I show the Admin nav item" probe.
- `GET /api/admin/data-sources` → `List<AdminDataSourceSummaryDto>` (`Id`, `Name`, `Provider`,
  `IsActive`) — every data source, not just active ones (admins need to see inactive ones too,
  unlike the business-user-facing `GetDataSourcesQuery`).
- `GET /api/admin/data-sources/{id}` → `AdminDataSourceDetailDto`:
  ```csharp
  public sealed record AdminDataSourceDetailDto(
      Guid Id, string Name, CatalogScope CatalogScope, List<string> AllowedObjects,
      List<AdminCatalogObjectDto> Objects);
  public sealed record AdminCatalogObjectDto(string SchemaName, string Name, SchemaObjectKind Kind);
  ```
  `Objects` is the **raw, unfiltered** list (new `IDataCatalogService.GetRawObjectsAsync`) —
  bypasses `CatalogScope`/`AllowedObjects` (that's exactly what's being configured) but still
  respects `AllowedSchemas` and the system-schema exclusion, since those stay untouched by this
  feature. No column metadata needed here, just schema/name/kind — cheap, not cached (admin usage
  is infrequent, and a stale picker list would be actively confusing).
- `PUT /api/admin/data-sources/{id}/catalog-policy`, body
  `{ catalogScope: CatalogScope, allowedObjects: string[] }` → `204`. Validates every
  `allowedObjects` entry actually exists in the raw object list (`CatalogValidationException`
  otherwise — same exception type/handling already used for query-definition validation). Updates
  the `DataSource` row, evicts that data source's `IMemoryCache` catalog entry (cache key
  `catalog:{dataSourceId}`, already used by `SqlServerDataCatalogService` — the command handler
  takes `IMemoryCache` as a dependency and calls `Remove`), and logs a new
  `AuditAction.DataSourceCatalogPolicyUpdated` entry (`entityType: nameof(DataSource)`).

## Frontend

- New route `/admin` (index: `AdminDataSourcesPage`, list of data sources) and
  `/admin/data-sources/:id` (`AdminDataSourcePolicyPage`).
- `AppShell`: new "Admin" nav item, rendered only if a `useQuery` against `GET /api/admin/access`
  on app load succeeds; a 401/403 hides it (not an error toast — an unauthorized user should never
  see this as a failure, just as "there's no admin link").
- `AdminDataSourcePolicyPage`:
  - Segmented control: Views only / Tables only / Tables + Views → `catalogScope`.
  - A "Restrict to selected objects" switch. Off → `allowedObjects: []` sent on save (no
    restriction). On → a searchable checklist of `Objects` filtered to the objects matching the
    current `catalogScope`, pre-checked from the existing `allowedObjects` (or all-checked the
    first time the switch is flipped on, matching "previously unrestricted" intuitively).
  - Save button → `PUT .../catalog-policy`, toast on success/failure, matching the existing
    `useMutation` + `toast.error(error instanceof ApiError ? ...)` pattern used throughout
    `client/src/hooks`.

## Out of scope (this slice)

- Full data-source CRUD (name, connection string, provider) — stays raw-SQL-only.
- `AllowedSchemas` management UI — stays raw-SQL-only; the raw object picker still respects it as
  a pre-existing, untouched filter.
- Any change to the *business-user*-facing `DataSourceDto`/`GET /api/data-sources` beyond the
  `ViewsOnly` → `CatalogScope` rename forced by the data model change.

## Testing

Same approach as prior features in this package (no automated test project exists yet; see
`CLAUDE.md`'s Testing section) — manual verification via the local sample app:
1. `dotnet build` clean.
2. As a non-admin (default `AdminAuthorization.Mode = Anonymous` still lets everyone through
   during local dev unless configured — verify the 403 path separately by temporarily setting
   `Mode = Role` with a role the test user doesn't have) confirm `/admin` nav item hidden and
   direct navigation to `/admin` 403s cleanly.
3. As an admin: change `CatalogScope`, verify the business-facing catalog sidebar reflects it
   immediately (cache eviction working) without restarting the app.
4. Restrict to a specific object subset, verify the query builder catalog only shows those, and
   that a previously-saved query referencing a since-excluded object still opens (but its catalog
   sidebar no longer lists that object) — confirms this doesn't retroactively break existing saved
   queries, only what's offered going forward.
5. Confirm the audit log records `DataSourceCatalogPolicyUpdated`.
6. `npm run build` clean.
