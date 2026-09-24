# QueryBuilder.Editor — working notes for Claude

Visual, no-SQL-required query builder for business users, shipped as an embeddable ASP.NET Core
NuGet package (`QueryBuilder.Editor`) with a React/TS/Tailwind/shadcn frontend built and embedded
into the assembly. Sibling product to `TemplateBuilder.Editor` — mirrors its architecture and
integration ergonomics (single-call registration, embedded SPA, DBA-friendly migrations).

- NuGet: https://www.nuget.org/packages/QueryBuilder.Editor — published version **1.0.13** (adds
  record limits, below), pushed to NuGet and committed/pushed to GitHub. The next release is
  1.0.14 — bump only when asked.
- GitHub: https://github.com/nagendra571/QueryBuilderEditor
- Hosted test deployment (user's own, real usage/bug reports come from here):
  http://templatebuilder.runasp.net/querybuilder

## Layout

```
src/
  QueryBuilder.Domain           entities, enums, QueryDefinition model — no dependencies
  QueryBuilder.Application      commands/queries (hand-rolled mediator, not MediatR — see below),
                                 validation, service abstractions
  QueryBuilder.Infrastructure   EF Core, SQL generation, catalog introspection, audit log, export
  QueryBuilder.Editor           the published package: DI/endpoint wiring, access control,
                                 setup diagnostics, embedded React app (wwwroot/)
  QueryBuilder.Api              sample host app — dotnet run this to try it locally
client/                         React/TS/Tailwind/shadcn frontend — source for QueryBuilder.Editor's
                                 embedded wwwroot
tests/QueryBuilder.Tests        xUnit (IsPackable=false) — SQL builder, data scoping, validation.
                                 References all four src projects explicitly (Editor's are PrivateAssets)
```

`Domain`/`Application`/`Infrastructure` are never published standalone — `QueryBuilder.Editor`
bundles their compiled DLLs into its own package (`BundleInternalAssemblies` MSBuild target) so a
consumer only ever installs one package. `IsPackable=false` + `PrivateAssets="all"` on those
ProjectReferences, with transitive deps re-declared explicitly in the `.csproj`.

**Mediator**: MediatR now requires a paid license, so this project uses a small in-house
`IRequest`/`IRequestHandler`/`ISender` in `Application/Common/Mediator.cs`. Don't reintroduce
MediatR.

**JSON**: every endpoint uses `QueryBuilderJson.Options` (camelCase, string enums) explicitly, via
`HttpContext.Request.ReadFromJsonAsync<T>(QueryBuilderJson.Options, ct)` and
`Results.Json(x, QueryBuilderJson.Options)` — never implicit minimal-API body binding. This makes
the package's JSON contract independent of whatever (if anything) the host app configured in its
own `ConfigureHttpJsonOptions`. Keep doing this for any new endpoint.

## The release workflow (always follow this exact sequence)

The user runs their own hosted deployment and tests changes there or asks for local verification.
**Never `dotnet nuget push` or `git push` without being explicitly asked each time** — those are
separate, deliberate asks, not implied by "implement this."

1. Make the change (backend and/or `client/`).
2. If frontend changed: `cd client && npm run build`, then copy the fresh bundle into the package:
   ```
   rm -rf src/QueryBuilder.Editor/wwwroot/assets
   cp -r client/dist/assets src/QueryBuilder.Editor/wwwroot/assets
   cp client/dist/index.html src/QueryBuilder.Editor/wwwroot/index.html
   ```
3. Verify before packaging (`dotnet test tests/QueryBuilder.Tests` plus a real run) — see "Testing" below. Don't skip this; several real bugs (EF tracking,
   Radix ScrollArea, a Zustand infinite-loop) were only caught by actually running the flow, not
   by reading the code or type-checking alone.
4. Only once asked to "bump the version and prepare the package": bump `<Version>` in
   `src/QueryBuilder.Editor/QueryBuilder.Editor.csproj` (patch bump for a bug/feature release —
   this project has been iterating fast: 1.0.0 → 1.0.11 so far, roughly one bump per fix/feature),
   `dotnet clean` + `dotnet pack -c Release -o ./nupkg`, give the user the push command — never run
   it yourself:
   ```
   dotnet nuget push ./nupkg/QueryBuilder.Editor.<version>.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
   ```
5. Only once the user confirms the NuGet push succeeded, and only once asked to "commit and push":
   stage exactly the changed files (never `git add -A` — `wwwroot/assets` churns hashed filenames
   every build, so check `git status` first), commit with a message explaining the *why*, `git push
   origin main`.

