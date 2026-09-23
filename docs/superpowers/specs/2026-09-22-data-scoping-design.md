<!-- Approved design, updated after implementation to match what was built. -->

# Row-level data scoping ("Program-admin sees only ProgramId = 10")

## Context

After the demo, the main concern was protecting data. Stakeholders want a way to limit which *rows* a user can see: a Program-admin with `ProgramId = 10` sees only the 20 of 100 rows where `ProgramId = 10`, while Super admin and Admin see everything. The package is generic, so it must not know about "programs". Today one team says ProgramId; tomorrow another says ModuleId or SystemId. We already limit *which objects* are visible (views only, allowlist); this adds limits on *which rows*.

Decisions agreed in brainstorming:

| Question | Decision |
|---|---|
| Which view column a key filters on | **Admin UI mapping only**: per data source and per view, the admin maps each scope key to a column |
| Views the admin hasn't decided on | **Fail closed**: hidden from scoped users until the admin either maps them or marks them "Not scoped (visible to everyone)" |
| Unrestricted vs scoped | **Explicit**: the resolver returns `DataScope.Unrestricted` or `DataScope.For(...)`. `null` or an exception means *denied*. No resolver configured means the feature is off (backward compatible) |
| Several values or keys per user | **Both**: `ProgramId IN (10, 12)`. Several keys mapped on one view are combined with AND |
| Where key names come from | **Declared in host options**: the admin UI shows them as a dropdown |
| Tests | **New xUnit project** for the core logic, plus Playwright end to end |
| Preview SQL | **Shows** the injected scope filter (it matches what actually runs) |

## Security findings from exploration (fixed as part of this work)

1. **Run and export trust the definition sent by the browser.** The server never re-loads the saved definition. Enforcement therefore **has to** happen on the server at SQL-build time, not in the UI or in saved queries.
2. **SQL injection through runtime parameter names.** `SqlServerQuerySqlBuilder.AddParameter` (line 226) writes `$"@{condition.ParameterName}"` raw into the SQL, and `ValidateAsync` only checks that the name matches a declared parameter, which also comes from the browser. A crafted name such as `x) OR (1=1` would break out of the scope predicate. Fix: require `^[A-Za-z][A-Za-z0-9_]{0,63}$` for `QueryParameter.Name` and `FilterCondition.ParameterName` in `ValidateAsync`, and also assert it in the builder as a second check. The `__` prefix is reserved for our scope parameters.

## Host-facing API

```csharp
builder.Services.AddQueryBuilderEditor(options =>
{
    options.ConnectionString = "...";
    options.DataScope.Keys = ["ProgramId"];              // what admins can map columns to
    options.DataScope.Resolver = ctx =>                   // or ResolverAsync for DB/API lookups
        ctx.User.IsInRole("SuperAdmin") || ctx.User.IsInRole("Admin")
            ? DataScope.Unrestricted
            : DataScope.For("ProgramId", GetProgramIds(ctx));   // params object[]; .And("RegionId", ...) to chain
});
```

- `DataScope` is a public type in `QueryBuilder.Editor` (host-facing, like `ActorResolver`). It has `Unrestricted`, `For(key, params object[] values)`, `And(...)` and a read-only `Values` dictionary. Key lookups ignore case.
- There are two properties, `Resolver` (sync) and `ResolverAsync` (`Func<HttpContext, ValueTask<DataScope?>>`), and at most one may be set.
- Checks at startup in `AddQueryBuilderEditor`: `Keys` without a resolver, or a resolver without `Keys`, throws. Keys must be non-empty identifier-like names with no duplicates.
- Resolved **once per request** and cached, the same pattern as `CurrentUserService`. `null` or an exception means denied: logged as an error, and the user is treated as scoped with no values, so they see only views marked "Not scoped".

## Behaviour

**Mapping model (per data source, per catalog object):**

| Admin state | Scoped user | Unrestricted user |
|---|---|---|
| Undecided (no rules) | hidden | visible, no filter |
| Not scoped | visible, no filter | visible, no filter |
| Scoped: key(s) mapped to column(s) | visible, `WHERE col IN (@__scope…)` for each key, combined with AND; **hidden** if the user has no values for any mapped key | visible, no filter |

A rule that references a key no longer present in `options.DataScope.Keys` hides the view from scoped users (fail closed), and the admin UI flags it.

