# QueryBuilder schema scripts

For hosts whose SQL login has no DDL rights (a common enterprise constraint — see
`QueryBuilderEditorOptions.ApplyMigrations`), these idempotent scripts let a DBA provision or
upgrade QueryBuilder's schema without the app ever running `CREATE TABLE`/`ALTER` itself.

Each script is generated straight from the EF Core migration chain and is **idempotent**: it's
safe to run against a brand-new database, and equally safe to re-run against an already-migrated
one — it checks `__EFMigrationsHistory` and only applies what's missing. That means, unlike a
strictly-versioned "run each release's script in order" scheme, the *latest* script alone covers
both first-time provisioning and every upgrade in between. Only keep the latest one around; older
ones are superseded, not additive.

## Regenerating after a migration change

Whenever a new EF Core migration is added under `QueryBuilder.Infrastructure/Persistence/Migrations`:

1. Bump `<Version>` in `QueryBuilder.Editor.csproj`.
2. Regenerate the script from the repo root:

   ```bash
   dotnet tool run dotnet-ef migrations script --idempotent \
     --project src/QueryBuilder.Infrastructure \
     --startup-project src/QueryBuilder.Api \
     --framework net10.0 \
     --output src/QueryBuilder.Editor/Scripts/QueryBuilder.schema.<version>.sql
   ```
3. Delete the previous version's `.sql` file — it's superseded, not additive (see above).

## What the 2.0.0 script contains (hub mode)

Four migrations were added for hub mode. The script file itself is regenerated when the version is bumped, not before; once
it is, the list below is what it applies:

| Migration | What it does |
|---|---|
| `AddApps` | Creates `Apps` and `MetadataSettings`; adds `AppId` to saved queries, visualizations, shares, data sources, data scope rules, dashboards, dashboard tiles, dashboard shares and the audit log (nullable there); puts every existing row in the built-in Default app. |
| `AddHubRegistry` | Creates `AppSigningKeys`, `AppReturnUrls`, `SeenUsers`, `LaunchReplays` and `DataProtectionKeys`; adds `ScopeKeys` to `Apps`. |
| `AddAppSessionsValidAfter` | Adds `Apps.SessionsValidAfterUtc`. |
| `AddHubRolesAndPublishing` | Adds `Dashboards.PublishedToApp`, `PublishedAtUtc`, `PublishedBy`, `DataSources.LockedBySuperAdmin`, and `Apps.AppAdminsCanManageConnections`, `MaxRecords` and `Version`. |

Things your DBA should know:

- **The `AuditLogEntries` backfill rewrites the whole table.** `AddApps` runs one `UPDATE` setting `AppId` on every existing audit
  row, so a large audit log takes time and transaction log. Run it in a quiet window.
- **The `AppId` columns keep a named database default** (`DF_<Table>_AppId`, the Default app) on purpose: raw-SQL data source
  registration, rolling back to 1.5.0 and mixed versions keep working. Do not drop them.
- **Grant `INSERT` on `MetadataSettings`** to the application login, or insert the mode row yourself once:
  `INSERT INTO MetadataSettings ([Key], [Value]) VALUES ('Mode', 'Embedded')` (`'Hub'` for a hub). Without it an embedded host
  only logs a warning, and a hub refuses to start.
- **A hub needs its own empty database.** Never run a hub against a database an embedded host uses; the mode marker makes the
  hub refuse it.
- A hub login needs `SELECT`, `INSERT`, `UPDATE` and `DELETE` on every QueryBuilder table, including `LaunchReplays` and
  `DataProtectionKeys`. Set `Hub:ApplyMigrations` to `false` in the hub host (or `options.ApplyMigrations = false` in your own).

## Usage

```csharp
builder.Services.AddQueryBuilderEditor(options =>
{
    options.ConnectionString = connectionString;
    options.ApplyMigrations = false; // never attempt DDL — the app login only needs DML rights
});
```

Have your DBA run the current `QueryBuilder.schema.<version>.sql` against the target database
(via SSMS, sqlcmd, or your normal change-control process) before the app first starts, and again after
each upgrade.

With `sqlcmd`, **pass `-I`**. It turns on `QUOTED_IDENTIFIER`, which the script's filtered indexes need;
without it the script fails with a "SET options have incorrect settings" error. SSMS has this on by default.

```bash
sqlcmd -S <server> -d <database> -I -b -i QueryBuilder.schema.<version>.sql
```
