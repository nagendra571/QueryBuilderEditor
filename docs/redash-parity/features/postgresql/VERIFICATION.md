# PostgreSQL data sources: verification

Run 2026-11-06 on the uncommitted `feature/rea1` working tree (bundle `index-CXBS3rAW.js`), against `SPEC.md`.

## Setup

- PostgreSQL 16.14 in a temporary local container, reached by a restricted login.
- Metadata store and the SQL Server source on throwaway LocalDB databases, collation `SQL_Latin1_General_CP1_CI_AS`.
- A scratch host built from a packed `QueryBuilder.Editor 1.2.0-test`, run on .NET 10 (Npgsql 10.0.3) and .NET 8 (Npgsql 9.0.5), both sharing one metadata database, with one Postgres source, one SQL Server source, a source with a malformed connection string and a MySQL row.
- Data sources were registered with the README's `INSERT`, unchanged.
- All of this was removed afterward.

## Results

| Check | .NET 10 | .NET 8 |
|---|---|---|
| `dotnet test` with `QB_TEST_POSTGRES` and `QB_TEST_METADATA_SQLSERVER` set | 820 passed, 0 skipped | 820 passed, 0 skipped |
| API acceptance script from the packed package | 124 of 124 | 124 of 124 |
| Chart and dashboard script | 34 of 34 | 34 of 34 |
| Browser checks | All pass | All pass |

Client tests: 143 passed. The browser click-through of building and running a query ran on .NET 10 only.

## Acceptance criteria

| AC | Result | Evidence |
|---|---|---|
| AC-1, 1a | Pass | Both clash kinds show disabled for unrestricted users. Run, export, preview, save and scoped run all refuse with the not-found error, same shape as a nonexistent object. Chart render and dropdowns can't name an object outside the catalog, by construction |
| AC-1b | Pass | A clash appearing after a "not scoped" rule stops reads. Saving scope and policy succeeds in that state. Dropping the clash restores access without re-deciding |
| AC-1c | Pass | Scoped catalog has no unavailable objects or names, including through a foreign key to a clashing table. Sidebar shows no count |
| AC-1d | Pass | No columns or partner names in business responses. Admin pages name the partner |
| AC-1e | Pass | Kind filter and allowlist remove them from the builder, not from admin pages |
| AC-1f | Pass, one limit | Reason text contrast 6.6:1 light, 6.82:1 dark. Name and reason are in the accessibility tree. **Checked with an accessibility snapshot, not a real screen reader.** SQL Server sources return an empty list |
| AC-2 | Pass | Scope on `CustomerId` filters that column when `customerid` also exists |
| AC-3 | Pass | Newly allowlisting an unavailable object and wrong-case entries are refused |
| AC-3a | Pass | `MetadataCollationTests` against a `_CI_` database: 400, earlier rules kept, case preserved |
| AC-3b | Pass | Save refused for a nondeterministic scope column. Integration tests cover plain, domain and view columns |
| AC-4 | Pass | Scope tests mirrored on the Postgres dialect, and an OR filter can't bypass scope against the real database |
| AC-5 | Pass | A writing view is refused with the message, and no row is written |
| AC-6 | Pass | Hostile filter and parameter values stay bound, and the table survives |
| AC-7 | Pass | Golden SQL tests unchanged since before the refactor |
| AC-8 | Pass | SQL Server run, export, contains, case-insensitive names, charts, app users, scoped persona |
| AC-9 | Pass | Kinds, types, keys; partition child, ungranted and system objects hidden |
| AC-10, 10a | Pass | 137 operator cases in integration tests, plus backslash and `%` cases and Contains on a nondeterministic column, against the real database |
| AC-11, 11a | Pass | Every odd value runs and exports to CSV and Excel. `infinity`, BC and year 12000 stay exact. No `9999` |
| AC-11b | Pass | 40-digit and long-fraction values show exactly in the grid and table tile, and plot in a bar chart |
| AC-12 | Pass | An enum created while the host runs reads after the catalog refresh |
| AC-13 | Pass, one difference | Date values are identical on both targets and both databases, and charts look the same. A Postgres `date` column reports type `date`, where SQL Server reports `dateTime` |
| AC-14 | Pass | Totals, HAVING, sort, record limit with truncation, refused export |
| AC-15 | Pass | All 13 chart types render. One dashboard mixes Postgres and SQL Server tiles (17 tiles, no failures, no console errors) |
| AC-16 | Pass | Empty, multi-select, number, text, date range, relative date, boolean and query-based dropdowns |
| AC-17 | Pass | Stops at about 61 s with the message, server query gone. Aborting the request cancels it |
| AC-18 | Pass | 64 bytes refused, 63 works, 32 `é` refused |
| AC-19 | Pass by API | `public.app_users` lists users, absence reports unavailable, sharing a query works, and the scoped viewer's chart shows only their rows. **The sharing dialog itself wasn't clicked** |
| AC-20 | Pass | Malformed string gives the connect message, without the host |
| AC-21 | Pass | Packed package ran both targets |

Review Focus: type modifiers and non-English culture are covered by parser tests; unavailable-only schemas and name search by browser and client tests; a MySQL row gives a clear 400; a query naming an unavailable object gets the not-found error.

## Not verified

- **PgBouncer.** Untested, as the spec says.
- **PostgreSQL versions other than 16.** The README states 12 as the minimum, from reading which catalog columns are used. It wasn't run on 12.
- **SSL connections, and cloud-hosted Postgres.**
- **`TemplateBuilderClient` and the hosted site.** They haven't moved to this code.
- **A real screen reader.**
- **Tiles left on a saved query whose object became unavailable, in a browser.** The refusal is confirmed by API.

## Issues found and left as they are

| Issue | Effect |
|---|---|
| A chart holding a 40-digit value repeats `1,000,000T` on its axis | The bar is right, the axis labels aren't useful |
| An unavailable-only schema header shows `0` | Cosmetic |
| The clash warning logs again on every admin read | Log noise |
| .NET 8 logs handled refusals as errors, where .NET 10 doesn't | Log noise; the responses are correct |
| A 64-byte alias saves, then each run returns a clear 400 | Refused at run rather than at save |
| No warning on a rule whose column later became nondeterministic | Shows "Scoped" while scoped users can't see the object; fails closed |
| `ParameterTests.cs:616-631` doesn't set the Postgres provider | The behavior was checked here by API, but that test wouldn't catch a regression |
| A SQL Server number filter too long for `decimal` is still a 500 | Existing behavior, kept on purpose |

## After acceptance: `Pkcs` advisories fixed

`System.Security.Cryptography.Pkcs` is now pinned to 8.0.2 on `net8.0` and 10.0.14 on `net10.0`, because `Microsoft.Data.SqlClient` through 7.1.1 still brings the vulnerable versions.

- `dotnet test` with both database variables set: 820 passed, 0 skipped, on each target.
- A throwaway consumer of a packed test package, restored with NuGet audit at its strictest (all packages, all severities, nuget.org advisories): no vulnerable packages.
- The package's dependency list carries the pin for each target.

The browser and API acceptance scripts above weren't rerun for this dependency-only change.
