# Verification — Query parameters + dashboard parameters

Run on 2026-11-03 on branch `feature/rea1` (uncommitted). Local LocalDB, synthetic `PARAM*` queries,
charts and dashboard (owner Super admin, shared with Program-admin 10), temporary dev personas.

## Automated
- `dotnet test`: **298 passed / 0 failed** (177 before; Phase A +96, Phase B +25).
- Negative proof (Phase A): replacing the list-membership check made 2 binder tests fail; restored.
- `npx tsc -b`, `npm run build` clean; wwwroot refreshed. No new packages.

## Runtime acceptance
| AC | Result | Evidence |
|---|---|---|
| A1 fixed list | PASS | `status=Completed` → 5 per program; `status=z` → 400 "Status: 'z' is not one of the allowed values." |
| A2 multi-select | PASS | `programs=["10","30"]` → exactly programs 10 and 30; `["99"]` → 400 |
| A3 query-based dropdown | PASS | Options: Super admin 10–50, Program-admin 10 → `[10]` only, user without access to the source query → 403; Program-admin 10 sending 30 → 400 |
| A4 date range + relative | PASS | `2025-01-01~2025-03-31` filters rows; `last_30_days` resolved server-side (no rows: data ends 2025-10); preview shows bound `BETWEEN @p1 AND @p2` |
| A5 URL | PASS (after fix) | `?p_programs=10&p_programs=30&p_status=Active` pre-fills the bar; Apply → 10/30 with 5 each; picking another status updates `p_status` |
| A6 save as defaults | PASS (after fix) | Save as defaults → Save → reload shows Status = Active; stored defaults `programs=["10","30"] status=Active` |
| A7 required/unknown | PASS (API) | Unknown parameter → 400 "Unknown parameter 'nope'"; optional with no value → condition dropped (all rows; scope still applies) |
| A8 dashboard parameter | PASS | One "Programs" bar drives both tiles; Super admin sees 10 and 30; Program-admin 10 sees only 10 (scope) |
| A9 fixed / tile input | PASS | Fixed Status hidden and applied; "Enrolled" date range shown as a tile input with its own Apply |
| A10 persistence | PASS | Parameters + mappings round-trip; unknown mapped query parameter → 400 |
| A11 compatibility | PASS | Existing queries/dashboards (Program overview) unchanged; 0 page errors |
| A12 DBA script | PASS | Fresh idempotent script applied with `sqlcmd -I -b` to a throwaway DB: 10 migrations, new nvarchar columns; DB dropped |
| A13 no lost functionality | PASS | Run/export/preview pass bar values; charts, add-to-dashboard, layout editing unaffected (smoke) |

## Fix made during verification
- Single-value dropdowns lost their value on load (`p_status` from the URL dropped, and "Save as defaults"
  stored an empty status): Radix Select reports `''` while its items aren't mounted. The parameter
  controls now ignore `''` (`controls/ListControls.tsx`, `controls/ParameterControl.tsx`).

## Known behaviour to confirm with the user
- A dashboard multi-value feeding a query-based dropdown is validated strictly against the **viewer's**
  options: Program-admin 10 choosing 10 and 30 sees that tile refuse with "'30' is not one of the allowed
  values" (safe, but unfriendly). Option: drop out-of-zone values for query-based lists instead.
- Dashboard parameters are created from a tile's "Parameters…" dialog (no free-form "add parameter").
- The "Unapplied changes" hint appears only after a first Apply on the page.

## Not run
- Full UI walk-through of the parameter settings dialog and the tile mapping dialog (covered by unit tests
  and type checks, exercised indirectly); keyboard-only pass on the new controls.
