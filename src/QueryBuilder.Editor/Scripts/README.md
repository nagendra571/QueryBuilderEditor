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
     --output src/QueryBuilder.Editor/Scripts/QueryBuilder.schema.<version>.sql
   ```
3. Delete the previous version's `.sql` file — it's superseded, not additive (see above).

## Usage

```csharp
builder.Services.AddQueryBuilderEditor(options =>
{
    options.ConnectionString = connectionString;
    options.ApplyMigrations = false; // never attempt DDL — the app login only needs DML rights
});
```

Have your DBA run the current `QueryBuilder.schema.<version>.sql` against the target database
(via SSMS, sqlcmd, or your normal change-control process) before the app first starts.
