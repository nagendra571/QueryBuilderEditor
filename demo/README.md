# QueryBuilder Hub demo

A public demo of QueryBuilder Hub: a made-up business app, **Northwind Programs**, hands its users over to the hub with a
signed launch token, and each user sees only the rows their program allows. Everything shown is made-up data.

| What | Address |
|---|---|
| Northwind Programs (where prospects start) | https://qb-demo-northwind.azurewebsites.net/ |
| QueryBuilder Hub | https://qb-demo-hub.azurewebsites.net/querybuilder/ |
| Super admin sign-in (video only) | https://qb-demo-hub.azurewebsites.net/querybuilder/signin |

**First visit after a quiet spell can take up to a minute**: the free hosting puts the apps and databases to sleep and wakes
them on the first request. Wait for the page; don't refresh repeatedly.

## The four people

Pick one with the **Signed in as** switcher (top right of Northwind Programs), then **Open reports in QueryBuilder →**.

| Person | Role in the hub | Rows | What they should see |
|---|---|---|---|
| Alex Morgan | Admin | all programs | Queries, Dashboards and Admin. "Enrolments by program" shows 5 programs, 100 enrolments. |
| Mary Chen | Admin | program 10 | The same queries and dashboard, but every chart shows only Youth Coding Academy (20 enrolments). Badge "ZONE · ProgramId 10". |
| Raj Patel | Admin | program 20 | Same as Mary, but only Adult Literacy (20 enrolments). |
| Lee Brooks | Viewer | program 10 | Only **Dashboards**: the published "Program overview", "Program performance" and "Funnel and outcomes", drawn for program 10 only. No queries, no SQL. |

The Northwind screens (Enrolments, Budgets) show the same rows the hub will show for that person, so you can compare.
"Back to Northwind Programs" in the hub returns to the app; switching person and clicking the button again replaces the hub
session.

## The five scenes (for the video)

1. **Super admin sets up an app (video only).**
   1. Get a one-time sign-in code (below) and sign in at `/querybuilder/signin`.
   2. Open **Apps**: walk through **Northwind Programs** (app key `northwind`, scope key `ProgramId`, record ceiling,
      signing key `demo-2026-11` and its fingerprint, return URL, turned on) and its locked data source.
   3. Choose **New app** and register **App B** from scratch (name, key such as `app-b`, a scope key). Leave it turned off;
      it is never launched.
2. **Alex** (Admin, all programs): open a query, run it, open the Program overview dashboard: every program.
3. **Mary** (Admin, program 10): same screens, only program 10. The SQL preview shows the scope filter.
4. **Raj** (Admin, program 20): same screens, only program 20.
5. **Lee** (Viewer, program 10): only the published dashboard, only program 10.

Prospects never get super admin; they start at Northwind Programs.

**Recording scene 1 again.** App B stays in the console after a take, and the hub has no "delete app" screen. Remove it with
`demo/sql/remove-app.sql` on the hub database (as the database admin), first to look, then to remove:

```
sqlcmd -S tcp:qb-demo-sql.database.windows.net,1433 -U qbadmin -d hub -I -b -v AppKey="app-b" Confirm="no"  -i demo/sql/remove-app.sql
sqlcmd -S tcp:qb-demo-sql.database.windows.net,1433 -U qbadmin -d hub -I -b -v AppKey="app-b" Confirm="yes" -i demo/sql/remove-app.sql
```

Use the key App B was registered with. It removes the app with its keys, return URLs, any data sources, content and its
audit entries, so the same key can be registered again. It refuses `northwind`, the built-in Default app, and any app someone
has launched into. Your PC's address must be allowed by the SQL server's firewall (Azure portal, `qb-demo-sql`, Networking),
and the admin password can be reset there if lost.

## Getting a super admin sign-in code

Codes are made on the server, never on a web page.

1. Azure portal, the **qb-demo-hub** App Service, **Advanced Tools** (Kudu), **Go**, then **Debug console** > **CMD**.
2. `cd site\wwwroot`
3. `dotnet QueryBuilder.Hub.dll signin-code --name video --minutes 60`
4. It prints `Code: …` and `Expires: …` (UTC). The code works **once**, for up to 60 minutes; the session it opens lasts up
   to 8 hours (1 hour idle). Treat it like a password: give it only to whoever records the video.

## What prospects can change

The starting set has three published dashboards: **Program overview** (bar, pie, stacked bar, table), **Program performance** (counter with target, trend, line, stacked area, combo, horizontal bar) and **Funnel and outcomes** (funnel, waterfall, scatter/bubble, pivot with shading, ranked table). Their data comes from `demo/sql/programs.sql` and `demo/sql/programs-showcase.sql`.

Prospect Admins can edit, delete, share or unpublish the starting queries and dashboards. The data source is locked (only a
super admin can change it), so they cannot break the connection or the program scoping.

**Reset demo** (bottom right of every Northwind page, then **Reset the demo**) puts the starting set back for everyone: the four
queries, their charts and the published Program overview dashboard, with their original ids; anything visitors created, shared
or unpublished goes away. It never touches the app's registration, keys, data source, scope rules or App B. Press it before
recording or before sending the link to a prospect. Visitors who had QueryBuilder open must reload.

How it works: `demo/sql/reset-setup.sql` (run once on the hub database after `demo/setup/setup.mjs`) saves the starting set in a
`demo` schema and adds a procedure `demo.ResetNorthwind`; the Northwind app runs it as the database user `demo_reset`, which may
execute that procedure and nothing else (setting `ConnectionStrings__HubReset`). To change the starting set on purpose, change it
in QueryBuilder and then run `EXEC demo.CaptureNorthwindBaseline` as the database admin.

## How it is hosted (all Azure free offers)

Resource group `qb-demo` (West US 3): free App Service plan (F1) with the two apps, Azure SQL server `qb-demo-sql` with two
free-offer databases, `hub` (the hub's own data) and `programs` (the sample data), both set to **pause** when the monthly free
amount runs out, never to bill. Secrets live only in the apps' Azure settings. The subscription is a free trial: it is
disabled when the trial ends unless upgraded to pay-as-you-go.

To remove everything: `az group delete -n qb-demo`.

## More to read

- [Hub setup guide](../docs/hub-setup-guide.md)
- [QueryBuilder.Launch guide](../src/QueryBuilder.Launch/README.md) (how an app signs users in to the hub)
- [Hub host guide](../src/QueryBuilder.Hub/README.md)
- [Product README](../README.md)
- The simulator: [`NorthwindPrograms/README.md`](NorthwindPrograms/README.md); the setup script: [`setup/README.md`](setup/README.md);
  the sample data: [`sql/programs.sql`](sql/programs.sql).
