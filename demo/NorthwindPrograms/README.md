# Northwind Programs (demo "App A")

A made-up business app that plays the host application for a public QueryBuilder Hub demo. A visitor picks a person in the
header, browses Home, Enrolments and Budgets, then clicks **Open reports in QueryBuilder** (or the **Reports** menu item).
The app signs a launch token with the `QueryBuilder.Launch` NuGet package and posts the visitor to the hub.

There is no real sign-in, on purpose: the switcher is the identity. Role and scope come only from the fixed list in
`Program.cs`; the browser sends nothing but the person's key, which is checked against that list.

| Person | Role | Scope |
|---|---|---|
| Alex Morgan | Admin | everything |
| Mary Chen | Admin | ProgramId 10 |
| Raj Patel | Admin | ProgramId 20 |
| Lee Brooks | Viewer | ProgramId 10 |

The user id in the token is the person's email.

## Settings

Set them in `appsettings.json` (placeholders only), user secrets, or environment variables (`Demo__HubUrl` and so on; on
Azure App Service, application settings with the same names).

| Setting | Meaning |
|---|---|
| `Demo:HubUrl` | The hub's address: scheme and host (and port), no trailing slash, no path. |
| `Demo:AppKey` | The key the hub registered this app under. Default `northwind`. |
| `Demo:KeyId` | The `kid` of the signing key as registered in the hub. |
| `Demo:PrivateKeyPem` | The PEM text of the RSA private key (PKCS#1 or PKCS#8). Secret. |
| `Demo:ReturnUrl` | Optional. Must be registered in the hub and canonical, for example `https://my-demo-app.azurewebsites.net/`. Adds "Back to your app" in the hub. |
| `ConnectionStrings:Programs` | SQL Server connection string for the sample data (`demo/sql/programs.sql`). |
| `ConnectionStrings:HubReset` | Optional. Connection string to the **hub** database as the `demo_reset` user (see `demo/sql/reset-setup.sql`). When set, the footer shows **Reset demo**; when empty, the button is hidden. |

If the key or hub address is missing the app still starts, and the Reports button shows "Demo not configured".

The free Azure SQL database pauses itself and can take about a minute to resume. Put `Connect Timeout=120` in the
connection string (commands also wait up to 120 seconds). While it wakes up, pages show a friendly message instead of an error.

`GET /healthz` answers `ok` without touching the database, so warm-up pings do not wake it.

## Run locally

```powershell
cd demo\NorthwindPrograms
dotnet user-secrets set "Demo:HubUrl" "https://localhost:7300"
dotnet user-secrets set "Demo:KeyId" "demo-2026-11"
dotnet user-secrets set "Demo:PrivateKeyPem" (Get-Content C:\path\to\private.pem -Raw)
dotnet user-secrets set "ConnectionStrings:Programs" "Server=(localdb)\MSSQLLocalDB;Database=QueryBuilderDemoSource;Integrated Security=True;TrustServerCertificate=True"
dotnet run --urls http://localhost:5090
```

Load the sample data first by running `demo/sql/programs.sql` against the database.

## Keys and hub registration

Make an RSA key pair as described in `src/QueryBuilder.Launch/README.md` section 2. Keep the private key out of git. Then,
in the hub console, register the app (key `northwind`), its public key with the same `kid` as `Demo:KeyId`, the scope key
`ProgramId`, and the return URL if you set one (section 3 of the same README).
