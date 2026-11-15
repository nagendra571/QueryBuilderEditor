# Demo setup script

`setup.mjs` registers the "Northwind Programs" app in a QueryBuilder Hub, sets up its data source policy, and creates the
starting set (four queries with charts and a published "Program overview" dashboard). Node 20+, no npm packages.
Safe to run again: it finds things by name or key and updates them instead of duplicating.

```
node demo/setup/setup.mjs              # everything
node demo/setup/setup.mjs --app-only   # register the app and its key, print the app id, stop
node demo/setup/setup.mjs --verify     # also launch as Mary, Raj and Lee and check what each one sees
```

Nothing secret is printed. Never commit the files the variables point to.

## Inputs (environment variables)

| Variable | Meaning |
|---|---|
| `QB_HUB` | The hub's address, exactly as `Hub:Audience` (for example `https://localhost:7300`; no trailing slash, no path). |
| `QB_CODE_FILE` | A file holding a line `Code: <code>` from `QueryBuilder.Hub signin-code --name <name>`. A code works **once**, so each run needs a fresh one (the script signs in once and reuses the session). |
| `QB_APP_PUBLIC_PEM_FILE` | The app's launch public key (SPKI PEM); registered on the app if `QB_KEY_ID` isn't there yet. |
| `QB_APP_PRIVATE_PEM_FILE` | The matching private key (PKCS#8 PEM). Used to sign launch tokens as Alex (and Mary, Raj, Lee for `--verify`). |
| `QB_KEY_ID` | The key id (`kid`), for example `demo-2026-11`. A revoked id is refused: use a new one. |
| `QB_RETURN_URL` | Optional. Set as the app's only return URL (https, or http only for localhost). |
| `QB_DS_MODE` | `existing` (default) or `managed`. |
| `QB_DS_HOST`, `QB_DS_PORT`, `QB_DS_DATABASE`, `QB_DS_USER`, `QB_DS_PASSWORD` | `managed` only: the connection the super admin saves as an encrypted data source (SQL Server, encryption required). Use a read-only SQL user. |

Make a throwaway key pair with Node:

```js
import { generateKeyPairSync } from 'node:crypto'
const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 2048,
  publicKeyEncoding: { type: 'spki', format: 'pem' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } })
```

## What it does

1. Signs in as super admin with the code.
2. Creates the app `northwind` ("Northwind Programs") if missing; sets scope keys `["ProgramId"]`, record ceiling 5000, "app admins can add data sources" off, turned on; adds the signing key and the return URL.
3. Switches into the app.
4. Data source "Northwind Programs": `managed` creates it (or re-applies the connection) through the admin endpoint, so it is locked; `existing` expects a row with that name already in the app. It checks the source is locked and locks it if not.
5. Catalog policy: views only, allowlist `sales.vw_ProgramEnrollments`, `vw_ProgramSummary`, `vw_ProgramBudgets`, `vw_Programs`; record limit 5000; scope rules (Enrollments and Summary `ProgramId` to `ProgramId`, Budgets `ProgramId` to `OwningProgramId`, `vw_Programs` not scoped).
6. Launches as Alex (Admin, unrestricted) and creates the queries, charts and dashboard, publishes the dashboard, and draws each chart once.
7. `--verify`: Mary (ProgramId 10, Admin), Raj (20, Admin), Lee (10, Viewer) each launch; their chart data, Lee's dashboard tiles and Lee's refusals are checked.

## "existing" mode: the raw-SQL data source row

Run `--app-only` first to get the app id, then (as a DBA, on the hub's metadata database) insert the row with an explicit `AppId`
(see `docs/hub-setup-guide.md`, D1 step 6), with `ConnectionStringName` naming an environment variable `ConnectionStrings__<name>` on the hub:

```sql
INSERT INTO DataSources (Id, AppId, Name, Provider, ConnectionStringName, AllowedSchemas, AllowedObjects, CatalogScope, ScopeKeys,
                         IsActive, LockedBySuperAdmin, CreatedBy, CreatedAtUtc)
VALUES (NEWID(), '<app id>', 'Northwind Programs', 0, 'Programs', 'sales', '', 0, '', 1, 1, 'dba', SYSUTCDATETIME());
```

Then run the script again without `--app-only`.

To enable the **Reset demo** button, run `demo/sql/reset-setup.sql` on the hub database once the starting set exists (it saves
that set and creates the `demo_reset` user), then give the Northwind app `ConnectionStrings__HubReset`. See `demo/README.md`.

## Local runs and the development certificate

A local hub uses a self-signed development certificate. If Node says `self-signed certificate`, run with
`NODE_OPTIONS=--use-system-ca` (works when the dev certificate is trusted by Windows; this is what the local run used), or point
`NODE_EXTRA_CA_CERTS` at the certificate. As a last resort, and only for `localhost`, `NODE_TLS_REJECT_UNAUTHORIZED=0`.
Never use that against a public hub.

## Example (local)

```powershell
$env:NODE_OPTIONS = '--use-system-ca'
$env:QB_HUB = 'https://localhost:7300'
$env:QB_CODE_FILE = 'C:\path\to\code.txt'            # contains: Code: <code>
$env:QB_APP_PUBLIC_PEM_FILE = 'C:\path\to\app-public.pem'
$env:QB_APP_PRIVATE_PEM_FILE = 'C:\path\to\app-private.pem'
$env:QB_KEY_ID = 'demo-local-1'
$env:QB_RETURN_URL = 'http://localhost:5090/'
$env:QB_DS_MODE = 'existing'
node demo/setup/setup.mjs --verify
```