**Enforcement is in one place, applied to every business-DB path:**
- **Catalog** (`GetCatalogQuery`): the cached, shared catalog is filtered *per request* after it is read from the cache. The cache key stays `catalog:{id}`, which keeps it shared.
- **Run, export, preview SQL** (`RunQueryCommand`, `ExportQueryCommand`, `BuildQuerySqlQuery`) and **save** (`SaveQueryCommand`): after `ValidateAsync`, the guard checks every alias (source and joins). A hidden object throws the existing `ForbiddenException`, which returns 403 "You don't have access to 'x'". Otherwise it returns one scope predicate per alias that needs one.
- **SQL**: `IQuerySqlBuilder.Build(definition, scopePredicates)`. The parameter is **required, not optional**, so any future caller has to deal with scope. Output is `WHERE (<scope preds combined with AND>) AND (<user filters>)`. The parentheses matter because user filters can use OR at the top level. The scope sits in WHERE, before GROUP BY/HAVING, so totals are computed on scoped rows only. Parameters are `@__scope0…`, typed by the column's `DataType` from the catalog, with values converted to invariant strings (the existing `ConvertValue` path in the execution service). It stays a single plain SELECT, so `ApplyRowLimit`'s `TOP` insertion is unaffected.
- **Saved and shared queries** always run with the *runner's* scope. A query shared from Program 10 to Program 12 shows Program 12's rows. If the view is hidden for the runner, running it returns the 403 message.
- **Audit**: run and export details gain `Scope: "Unrestricted" | { ProgramId: [10] }`. Admin rule changes are audited as a new `AuditAction.DataSourceDataScopeUpdated`.
- **Out of scope**: the `AppUsers` sharing picker (not business data) and the admin endpoints (admins see the raw object list anyway).

## Implementation

### Domain / persistence
- New entity `DataScopeRule` (`src/QueryBuilder.Domain/Entities/`): `Id`, `DataSourceId`, `ObjectName` (`"schema.object"`), `ScopeKey` (nullable; null means the "Not scoped" marker), `ColumnName` (nullable). Unique index on `(DataSourceId, ObjectName, ScopeKey)`, FK to `DataSources` with cascade delete. A separate table rather than a JSON column, so DBAs can read and seed it with SQL.
- EF configuration next to `DataSourceConfiguration.cs`, a `DbSet`, and migration `AddDataScopeRules` in `src/QueryBuilder.Infrastructure/Persistence/Migrations/`. The idempotent schema script is regenerated only at release time, following the normal version-bump flow.
- Repository `IDataScopeRuleRepository`: `GetForDataSourceAsync` and `ReplaceForDataSourceAsync`. Replace-all removes the old rows and adds new ones **through the `DbSet`**, never a parent collection (the EF sharp edge in CLAUDE.md). Rules are cached in `IMemoryCache` (`datascope:{id}`, 5 min) and evicted on update.

### Application
- `ICurrentDataScope` (abstraction): `IsEnabled`, `DeclaredKeys`, and `Task<ResolvedDataScope> GetAsync(ct)` (async because of `ResolverAsync`). `ResolvedDataScope` has `IsUnrestricted` and `IReadOnlyDictionary<string, IReadOnlyList<string>> Values`. Implemented in Editor as `CurrentDataScopeService` (same shape as `Identity/CurrentUserService.cs`). When the feature is off it returns Unrestricted.
- **`DataScopeEvaluator`**, a pure static class that holds the logic and is unit-tested:
  - `IsVisible(objectKey, rules, scope, declaredKeys)`
  - `GetPredicates(definition, catalog, rules, scope)` → `List<ScopePredicate(Alias, ColumnName, DataType, Values)>`
- `IDataScopeGuard` / `DataScopeGuard`: the thin, I/O-bound wrapper the handlers call. It has `FilterCatalogAsync` and `AuthorizeAsync(dataSourceId, definition)`.
- Handlers wired: `GetCatalogQuery`, `RunQueryCommand`, `ExportQueryCommand`, `BuildQuerySqlQuery`, `SaveQueryCommand`.
- Admin: `GetDataScopeQuery` returns the declared keys, the objects (from `GetRawObjectsAsync`, filtered by the data source's `CatalogScope`/`AllowedObjects`) with their columns, and the current rules plus any stale-key warnings. `UpdateDataScopeCommand` validates every object, key and column (and that one object doesn't mix "Not scoped" with key rules), replaces the rules, evicts the cache, and writes an audit entry. It follows `Commands.Admin/UpdateCatalogPolicyCommand.cs`.

### Infrastructure
- `SqlServerQuerySqlBuilder.Build(definition, scope)`: emits the scope predicates and wraps the user filter group in parentheses when scope is present. `QuerySqlBuilderFactory` and `IQuerySqlBuilder` get the new signature.
- `SqlServerDataCatalogService.ValidateAsync`: the parameter-name validation (finding 2).