If a version was already packed+pushed and you need to add more changes before the user has pushed
it, you must bump again — NuGet versions are immutable (`409 Conflict` on a re-push of the same
version was hit once already).

## Testing

- `dotnet test tests/QueryBuilder.Tests` — run before packaging; it's fast and needs no database
  (`SqlServerDataCatalogService` tests pre-seed `IMemoryCache` with `catalog:{id}` instead).
- **`localhost` is unreliable from the Claude-in-Chrome browser tool in this environment** — it
  resolves to `chrome-error://chromewebdata/` intermittently, while external sites work fine.
  **Use the Playwright MCP tools instead** (`mcp__plugin_playwright_playwright__*`) — different
  process, reliably reaches `localhost`. Use Claude-in-Chrome only for the hosted
  `templatebuilder.runasp.net` site.
- To run the sample app locally: `dotnet run --project src/QueryBuilder.Api --no-launch-profile
  --urls http://localhost:5080` (background it). `--no-launch-profile` matters — the checked-in
  launch profile hardcodes `ASPNETCORE_ENVIRONMENT=Development`, which skews `/_setup` behavior.
- Local DB names: `QueryBuilderAppDb` (metadata) and `QueryBuilderDemoSource` (the "Sales Sample"
  data source's business data) on `(localdb)\MSSQLLocalDB`.
- The demo `QueryBuilderDemoSource` DB currently also has a `dbo.AppUsers` view with 100 hardcoded
  sample users (see `Sample-AppUsers.sql` at the repo root, not part of the package) — useful for
  testing the sharing feature's picker without real auth.
- To test sharing/multi-user flows without real auth: the sample app has no `ActorResolver`
  configured (falls through to `"anonymous"` for every request). Temporarily set
  `options.ActorResolver = ctx => ctx.Request.Headers["X-Test-User"].FirstOrDefault();` in
  `src/QueryBuilder.Api/Program.cs`, drive requests with an `X-Test-User` header for different
  simulated identities, then **revert Program.cs before packaging** — this is a real diff the user
  never asked for.
- The sample app (`QueryBuilder.Api/Program.cs`) never calls `app.UseAuthentication()`/
  `app.UseAuthorization()` — its own `Authorization`/`AdminAuthorization` are left at the `Anonymous`
  default, which needs neither. If you temporarily set either to `Authenticated`/`Role` to test the
  guard (e.g. confirming `RoleNames` validation, or that a non-default route is actually gated), a
  live request against that route 500s instead of 401/403ing, because ASP.NET Core requires that
  middleware whenever `RequireAuthorization` is in effect. This is expected, not a bug — verify the
  *absence of data/functionality* reaching a non-admin (e.g. via Playwright: nav item hidden, no
  admin data in the browser) rather than the exact status code, and revert the temporary Program.cs
  edit afterward same as the `ActorResolver` trick above.
- To test row-level data scoping: the sample app configures no `DataScope` (feature off).
  Temporarily add `options.DataScope.Keys = ["Country"]` and a `Resolver` reading an `X-Test-Scope`
  header or `qb-test-scope` cookie (`all` → `DataScope.Unrestricted`, missing → `null`/denied,
  `Canada|India` → `DataScope.For("Country", value.Split('|'))`) — the cookie lets Playwright
  switch identity via `document.cookie`. The demo views need no changes: map `vw_OrderDetails`
  Country → **`ShippingCountry`** (shows the key/column names needn't match) and
  `vw_CustomerOrderSummary` Country → `Country`. Note the local "Sales Sample" source has an
  allowlist (50 `vw_BulkTest_*` views + `vw_CustomerOrderSummary`) left over from earlier testing, so
  `vw_OrderDetails` must be temporarily added to it (and restored after). Clear the test rules
  (`PUT .../data-scope` with `{objects:[]}`) and revert Program.cs afterward.
- To see generated SQL for debugging EF issues: temporarily override
  `"Microsoft.EntityFrameworkCore.Database.Command": "Information"` in
  `src/QueryBuilder.Api/appsettings.json`'s Serilog override section, then revert.
- After any local test run, stop stray `dotnet` processes before rebuilding (`dotnet build` fails
  with file-lock errors otherwise):
  `powershell -Command "Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object { \$_.StartTime -gt (Get-Date).AddHours(-N) } | Stop-Process -Force"`.
- `.playwright-mcp/` shows up as an untracked stray dir from the Playwright tool — never commit it.

## Known sharp edges (already hit once — don't reintroduce)

- **Zustand selectors must never return a newly-constructed array/object** (e.g.
  `useStore(s => s.items.filter(...))`). Breaks `useSyncExternalStore`'s snapshot-stability
  contract → infinite render loop (React error #185). Select the stable underlying state, then
  `.filter()`/`.map()` as a plain expression in the render body instead.
- **Radix `ScrollArea` wraps children in an internal `display:table; min-width:100%` div** (for a
  horizontal scrollbar this app never renders) — it shrink-to-fits content instead of respecting
  the viewport width, silently clipping anything that should have truncated. Already fixed in
  `client/src/components/ui/scroll-area.tsx` via `[&>div]:!block` on the Viewport — don't remove
  that.
- **Tailwind `truncate` on a flex child does nothing without `min-w-0` on that same element** — a
  flex item's default `min-width: auto` blocks it from shrinking below content width even with
  `overflow:hidden` set. Pattern used throughout: `min-w-0 flex-1 truncate` on the text span,
  `shrink-0` on fixed-width siblings.
- **Adding a new EF entity via an already-tracked parent's navigation collection**
  (`parent.Children.Add(newChild)`) can leave the change tracker inferring `Modified` instead of
  `Added` when the child has a non-default client-generated key (our `Guid`s are all
  `= Guid.NewGuid()` property initializers) — produces an `UPDATE` matching zero rows
  (`DbUpdateConcurrencyException`). Always add new child entities directly via the `DbSet`
  (`db.QueryShares.AddAsync(share)`), not through the parent's collection. See
  `SavedQueryRepository.AddShareAsync`/`RemoveShare` for the pattern to copy for any future
  child-entity type.
- **`dotnet pack` on a multi-project-bundled package needs a `dotnet clean` first** — incremental
  builds don't always regenerate the embedded-files manifest after `wwwroot` changes.
- **TanStack Query v5 throws if a `queryFn` resolves to `undefined`** (`"<queryHash> data is
  undefined"`), which puts the query into an error state — `isSuccess` never becomes `true`, with no
  console error pointing at the cause. Bit us in `useAdminAccess` (`client/src/hooks/useAdmin.ts`),
  whose probe endpoint returns `void`/204: `queryFn: someApiCall` where `someApiCall` returns
  `Promise<void>` silently breaks any UI gated on that query's success. Fix: wrap it to resolve a
  real value, e.g. `queryFn: async () => { await someApiCall(); return true }`.
- **A client-side react-query cache with a `staleTime` can silently undo a server-side cache
  eviction.** When a mutation's `onSuccess` only invalidates its own detail query, a *different*
  query key reading related data (e.g. the business-facing catalog after an admin changes a data
  source's policy) keeps serving stale data for up to `staleTime` on in-SPA navigation — a full page
  reload masks this because it wipes the whole client cache, so a Playwright check that always
  reloads won't catch it. When a mutation changes something another query key depends on, invalidate
  that key too in the same `onSuccess` (see `useUpdateCatalogPolicy` in `client/src/hooks/useAdmin.ts`
  for the pattern: invalidates `['data-source-catalog', id]` and `['data-sources']` alongside its own
  key).
- **Run/export/preview trust the `QueryDefinition` the browser sends** — the server never re-loads a
  saved definition. So anything security-relevant (row-level data scope) must be enforced
  server-side at SQL-build time, never in the UI or the saved query. Every business-SQL path goes
  `ValidateAsync` → `IDataScopeGuard.AuthorizeAsync` → `Build(definition, scope)`; `Build`'s scope
  parameter is required on purpose so a new path can't skip it. Runtime parameter names are the
  one piece of browser text emitted raw into SQL (`@name`) — they're validated by
  `QueryParameterNames` (letters/digits/underscore, `__` prefix reserved for `@__scope0`...); an
  unvalidated name was a real injection hole until 1.0.12.
- **A C# `params object[]` + generic `IEnumerable<T>` overload pair binds a single `string` to the
  generic one** (string is `IEnumerable<char>`) — `DataScope.And("key", "14")` split into `"1"`,`"4"`.
  `DataScope` now has only the `params` overload and flattens collections (excluding strings) itself.
- **Requests made with axios `responseType: 'blob'` (export) receive their error body as a Blob
  too**, so `error.response.data.title` is undefined and every failure surfaced as a generic "Export
  failed." — the interceptor in `client/src/lib/api-client.ts` now parses JSON Blobs into
  `ApiError`. Keep that if adding any other binary-download endpoint.
- **Bodies are read with `ReadFromJsonAsync`, not minimal-API binding**, so a malformed or
  wrongly-typed body throws `JsonException` inside the handler — `GlobalExceptionHandler` maps that
  (and `BadHttpRequestException`) to 400. Before 1.0.13 both were unmapped 500s.

## Feature status

**Shipped**: access control (Anonymous/Authenticated/Role/Custom), `ActorResolver` identity,
single-call registration, embedded SPA under a fixed `/querybuilder` mount path (never the host's
root), DBA-friendly migrations (auto or idempotent script), `/_setup` diagnostics, auto-seeded
default data source, catalog browsing with collapsible tree + search, column totals (Sum/Avg/
Count/Distinct/Min/Max) with auto-`GROUP BY` and a HAVING filter, saved queries with SQL preview/
run/export/audit history, single-data-source picker skip, **saved query sharing** (Viewer/Editor
via an optional per-data-source `dbo.AppUsers` view — durable grant model, `AppUsers` is only the
picker source, not re-checked live), **disable a saved query** (owner-only toggle, blocks run/export
without deleting it), **admin UI — catalog scope/allowlist** (`/admin` in the SPA, gated by a second,
independent `AdminAuthorization` option that composes with `Authorization` via AND — a request must
satisfy both; defaults `Anonymous` like the rest of the package): per data source, pick
views-only/tables-only/both (`DataSource.CatalogScope`) and optionally restrict to specific
tables/views (`DataSource.AllowedObjects`), replacing raw-SQL edits for this one concern. Spec:
`docs/superpowers/specs/2026-09-22-admin-catalog-allowlist-design.md`.

**Shipped in 1.0.12**: **row-level data scoping** — host declares
`options.DataScope.Keys` + a `Resolver`/`ResolverAsync` returning `DataScope.Unrestricted` or
`DataScope.For(key, values)` per user; admins map each key to a view column per data source
(`DataScopeRules` table, admin UI section on the data-source policy page) or mark a view "not
scoped". Fail closed throughout: undecided views hidden from scoped users; resolver `null`/throw =
denied (never unrestricted); stale key or missing column hides the view. Enforced in
`SqlServerQuerySqlBuilder`'s WHERE (`(scope) AND (user filters)`, before GROUP BY) via
`Application/DataScoping/DataScopeGuard` (+ pure `DataScopeEvaluator`) on catalog/run/export/
preview/save; shared queries run with the runner's scope; scope recorded in run/export audit
details. No resolver = feature off. Also fixed the runtime-parameter-name SQL injection. Spec:
`docs/superpowers/specs/2026-09-22-data-scoping-design.md`.

**Shipped in 1.0.13**: **record limits** — `options.DefaultMaxRecords`
(1–100,000) plus a per-data-source `DataSource.MaxRecords` set in the admin UI ("Maximum records per
query"); resolution in `Application/Common/RecordLimits.cs` (data source → host default → none).
With a limit, `RunQueryCommand` caps the grid at it (ignoring the browser's `maxRows`) and returns
`rowLimit` + `truncated` (N+1 fetch, no COUNT), shown as a warning banner in `ResultsTable.tsx`;
`ExportQueryCommand` throws `RecordLimitExceededException` (409) instead of writing a cut-off file.
With neither set, the legacy 1,000 grid / 100,000 silent export caps apply unchanged, so upgrading
never starts refusing exports by itself. Designed bounded, in chat (no spec file).

**Discussed, not built**: full data-source registration/editing in the admin UI (name, connection
string, provider, `AllowedSchemas`) — still raw SQL insert only, see the package README; multi-table
joins (deliberately skipped — the user's take: "if there are joins, create a view instead, business
users won't write joins"); roadmap still has manual-SQL mode, saved-query-to-visualization, and
dashboards.

## Conventions

- No comments explaining *what* code does; only *why*, for non-obvious constraints (see the
  Zustand/Radix/EF notes above for the tone/level of detail expected).
- Every new package-facing behavior gets documented in **both** `README.md` (root) and
  `src/QueryBuilder.Editor/README.md` (the NuGet-facing one) — check both when shipping a feature,
  and update the "Roadmap"/"What's included" lists to keep them accurate.
- Commit messages explain root cause and what was verified, not just what changed — match the
  existing `git log` style.
