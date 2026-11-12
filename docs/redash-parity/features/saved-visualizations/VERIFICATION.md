# Verification — Saved visualizations (Phase 1)

Run on 2026-11-02/03 on branch `feature/rea1` (uncommitted). Local only: `(localdb)\MSSQLLocalDB`
with `QueryBuilderAppDb` and `QueryBuilderDemoSource`, using synthetic fixtures and temporary
DevPersonas (reverted). Evidence files (screenshots, script) are in the session scratchpad folder
`acceptance\`. They are not committed.

## Automated
- `dotnet test tests/QueryBuilder.Tests`: **126 passed / 0 failed** (75 existing + 51 new).
- Negative proof for the scope check: changing the render handler to `Build(definition, [])`
  made `Guard_predicates_reach_the_sql_builder` fail (`Failed: 1, Passed: 21`). With the code
  restored, all 126 pass.
- `npm run build` (tsc + vite) passes. The bundle is refreshed into
  `src/QueryBuilder.Editor/wwwroot` (`index-Dw3wmGkK.js` after the dialog-defaults fix).
- `dotnet clean` + `dotnet pack` to the scratchpad: the nupkg contains the Editor, Domain,
  Application and Infrastructure DLLs, and the Editor DLL embeds the new SPA.
- Licenses: 37 npm packages added, all MIT, ISC or BSD-3. Nothing commercial. No NuGet changes.

## Runtime acceptance (SPEC §3)
| AC | Result | Evidence (summary) |
|---|---|---|
| W1 create/reopen | PASS | Chart tab is still there after reload; 5 bars |
| W2 Editor vs Viewer | PASS | Editor got 200/200/204; Viewer got 403/403/403 on edits and 200 on list and render |
| W3 cascade | PASS | Visualization row removed together with its query |
| S1 isolation | PASS | Super admin 5 rows. Program-admin 10 got exactly `[{ProgramId:10,Enrolments:20}]`. No-scope got 403, same as `/run` |
| S2 unshared | PASS | 403 on list and render |
| S3 revoked | PASS | 403 on the next request |
| S4 disabled | PASS | 409 for owner and share holder; 200 after re-enabling |
| S5 projection | PASS | Only the referenced columns returned from a 4-column query |
| V1 invalid options | PASS | 400 for a missing column; 400 for a non-number measure |
| V2 stale | PASS | 400 "needs updating: TotalFees"; the UI shows the needs-updating state |
| L1 record limit | PASS | `MaxRecords=3` gave 409; no data returned |
| L2 chart cap | PASS | Synthetic 1,500-row view (dropped afterwards) gave 409 "too many points" |
| L3 pie | PARTIAL | 15 slices gave 400. Negative values covered only by an xUnit test (no negative fixture data) |
| E1 empty / null | PARTIAL | Empty: 200 with 0 rows, UI empty state. Nulls: **not runtime-tested** (no NULL fixture) |
| P1 parameter injection | PASS | Malicious value rejected with 400. SQL text holds `@pid`, never the value |
| A1 accessibility | PASS | `role="img"` plus a descriptive aria-label; "Show data" table works; tabs and button are focusable (checked with `.focus()`, not real keyboard presses) |
| C1 upgrade | PASS | Migration auto-applied to the existing 1.0.13 database; existing run and CSV export still return 200 |
| C2 DBA script | PARTIAL | Fresh-DB run succeeds. **The second run fails in an older migration** (see below); the new migration is not involved |
| C3 embedded bundle | PASS | Browser asset names match `wwwroot/index.html` |

## Fix made after acceptance
- The chart dialog's default roles used to pick the first numeric column as the measure, so an
  ID such as ProgramId became both X and measure. Now the grouping column goes on X and an
  aggregated total is the measure (`client/src/features/visualizations/chart-data.ts`
  `defaultRoles`). Re-verified at runtime on the demo query "Total Lifetime Value by Country":
  X = Country, measure = Total Lifetime Value.

## Found, not fixed (outside this slice)
1. **Pre-existing, already shipped in 1.0.13:** the idempotent DBA script is not re-runnable.
   `QueryBuilder.schema.1.0.13.sql:202` runs
   `UPDATE DataSources SET CatalogScope = CASE WHEN ViewsOnly = 1 …`, which comes from the
   `AddCatalogScopeAndAllowedObjects` migration's `migrationBuilder.Sql(...)`. Once that
   migration has been applied, the `ViewsOnly` column no longer exists. SQL Server compiles the
   whole batch even inside the `IF NOT EXISTS` guard, so a re-run fails with Msg 207.
   Likely fix: wrap that statement (and the Down counterpart) in `EXEC(N'…')`.
2. The idempotent script must be run with `sqlcmd -I` (QUOTED_IDENTIFIER), otherwise it fails
   with Msg 1934. `Scripts/README.md` should say so.
3. `npm install` reported 20 audit advisories; not triaged.
4. The main JS bundle grew by about 433 KB (379 KB gzip total). Lazy-loading the chart code would
   bring the first page load back down.

## Limits
- No runtime comparison with Redash was done (U1 not approved), so **no parity claim** is made.
  Redash behavior in the SPEC comes from its source code and docs.
- The `DataScopeRules` table in local `QueryBuilderAppDb` already held 4 rows (`CLAUDE.md` says it
  had been cleared). The run left them unchanged.
