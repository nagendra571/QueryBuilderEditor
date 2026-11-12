# PLAN — Saved visualizations (Phase 1)

Status: **APPROVED 2026-11-02** — U3 refuse-when-truncated, U4 Recharts.

Branch: `feature/rea1` (already created from `bca3dcb`; isolated from `main`). Unrelated local
changes are preserved: `CLAUDE.md`, the `Sample-*.sql` files and `.playwright-mcp/` are never
staged. Approach: TDD for backend handlers (tests first, watched fail). Small commits, one per
task below, only when you ask.

## Components to change

### Domain (`src/QueryBuilder.Domain`)
1. `Entities/QueryVisualization.cs` — `AuditableEntity` with `SavedQueryId`, `Name`, `Type`,
   `OptionsJson`, `DisplayOrder`.
2. `Enums/Enums.cs` — `VisualizationType { Bar, Line, Pie, Counter }`; append `AuditAction`
   values 12–14.
3. `Model/VisualizationOptions.cs` — typed options (`Version`, `X`, `Measures`, `Series`,
   `Stacked`, `SortX`, `Counter`).

### Application (`src/QueryBuilder.Application`)
4. `Abstractions/Repositories.cs` — `IVisualizationRepository` (list by query, get, add via
   DbSet, remove, save).
5. `Visualizations/VisualizationOptionsValidator.cs` — pure function: options × `QueryDefinition`
   ⇒ errors (unknown/hidden column, non-numeric measure, shape rules per type). Used by save and
   render.
6. `Visualizations/SavedQueryAccess.cs` — shared helper: `CanView` (owner or any share),
   `CanEdit` (owner or Editor). Reuses the logic in `GetSavedQueryByIdQuery`/`SaveQueryCommand`
   instead of duplicating it.
7. Commands/queries: `GetVisualizationsQuery`, `SaveVisualizationCommand` (create/update),
   `DeleteVisualizationCommand`, `RenderVisualizationCommand`. The render command implements SPEC
   §2.4 steps 1–10, reusing `IDataCatalogService`, `IDataScopeGuard`, `IQuerySqlBuilderFactory`,
   `IQueryExecutionService`, `RecordLimits`, `IAuditLogger`.
8. `Common/RecordLimits.cs` — add `ChartMaxRows = 1_000` and `ChartRows(effectiveLimit)`.
9. `Exceptions` — reuse existing ones (403/404/409/400). New: `VisualizationOutOfDateException`
   → 400 (or reuse `CatalogValidationException` with a specific message).
10. DTOs in `Dtos/VisualizationDtos.cs`.

### Infrastructure (`src/QueryBuilder.Infrastructure`)
11. `Persistence/Configurations/QueryVisualizationConfiguration.cs` — table, lengths, string
    enum, cascade FK, index on `SavedQueryId`.
12. `AppDbContext` — `DbSet<QueryVisualization> QueryVisualizations`.
13. `Repositories/VisualizationRepository.cs`; register in `DependencyInjection.cs`.
14. Migration `AddQueryVisualizations` via
    `dotnet tool run dotnet-ef migrations add AddQueryVisualizations --project src/QueryBuilder.Infrastructure --startup-project src/QueryBuilder.Api --output-dir Persistence/Migrations`.

### Editor package (`src/QueryBuilder.Editor`)
15. `Endpoints/VisualizationEndpoints.cs` — the five routes. Bodies read via
    `ReadFromJsonAsync(QueryBuilderJson.Options)`, responses via `Results.Json(…,
    QueryBuilderJson.Options)`. Mapped inside the existing group in
    `EndpointRouteBuilderExtensions.MapQueryBuilderEditor`, so host `Authorization` applies.
16. `GlobalExceptionHandler` — map any new exception.

### Frontend (`client/src`)
17. `types/index.ts`, `lib/api.ts` — visualization types and API calls.
18. `hooks/useVisualizations.ts` — TanStack Query hooks. Mutations invalidate
    `['visualizations', queryId]`. A render query key includes the parameter values. Selectors
    never return new arrays from Zustand (sharp edge).
