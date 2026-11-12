# Verification — Dashboards (Phase 2)

Run on 2026-11-03 on branch `feature/rea1` (uncommitted). Local only: LocalDB
`QueryBuilderAppDb` / `QueryBuilderDemoSource`, synthetic fixtures, temporary DevPersonas
(reverted). Artifacts are in the session scratchpad folder `acceptance-dash\`, not committed.

## Automated
- `dotnet test tests/QueryBuilder.Tests`: **157 passed / 0 failed** (126 from Phase 1, 31 for
  dashboards, plus 1 regression test added during review).
- Negative proof 1: removing the restricted-tile stripping made 2 leak tests fail
  (`Failed: 2, Passed: 154`). Restored, all pass.
- Negative proof 2: reverting the share-preview redaction (found in review, see below) made
  `Share_preview_never_reveals_a_query_the_owner_cannot_view` fail (`Failed: 1, Passed: 156`).
  Restored, 157 pass.
- `npm run build` passes, and `wwwroot` is refreshed (`index-BPp55obK.js`). There are **no new
  npm or NuGet packages**: `package.json` and `package-lock.json` are unchanged since Phase 1.
- `dotnet clean` + `dotnet pack` to the scratchpad: all 4 DLLs are present, and the Editor DLL
  embeds the SPA.

## Runtime acceptance (SPEC §3)
| AC | Result | Evidence (summary) |
|---|---|---|
| C1 migration on existing DB | PASS | `AddDashboards` applied. FKs: tile→dashboard and share→dashboard CASCADE, tile→viz SET_NULL |
| D1 create/arrange/reload | PASS | Order, widths and text identical after reload; keyboard reorder used |
| D2 roles | PASS | Editor: save tiles and rename 200. Viewer: 403/403. Non-owners: 403 on delete and every share endpoint |
| D3 concurrency | PASS | Stale version gives 409; UI banner with Reload |
| S1 scope isolation | PASS | Program-admin 10 gets exactly `[{ProgramId:10,Enrolments:20}]`; Super admin gets 5 rows; UI shows 1 bar |
| S2 restricted tile | PASS | `restricted:true`; Q2/V2 names and ids absent from the whole response body; render 403 |
| S3 "also share queries" | PASS | Positive case shares only Q2 as Viewer. A query not on the dashboard gives 400; a query the caller doesn't own gives 403; nothing persisted on refusal. Redacted preview entry confirmed |
| S4 unshared user | PASS | 403 on GET; dashboard absent from their list |
| S5 Editor without query access | PASS | Adding that chart gives 403. Saving the full list keeps the restricted tile's VisualizationId unchanged (sqlcmd) |
| S6 disabled query | PASS | That tile gets 409; the other tile still renders |
| E1 removed chart / cascade | PASS | `missing:true`, DB VisualizationId NULL, UI "This chart was removed"; deleting the dashboard cascades tiles and shares |
| E2 XSS in text tile | PASS | Rendered as literal text; 0 script/img elements; no dialog |
| E3 31 tiles | PASS | 400; nothing persisted |
| E4 refresh | PASS / partial | Refresh all and 1-min auto-refresh re-render tiles. Pause on hidden tab: **NOT RUN** |
| A1 accessibility | PASS | Tiles are regions with h2 headings; focusable reorder/size/edit buttons; Space/Arrow keyboard reorder works |
| C2 DBA script (fresh DB) | PASS | Idempotent script with `sqlcmd -I -b`: all 8 migrations, 3 tables, FK rules correct |
| C3 embedded bundle | PASS | Browser asset name matches `wwwroot/index.html` |

## Fixes made during review and verification
1. **Security (share-preview leak):** the owner's share-preview could show the name and id of a
   tile query that the owner cannot view (an Editor added a chart from their own unshared query).
   It is now redacted to "A chart you don't have access to" (`Guid.Empty`, not shareable), in
   `src/QueryBuilder.Application/Dashboards/DashboardSharing.cs`, with a regression test.
   Re-verified at runtime in S3.
2. **UX:** a browser reload of a just-created dashboard re-entered edit mode, because history
   state survives a reload. The page now clears that state after entering edit mode
   (`client/src/pages/DashboardPage.tsx`). Re-verified: a new dashboard opens in edit mode and a
   reload opens it in view mode.

## Limits
- No runtime comparison with Redash (its local instance was not driven), so **no parity claim**.
- Pause-on-hidden-tab auto-refresh was not exercised.
- The known pre-existing issue that the full idempotent script cannot be re-run (1.0.13
  `AddCatalogScopeAndAllowedObjects`) is unchanged.