### Editor (package)
- `QueryBuilderEditorOptions.DataScope` (a `DataScopeOptions` holding `Keys`, `Resolver` and `ResolverAsync`), the public `DataScope` type, and startup checks in `ServiceCollectionExtensions.AddQueryBuilderEditor`.
- `AdminEndpoints.cs`: `GET /api/admin/data-sources/{id}/data-scope` and `PUT /api/admin/data-sources/{id}/data-scope`. Both use `QueryBuilderJson.Options` explicitly, like the other endpoints.
- Denials reuse `ForbiddenException`, which `GlobalExceptionHandler` already maps to 403.
- `GET /api/admin/data-sources/{id}/data-scope` returns `enabled`, so the UI can show or hide the section (no change to the existing admin DTOs).

### Client
- `AdminDataSourcePolicyPage.tsx` gains a **"Row-level data scope"** section, shown only when the data-scope GET returns `enabled: true`. It has one row per visible view: a state selector (*Undecided / Not scoped / Scoped*), and for Scoped, one column dropdown per declared key (the view's columns, "—" to leave that key out). It also has an "Undecided" count and a filter, so admins can see what scoped users can't access yet, plus stale-key warnings and a Save button.
- `adminApi` in `lib/api.ts`, types in `types/index.ts`, and hooks `useDataScope` / `useUpdateDataScope` in `hooks/useAdmin.ts`. `onSuccess` invalidates its own key **and** `['data-source-catalog', id]` and `['data-sources']` (the stale-cache note in CLAUDE.md).
- Builder: no change needed. The catalog is already filtered, and a 403 is shown through the existing error toast.

### Tests: new `tests/QueryBuilder.Tests` (xUnit, `IsPackable=false`, added to `QueryBuilder.slnx`)
- **Builder**: scope plus top-level OR user filters are parenthesized correctly; no user filters; aggregates with auto GROUP BY and HAVING (scope stays in WHERE); IN list with several values; two keys combined with AND; a join alias gets its own predicate; `@__scope` names never collide with `@p`.
- **Evaluator**: the undecided, not-scoped, scoped-with-values, scoped-without-values and stale-key cases; unrestricted sees everything; denied sees only views marked "Not scoped".
- **Validation**: a malicious `ParameterName` or `Parameters[].Name` is rejected, and so is the `__` prefix.
- **Options**: startup validation (keys without a resolver, a resolver without keys, both resolvers set).

### Docs
- Spec written to `docs/superpowers/specs/2026-09-22-data-scoping-design.md`, not committed until you ask.
- Both READMEs (root and `src/QueryBuilder.Editor/README.md`): a "Row-level data scoping" section covering the options example, the fail-closed rules, and "no resolver = off". Update the features and roadmap lists.
- CLAUDE.md: feature status, plus a testing note for the local `X-Test-Scope` trick.

## Verification

1. `dotnet test tests/QueryBuilder.Tests`: all green.
2. `cd client && npm run build`, copy the bundle into `wwwroot`, then `dotnet build`.
3. Local end to end with Playwright against `dotnet run --project src/QueryBuilder.Api --no-launch-profile --urls http://localhost:5080`:
   - **Temporary** `Program.cs` change (reverted afterwards, like the `X-Test-User` trick): `DataScope.Keys = ["Country"]` and a resolver that reads an `X-Test-Scope` header (`all` means Unrestricted, `Canada` means scoped). No new demo data is needed. The existing demo views show why the package stays generic: `sales.vw_OrderDetails` maps Country to **`ShippingCountry`**, and `sales.vw_CustomerOrderSummary` maps Country to `Country`.
   - Admin UI: map one view, mark the other "Not scoped", and leave `vw_OrderDetails` undecided at first. Confirm that a scoped user doesn't see it in the catalog and that a direct POST `/run` returns 403.
   - After mapping: the scoped Canada user gets only Canada rows, and totals and GROUP BY reflect only those rows. The unrestricted user gets all rows. Preview SQL shows `[ShippingCountry] IN (@__scope0)`.
   - A saved query shared between two scoped users with different values shows each user their own rows.
   - A resolver returning `null` sees only the "Not scoped" view. A crafted parameter name is rejected with 400.
   - The audit log records the scope for each run.
4. Revert the temporary `Program.cs` edit and stop stray `dotnet` processes.
5. Release steps (version bump to 1.0.12, schema script regeneration, pack, push, commit) happen **only when you ask**, following the CLAUDE.md release workflow.