19. `features/visualizations/ChartEditorDialog.tsx` — type/column pickers (existing shadcn
    `Select`, `Dialog`, `Switch`) plus a live preview.
20. `features/visualizations/ChartRenderer.tsx` — Bar/Line/Pie/Counter. aria-label, "Show data"
    table, empty/stale/denied/disabled states.
21. `features/visualizations/chart-data.ts` — pure transform (rows → series, null handling,
    sorting, pie checks). Kept pure so it can be unit-tested if a client test runner is approved
    (none exists today; adding `vitest` (MIT) would be a separate approval).
22. `pages/QueryEditorPage.tsx` — chart tabs between Results and History, plus an **Add chart**
    button (owner/Editor only).
23. ⚑ U4: if Recharts is approved, `npm install recharts` and add `components/ui/chart.tsx`
    following the shadcn Chart pattern. Then run the license audit over the new
    `package-lock.json` subtree (G-LIC).

### Tests (`tests/QueryBuilder.Tests`)
24. `VisualizationOptionsValidatorTests` — AC-V1, shape rules, stale detection (AC-V2), pie
    rules (AC-L3).
25. `RenderVisualizationCommandTests` — with fakes for repositories, guard, builder factory and
    executor:
    - AC-S2/S3: no share → 403;
    - AC-S4: disabled → 409;
    - AC-S5: projection;
    - AC-S6: scope predicates reach `Build`. A deliberately broken variant (predicates dropped) is
      shown to fail;
    - AC-L1/L2: overflow refusal;
    - Viewer vs Editor rules for save/delete (AC-W2).
26. `ChartDataTests` are not possible in xUnit (client code). The null/empty behavior (AC-E1) is
    covered by the Playwright run instead.

### Docs
27. Both READMEs: a feature bullet, a "Visualizations" section in the package README, and the
    roadmap updated. `CLAUDE.md` working notes updated at release.

## Migration / package implications

- Additive table plus a cascade FK. No data migration. Auto-migration (`ApplyMigrations = true`)
  applies it on first start.
- At release time only (when asked to bump): regenerate `Scripts/QueryBuilder.schema.<ver>.sql`
  per `src/QueryBuilder.Editor/Scripts/README.md`, delete the previous script, and bump to
  1.0.14.
- The embedded SPA grows by the chart library (Recharts ≈ 100 KB gzip, estimated; to be measured
  in `npm run build` output).
- No new NuGet dependencies are planned.

## Validation commands

```bash
dotnet test tests/QueryBuilder.Tests                       # unit + negative tests
cd client && npm run build && cd ..                         # type-check + bundle
rm -rf src/QueryBuilder.Editor/wwwroot/assets
cp -r client/dist/assets src/QueryBuilder.Editor/wwwroot/assets
cp client/dist/index.html src/QueryBuilder.Editor/wwwroot/index.html
dotnet run --project src/QueryBuilder.Api --no-launch-profile --urls http://localhost:5080   # background
```

Then, with the temporary `DevPersonas` block in `src/QueryBuilder.Api/Program.cs` (marked
`TEMP-DATASCOPE-TEST` and reverted afterwards), I'll use Playwright MCP to check:
- AC-W1–W3, AC-S1–S5, AC-L1–L3, AC-E1, AC-A1 via personas Super admin / Program-admin 10 /
  No scope;
- the API-level negatives via `ctx_execute` `fetch` with the persona cookie.

DB checks use `sqlcmd -S "(localdb)\MSSQLLocalDB"` on `QueryBuilderAppDb` (table exists, cascade
works). Packaging dry run: `dotnet clean && dotnet pack -c Release -o ./nupkg` (local only;
**no push**).

Afterwards: remove the test rules and the record limit, revert `Program.cs`, stop the `dotnet`
processes.

## Out of scope for this slice

Dashboards, stored parameter values per chart, result caching, schedules, alerts, sharing a
single chart, anonymous links, image export, and fixing D2 (favorites).
