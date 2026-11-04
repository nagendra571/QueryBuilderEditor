# Unknowns register

U1–U4 are also recorded in REA's residual-unknown ledger (IDs given) and exported in the
scratchpad bundle. The others are tracked only here.

| ID | Question | Impact | Evidence so far | Status | Next probe | Resolution |
|---|---|---|---|---|---|---|
| U1 (`unk_1099105360e0…564b`) | How does the running Redash 8.0.0 behave at runtime for: create → save → reopen → render a chart; render by a view-only user; render after the query's columns change? | Medium. Only affects "observed Redash behavior" in the SPEC. The desired QBE behavior is specified independently | Source only: E-R03, E-R06, E-R08, E-R15 (`chart/index.js:157-158` prunes stale mappings client-side) | **Open — needs approval + credentials** | `capture_browser_scenario` on `localhost:5000` with synthetic data and two disposable users (see SPEC §1.4). Operations listed in the report to the user | — |
| U2 (`unk_d91100f5f2b9…bb21a`) | Which identity and scope should QBE background jobs (schedules, alerts, cache warmers) use, given that identity and scope resolve only from HttpContext? | High for roadmap Phase 4. **Does not block** the visualization slice (all renders are interactive HTTP requests) | E-Q07, E-Q08. Redash runs scheduled refreshes as `query.user_id` (E-R12) | Open | Design review before Phase 4: a host-supplied `IBackgroundScopeResolver(ownerId)` with fail-closed default | — |
| U3 (`unk_6bdcf1529497…bd84`) | When a chart's data hits the record limit (truncated), should QBE draw the partial chart with a warning, or refuse? | Medium. Determines acceptance criterion AC-L1 | QBE grid = partial + banner, export = refuse (E-Q05). Redash 8.0.0: no chart-level limit found (bounded grep of `redash/settings` and `client/app/visualizations`) | Resolved | — | **User decision 2026-11-02: refuse to draw (409) when truncated.** |
| U4 (`unk_f12d51ac381c…dbe`) | Which chart library: Recharts 3 vs hand-rolled SVG vs Plotly basic? | Medium. Determines PLAN tasks and bundle size | npm registry: `recharts` 3.10.1 MIT, `chart.js` 4.6.1 MIT, `plotly.js-basic-dist-min` 4.2.0 MIT. shadcn/ui's Chart component is built on Recharts | Resolved (license audit pending at install) | Audit the added `package-lock.json` subtree | **User decision 2026-11-02: Recharts 3 (MIT).** |
| U5 | Does REA's JS analyzer recover API calls from Angular `$resource`? | Low. Affects only how much REA can add for Redash 8's client | `ev_510cf004…` routes view empty; request family 0 relations | Answered for this scope: **not covered**. Source citations used instead (E-R19) | — | Closed (as a tool limit; it does not show the calls are absent) |
| U6 | Do the Redash docs pages (undated) describe 8.0.0 or current behavior? | Low | Parameters page mentions "Prior to Version 8…" (E-R11) | Open | Treat docs as current-version claims; use source as authoritative for 8.0.0 | — |
| U7 | Exact shape of Redash `options` JSON for a saved chart as persisted by 8.0.0 at runtime | Low. QBE defines its own typed options and does not copy Redash's | E-R15 (defaults only) | Open | Read one `visualizations.options` row during the approved U1 run (synthetic data only) | — |
| U8 | Should the visualization slice also fix D2 (global, unchecked favorites)? | Low | E-Q10 | Open — scope decision | Recommend a separate P2 change, not this slice | — |

## Blocking assessment for the proposed first slice (saved visualizations)

- U3 and U4 resolved by the user on 2026-11-02; spec approved.
- **Not blocking:** U1 (only refines the "observed Redash" section), U2 (later phase), U5–U8.
