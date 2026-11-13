# QueryBuilder Hub: setup guide

This guide takes you from nothing to a working QueryBuilder hub with one app connected. Follow the parts in order the
first time. Every step says what to do and how to check it worked.

Short references for each piece, if you need a full table later:

- [`src/QueryBuilder.Hub/README.md`](../src/QueryBuilder.Hub/README.md): the ready-made hub host (all settings, troubleshooting).
- [`src/QueryBuilder.Launch/README.md`](../src/QueryBuilder.Launch/README.md): how an app sends a user to the hub (token claims, limits, refusal reasons).
- [`src/QueryBuilder.Editor/README.md`](../src/QueryBuilder.Editor/README.md), section "Hub mode (2.0.0)": the package side.
- [`MANAGING-KEYS.md`](../MANAGING-KEYS.md): the data source key and how to change it.
- [`src/QueryBuilder.Editor/Scripts/README.md`](../src/QueryBuilder.Editor/Scripts/README.md): the schema script for DBAs.

Hub mode is part of QueryBuilder 2.0.0. Names, defaults and limits below are those of the code in this repository.

## 0. Overview

### The three pieces

```
 Agency app ("App A")                         The hub                      Your staff
 -------------------                    ---------------------------     ---------------
 user is already signed in                one QueryBuilder for            super admins
  |                                       many apps                        |
  | 1. user clicks "Go to QueryBuilder"   - its own empty SQL Server DB    | sign in with your
  | 2. App A's server signs a short-lived - one data source key            | identity provider
  |    token (private key stays in App A) - HTTPS only                     | and/or a one-time
  v                                                                        | code
 browser posts the token  ------------->  POST /querybuilder/launch        v
                                          checks it with App A's PUBLIC   GET /querybuilder/signin
                                          key, starts a session cookie,
                                          redirects to /querybuilder/
```

1. **The agency apps.** Each app (a "tenant") signs its own users in. When a user wants QueryBuilder, the app's server
   makes a signed launch token that says who the user is, which role they have and which rows they may see. The app uses the
   small `QueryBuilder.Launch` NuGet package for this (or any JWT library).
2. **The hub.** One QueryBuilder that serves all the apps. It only ever stores each app's **public** key.
3. **Where the hub runs.** Pick one:
   - **The ready-made hub:** `src/QueryBuilder.Hub` in this repository. It is an ASP.NET Core application (`net10.0`) you
     publish and deploy. It is not a NuGet package. Part A.
   - **A hub you build yourself** on the `QueryBuilder.Editor` NuGet package, in your own ASP.NET Core host. Part B.

### Roles

| Role | How a person gets it | What they can do |
|---|---|---|
| **Super admin** | Your own staff. Signs in at `/querybuilder/signin` with your identity provider and/or a one-time code, and must be on the allowlist. A launch token can never make a super admin. | Everything in every app: the app registry (apps, signing keys, return URLs, scope keys, record ceiling), data sources (adding, locking, moving), the data source key, the hub audit log. No row restriction. |
| **Admin** | The `role` claim of the launch token is `Admin`. | Everything inside their own app: sees and manages all queries, dashboards and shares, publishes dashboards, uses the Admin page for unlocked data sources. |
| **Author** | `role` is `Author`. | Builds, saves, charts, runs, exports and shares queries; builds dashboards. |
| **Viewer** | `role` is `Viewer`. | Dashboards only: the ones published to the app and those shared with them. No query builder, no SQL, no query export; they can download the data behind a dashboard chart (CSV or Excel, audited). |

A role comes from the latest launch only. It is not stored as a permanent grant: the app decides it again on every click.

### Which path should I take?

> **Use the ready-made hub (Part A) unless you need your own host.** You need your own host (Part B) only if you must
> add your own middleware, use your own authentication stack, or your hosting has constraints the ready-made host cannot meet.
> A hub you build yourself carries every security job the ready-made host does for you (HTTPS, forwarded headers, rate limits,
> security headers). Part B lists them.

Everyone, whichever path, also does Part C (connect each app) and Part D (first day).

## 1. Before you start

Tick these off first.

- [ ] **SQL Server for the hub's metadata.** One **empty** database used only by the hub. Never one that an embedded
      QueryBuilder uses: the hub refuses a database that already holds QueryBuilder data without a recorded mode. Know the
      connection string and make a SQL login or identity that can read and write it. If the hub is to create the tables itself
      (the default), that login also needs the rights to create and alter tables on the first start and on each upgrade
      (`db_ddladmin`, with `db_datareader` and `db_datawriter`). If your DBA runs the schema script instead, see step A2. A
      managed identity (`Authentication=Active Directory Managed Identity`) works too; A2 shows how.
- [ ] **A public https address** for the hub, for example `https://hub.example.com`, with a certificate. The hub must be
      served at the root of its own host name. It cannot live under a path such as `/tools`. HTTPS is required: the session cookie
      is named `__Host-qb-hub` and browsers refuse such a cookie unless it is `Secure`.
- [ ] **A data source key**: 32 random bytes as base64. Make it now.

  Windows PowerShell 5.1 or PowerShell 7:

  ```powershell
  $b = New-Object byte[] 32
  [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
  [Convert]::ToBase64String($b)
  ```

  Linux, macOS or Git Bash:

  ```bash
  openssl rand -base64 32
  ```

  The result is 44 characters ending in `=`. Save it in a password manager immediately. If it is lost, stored data source
  passwords cannot be read again. Never put it in the repository or in `appsettings.json`.
- [ ] **A decision on how super admins sign in.** Choose one or both:
  - an **identity provider** (Microsoft Entra ID, or any OpenID Connect provider), or
  - **one-time sign-in codes** made on the server by an operator.

  The hub refuses to start with neither. If you do not have an identity provider yet, start with codes only and add the
  provider later.
- [ ] **Who holds which secret.**

  | Secret | Held by | Where it goes |
  |---|---|---|
  | Data source key (`Hub:DataSourceKey`) | The hub operator | Environment variable or a server-only secret store. A copy in a password manager. |
  | Identity provider client secret | The hub operator | Environment variable or server-only secret store. Note its expiry date. |
  | Hub database connection string | The hub operator | Environment variable or server-only secret store. |
  | An app's **private** signing key | The app owner only | The app's key store or certificate store. It never goes to the hub operator. |
  | An app's **public** key | The app owner gives it to a super admin | Pasted into the hub console. Not secret. |

> **Warning: with sign-in codes on, the data source key is a super admin credential.** Anyone who holds it can create a valid
> sign-in code for any name in `Hub:SuperAdmins:CodeNames`. Treat it like an admin password, and use a **different key for each
> deployment** (staging and production must not share one).

## 2. Part A: deploy the ready-made hub (`QueryBuilder.Hub`)

### A1. Build and publish

You need the .NET 10 SDK on the build machine and the .NET 10 ASP.NET Core runtime on the server.

```powershell
git clone https://github.com/nagendra571/QueryBuilderEditor.git
cd QueryBuilderEditor
git checkout feature/hub
dotnet publish src/QueryBuilder.Hub -c Release -o ./publish/hub
```

(Use the branch or tag that your release process names. `feature/hub` is the branch this guide was written on.)

The folder `./publish/hub` holds `QueryBuilder.Hub.dll` (and `QueryBuilder.Hub.exe` on Windows). Copy it to the server.
You can test it by starting it by hand once the settings in A3 are in place:

```bash
dotnet QueryBuilder.Hub.dll --urls https://localhost:5001
```

### A2. The database

1. Create an **empty** database, for example `QueryBuilderHub`. Use it for nothing else.
2. Create a login or identity for the hub in that database (see "How the hub signs in to SQL Server" below).
3. Choose who creates the tables, and grant the roles that option needs:

**Option 1: the hub does it (default).** Leave `Hub:ApplyMigrations` at `true`. On first start the hub creates the tables
and records `MetadataSettings` `Mode` = `Hub`, which claims the database. The login needs the rights to create and alter
tables for that start, and again for each upgrade. Without them the first start fails with
`CREATE TABLE permission denied in database '...'`. Grant:

```sql
ALTER ROLE db_ddladmin   ADD MEMBER [<hub login or identity>];  -- create and alter tables (first start, upgrades)
ALTER ROLE db_datareader ADD MEMBER [<hub login or identity>];
ALTER ROLE db_datawriter ADD MEMBER [<hub login or identity>];
```

Keep `db_ddladmin`: later versions that add tables or columns upgrade themselves the same way.

**Option 2: your DBA does it.** Set `Hub:ApplyMigrations` to `false` (the hub then never runs DDL).

1. The DBA runs the idempotent schema script `QueryBuilder.schema.<version>.sql` from
   [`src/QueryBuilder.Editor/Scripts`](../src/QueryBuilder.Editor/Scripts/README.md) against the empty database. With `sqlcmd`,
   pass `-I`, or the filtered indexes fail with a "SET options have incorrect settings" error:

   ```bash
   sqlcmd -S <server> -d <database> -I -b -i QueryBuilder.schema.<version>.sql
   ```

   (Use `QueryBuilder.schema.2.0.0.sql`, from `src/QueryBuilder.Editor/Scripts` or the `scripts/` folder of the
   QueryBuilder.Editor 2.0.0 package.)
2. The DBA inserts the mode marker once, and turns the built-in Default app off, because the hub does that itself only
   when it claims a database and an existing marker skips that step:

   ```sql
   INSERT INTO MetadataSettings ([Key], [Value]) VALUES ('Mode', 'Hub');
   UPDATE Apps SET IsEnabled = 0 WHERE Id = '00000000-0000-0000-0000-00000000000d';
   ```
3. The hub's login needs `SELECT`, `INSERT`, `UPDATE` and `DELETE` on every QueryBuilder table, including `MetadataSettings`,
   `LaunchReplays` and `DataProtectionKeys`. (If you do not grant `INSERT` on `MetadataSettings`, the DBA must do the insert above, and the hub
   then only reads it.) The two data roles cover all of it:

   ```sql
   ALTER ROLE db_datareader ADD MEMBER [<hub login or identity>];
   ALTER ROLE db_datawriter ADD MEMBER [<hub login or identity>];
   ```
4. Repeat the script before each upgrade.

If the database already holds QueryBuilder data from an embedded app, the hub refuses to start ("needs its own empty
metadata database"). Give it a new database. A failed first start (for example a permission error) leaves nothing to clean up.

**How the hub signs in to SQL Server.** `Hub:ConnectionString` decides it:

| Sign-in | Connection string | Who the roles above go to |
|---|---|---|
| SQL login | `Server=tcp:<server>,1433;Database=QueryBuilderHub;User Id=<login>;Password=<password>;Encrypt=True` | `CREATE USER [<login>] FOR LOGIN [<login>];` first |
| Entra managed identity (Azure App Service, VM) | `Server=tcp:<server>.database.windows.net,1433;Database=QueryBuilderHub;Authentication=Active Directory Managed Identity;Encrypt=True` (add `User Id=<client id>` for a user-assigned identity) | An Entra admin of the database runs `CREATE USER [<app or identity name>] FROM EXTERNAL PROVIDER;` first |
| Windows authentication (IIS on a domain) | `Server=<server>;Database=QueryBuilderHub;Integrated Security=True;Encrypt=True` | The app pool identity: `IIS APPPOOL\<pool>` on the same machine, or the pool's domain account |

The ready-made hub includes `Microsoft.Data.SqlClient.Extensions.Azure`, which every `Authentication=Active Directory ...`
method needs since SqlClient 7. A host of your own needs it too (Part B).

### A3. Settings

Settings live in the `Hub` section of configuration. The `appsettings.json` that ships with the host holds empty
placeholders only. Put real values in environment variables (A4) or a file that exists only on the server. A double underscore
in an environment variable name stands for `:`.

| Setting | Required | Default | What it does |
|---|---|---|---|
| `Hub:ConnectionString` | yes | none | The hub's own SQL Server database. |
| `Hub:Audience` | yes | none | The hub's public https address: scheme, host and optional port only. No path, query, fragment or trailing slash. Every launch token's `aud` must equal it exactly. |
| `Hub:SignIn:Provider:Kind` | one of provider or codes | empty | Empty (codes only), `Entra` or `OpenIdConnect`. Exactly that spelling and case. |
| `Hub:SignIn:Provider:DisplayName` | no | `your organisation` | Text of the sign-in button ("Sign in with ..."). |
| `Hub:SignIn:Provider:ClientId` | with a `Kind` | none | Application (client) ID. |
| `Hub:SignIn:Provider:ClientSecret` | with a `Kind` | none | Client secret. Environment variable or server-only file only. |
| `Hub:SignIn:Provider:TenantId` | `Entra` | none | One tenant's id. `common`, `organizations` and `consumers` are refused. |
| `Hub:SignIn:Provider:Instance` | no | `https://login.microsoftonline.com/` | `Entra` only. Absolute https address. Change only for a sovereign cloud. |
| `Hub:SignIn:Provider:Authority` | `OpenIdConnect` | none | The provider's issuer address, absolute https. |
| `Hub:SignIn:Provider:CallbackPath` | no | `/signin-oidc` | Must match the registered redirect URI's path. |
| `Hub:SignIn:Provider:Scopes` | no | empty (`openid`, `profile`, `email`) | If you list scopes, `openid` must be one. |
| `Hub:SignIn:Provider:GroupsClaim` | no | `groups` | `OpenIdConnect` only: the claim that carries group ids. |
| `Hub:SignIn:Codes:Enabled` | no | `false` | Turns on one-time sign-in codes. Needs at least one `Hub:SuperAdmins:CodeNames`. |
| `Hub:SignIn:Codes:DefaultMinutes` | no | `15` | Life of a new code when the command does not say. 1 to 60. |
| `Hub:SignIn:Codes:PermitLimit` | no | `10` | Code attempts per client address per window. At least 1. |
| `Hub:SignIn:Codes:WindowSeconds` | no | `60` | Window length. At least 1. |
| `Hub:SuperAdmins:TenantIds` | `Entra` | none | Entra tenant ids super admins may sign in from. At least one. Ignored (with a warning) for `OpenIdConnect`. |
| `Hub:SuperAdmins:ObjectIds` | `Entra`: one of ObjectIds, Emails, GroupIds | none | Entra object ids (`oid` claim). The most reliable choice. |
| `Hub:SuperAdmins:UserIds` | `OpenIdConnect`: one of UserIds, Emails, GroupIds | none | The provider's `sub` values. The most reliable choice. |
| `Hub:SuperAdmins:Emails` | one of the lists above | none | Emails, compared ignoring case. With `OpenIdConnect`, only when the provider reports `email_verified`. |
| `Hub:SuperAdmins:GroupIds` | one of the lists above | none | Group ids from the groups claim. |
| `Hub:SuperAdmins:CodeNames` | with codes on | none | Names a code may be issued for, compared exactly including case. |
| `Hub:DataSourceKey` | yes | none | Base64 of 32 bytes (section 1). |
| `Hub:PreviousDataSourceKeys` | no | empty | Old keys, read-only, kept while rotating. |
| `Hub:LaunchRateLimit:PermitLimit` | no | `30` | Launch requests per client address per window. At least 1. |
| `Hub:LaunchRateLimit:WindowSeconds` | no | `60` | Window length. At least 1. |
| `Hub:ForwardedHeaders:KnownProxies` | no | none (loopback only) | IP addresses of reverse proxies whose `X-Forwarded-*` headers are trusted. |
| `Hub:ForwardedHeaders:KnownNetworks` | no | none | The same, as CIDR ranges such as `10.0.0.0/8`. |
| `Hub:ForwardedHeaders:ForwardLimit` | no | `1` | How many proxy hops of `X-Forwarded-For` to read, counted from the right. At least 1. |
| `Hub:DefaultMaxRecords` | no | none | Row limit (1 to 100,000) for data sources with none of their own. |
| `Hub:ApplyMigrations` | no | `true` | `false` when a DBA applies the schema script. |
| `AllowedHosts` | no | `*` | Set to the hub's public host name, for example `hub.example.com`. |

The host refuses to start if a required setting is missing, the key is not base64 of 32 bytes, an old key repeats the
current one, there is neither a provider nor codes, codes are on with no `CodeNames`, or a provider is set but its
allowlist is empty. A missing `Hub:*` value is named by that setting; the other refusals name the package option it maps to (for example `DataSourceManagement.EncryptionKey` for a missing or bad `Hub:DataSourceKey`; see Troubleshooting). A message never contains a value. The older `Hub:Entra:*` section is no longer read: a host
that still has any `Hub:Entra` value refuses to start.

Super admins are checked against `Hub:SuperAdmins` on every request. Settings are read once when the process starts, so
every change in this section needs a **restart**.

### A4. Secrets in environment variables

Never put the client secret, the key or the connection string in `appsettings.json`, in `web.config`, or in source control. Do not pass them on a shared command line when an environment variable will do (arguments land in shell history
and in process lists).

Example (Entra, with codes as a second way in):

```
Hub__ConnectionString=Server=...;Database=QueryBuilderHub;...
Hub__Audience=https://hub.example.com
Hub__SignIn__Provider__Kind=Entra
Hub__SignIn__Provider__TenantId=<tenant guid>
Hub__SignIn__Provider__ClientId=<client guid>
Hub__SignIn__Provider__ClientSecret=<client secret>
Hub__DataSourceKey=<base64 of 32 bytes>
Hub__SuperAdmins__TenantIds__0=<tenant guid>
Hub__SuperAdmins__ObjectIds__0=<your object id>
Hub__SignIn__Codes__Enabled=true
Hub__SuperAdmins__CodeNames__0=<a name for the person or role>
Hub__ForwardedHeaders__KnownProxies__0=10.0.0.4
AllowedHosts=hub.example.com
```

How to set them where you host:

- **Windows service or a shell:** set them for the process. In PowerShell 7.1 or later: `$env:Hub__DataSourceKey = Read-Host 'Key' -MaskInput` (for that window only, and nothing is typed on the command line). Windows PowerShell 5.1 has no `-MaskInput`: a value typed on the command line lands in PSReadLine's history file, so clear that history afterwards (`Clear-Content (Get-PSReadLineOption).HistorySavePath`).
- **Azure App Service:** Settings, Environment variables (or Key Vault references). On Linux App Service the double underscore
  works as is.
- **Container:** `-e` flags or your orchestrator's secrets.
- **IIS:** per application pool, or a file on the server that the site's account can read and nobody else can. Do not put them in
  the published folder's `web.config` where it ends up in a backup or a repository.

### A5. Hosting: HTTPS and a reverse proxy

The host redirects http to https and sends HSTS outside Development. It sets these headers on every response:
`Content-Security-Policy: frame-ancestors 'self'`, `X-Frame-Options: SAMEORIGIN`, `X-Content-Type-Options: nosniff` and
`Referrer-Policy: same-origin`. It serves `/healthz` (answers `ok`, no sign-in) and sends `/` to `/querybuilder/`.

**When TLS ends at a proxy** (IIS reverse proxy, Azure Front Door or Application Gateway, nginx, a load balancer), the hub
must trust that proxy's `X-Forwarded-For` and `X-Forwarded-Proto` headers, and only that proxy's:

1. Set `Hub:ForwardedHeaders:KnownProxies` (addresses) or `Hub:ForwardedHeaders:KnownNetworks` (CIDR ranges). With neither set,
   only a proxy on the same machine (loopback) is trusted. **Setting either one replaces that loopback default**, so list
   loopback too if a local proxy sits in front.
2. With **chained proxies**, list every hop and set `Hub:ForwardedHeaders:ForwardLimit` to the number of hops. With a chain and
   a limit of 1, every user looks like they come from the first proxy: they share one launch rate-limit allowance and the
   audit log shows the wrong address. (Alternatively, have the last proxy replace `X-Forwarded-For` with the real client address.)
3. `X-Forwarded-Prefix` is ignored. The hub has no path base.
4. Set `AllowedHosts` to the public host name.

Wrong proxy values do two harms at once: the hub thinks every request is plain http (Entra then answers `AADSTS50011`, or
sign-in loops), and every user appears to come from the proxy's address.

**Check it:** open `https://<hub>/healthz`. It must say `ok`. Then sign in as a super admin (A7).

**IIS:** install the ASP.NET Core Hosting Bundle for .NET 10, create a site on https with the certificate, and point it at the
published folder. How IIS passes the original scheme depends on how you host (in-process or as a reverse proxy to a separate process); check Microsoft's
documentation, and if a proxy is involved apply the proxy rules above. **Azure App Service:** it terminates TLS in front of the app; check your provider's current
documentation for which proxy addresses to trust, and test with `/healthz` and a sign-in. **A header size limit:** the hub's
session cookie can reach about 13 to 15 KB in the worst case, so check your proxy's request header size limit.

### A6. Several instances

Every instance must use the same metadata database and the same `Hub:DataSourceKey` (and `Hub:PreviousDataSourceKeys`). The hub
keeps its Data Protection keys, the ones that protect the session cookie, **in its own metadata database**, encrypted with the data
source key. Do not configure Data Protection yourself (no `PersistKeysTo...`, no `ProtectKeysWith...`, no shared folder). Do not
set `HostOptions.ServicesStartConcurrently` to `true`: the steps that migrate and claim the database must start in order.

### A7. Super admin sign-in

#### Option 1: Microsoft Entra ID (`Kind` = `Entra`)

In the Azure portal: Microsoft Entra ID, App registrations, New registration.

1. **Name** `QueryBuilder Hub`. **Supported account types**: *Accounts in this organizational directory only* (single tenant).
2. **Redirect URI**: platform **Web**, `https://<your hub host>/signin-oidc`. It must equal your `Hub:SignIn:Provider:CallbackPath`
   exactly (default `/signin-oidc`).
3. Copy the **Application (client) ID** and the **Directory (tenant) ID**.
4. Certificates and secrets, New client secret. Copy the value now (shown once) and note when it expires. When it expires,
   sign-in stops until you replace it. The host signs in with a client secret; certificates are not supported.
5. Recommended: under Enterprise applications, open the same app, Properties, set **Assignment required?** to Yes, and assign
   only the people (or the one group) who may use the hub. The hub's own allowlist would refuse others anyway, but they would show
   up as refusals in the audit log.
6. Only if you allow super admins by **group**: set `"groupMembershipClaims": "SecurityGroup"` in the app registration's manifest.
   A user in too many groups gets no `groups` claim from Entra and can then only match by object id or email.
7. Set the settings. In environment variables:

   ```
   Hub__SignIn__Provider__Kind=Entra
   Hub__SignIn__Provider__TenantId=<tenant guid>
   Hub__SignIn__Provider__ClientId=<client guid>
   Hub__SignIn__Provider__ClientSecret=<secret>
   Hub__SuperAdmins__TenantIds__0=<tenant guid>
   Hub__SuperAdmins__ObjectIds__0=<your object id>
   ```

   The allowlist for Entra is `TenantIds` (required) plus at least one of `ObjectIds`, `Emails`, `GroupIds`. The `sub` claim never
   matches, and `UserIds` is not used. Your object id is on your user's page in the portal. Portal wording changes; check
   Microsoft's documentation if a screen differs.

#### Option 2: another OpenID Connect provider (`Kind` = `OpenIdConnect`)

Register the hub as a **confidential web application** (one with a client secret, using the authorization code flow). The host
also uses PKCE.

1. Redirect URI: `https://<your hub host>/signin-oidc` (or your `CallbackPath`).
2. Set `Hub:SignIn:Provider:Kind` = `OpenIdConnect`, `Authority` = the provider's issuer address (absolute https; the host reads its
   `.well-known/openid-configuration`), `ClientId`, `ClientSecret`, and `DisplayName` for the button.
3. List who may sign in with at least one entry in `Hub:SuperAdmins` `UserIds`, `Emails` or `GroupIds`:
   - `UserIds`: the person's `sub`. Use this where you can.
   - `Emails`: matched ignoring case and **only when the provider says the email is verified** (`email_verified` is `true`).
   - `GroupIds`: matched against the claim named by `GroupsClaim` (default `groups`), by the claim's raw name.
   - `TenantIds` and `ObjectIds` mean nothing here (the host starts but logs a warning if they are set).
4. **To find your own `sub`**: try to sign in once. The refusal page is generic, but a refused sign-in writes `SuperAdminSignInRefused` to the
   hub audit log with the `sub` it saw as `objectId`. Add it to `UserIds` and restart. You can also read it from your provider's user page.

Starting points for common providers. These are hints, not instructions: **check your provider's documentation**, since screens and
claim names differ.

| Provider | `Authority` usually looks like | `sub` usually is | Groups |
|---|---|---|---|
| Google Workspace | `https://accounts.google.com` | A long number, not the email | Not in the token; use `UserIds`. Avoid `Emails`. |
| Okta | `https://<your-org>.okta.com` (or `.../oauth2/default`) | A short id starting `00u` | Add a groups claim to the app, and list in `GroupIds` whatever it contains. |
| Auth0 | `https://<your-tenant>.auth0.com/` | `auth0|<id>`, or `google-oauth2|<id>` for social logins | Not by default; add them in an Action, and set `GroupsClaim` to that claim's exact name. |
| Keycloak | `https://<host>/realms/<realm>` | A GUID | Add a Group Membership mapper to the client, with Full group path off. |

A provider that only supports SAML is not supported: use codes, or put an OpenID Connect broker in front of it.

#### Option 3: one-time sign-in codes

A code is a short-lived, single-use sign-in that an operator creates **on the server**. There is no screen or endpoint that
creates one.

1. Set `Hub:SignIn:Codes:Enabled` = `true` and put at least one name in `Hub:SuperAdmins:CodeNames`
   (`Hub__SuperAdmins__CodeNames__0=ops-alex`). Names are compared exactly, including case. Restart.
2. On the server, in the folder that holds `QueryBuilder.Hub.dll`, with the same environment variables as the site:

   ```bash
   dotnet QueryBuilder.Hub.dll signin-code --name ops-alex --minutes 10
   ```

   On Windows you can run `QueryBuilder.Hub.exe signin-code --name ops-alex --minutes 10`. `--minutes` is optional (default
   `Hub:SignIn:Codes:DefaultMinutes`, 15) and must be 1 to 60. It prints two lines and exits with 0:

   ```
   Code: <long string>
   Expires: 2026-11-12 10:15:00Z
   ```

   (UTC.) On an error it prints `Could not issue a sign-in code: <reason>` on the error output and exits with 1. The command writes
   the audit record before it prints the code, so it needs the hub's database. It starts no web server and runs no migrations.
3. Open `https://<hub>/querybuilder/signin`, paste the code in **One-time sign-in code**, and press **Sign in with code**.

**On IIS or Azure App Service.** Run the command from the site's own console (on App Service, the Kudu console at
`https://<app>.scm.azurewebsites.net`), in the folder that holds `QueryBuilder.Hub.dll`. The App Service settings are visible there.
Plain IIS has no such console: environment variables set for the app pool are not visible to a shell you open yourself, so set
the `Hub__...` variables in that shell first. Prefer that to `--Hub:...=value` arguments.

Rules for codes:

- A code works once and lives at most 60 minutes (default 15). Clocks may differ by up to 30 seconds.
- A code is tied to this hub (its `Hub:Audience` and a key derived from `Hub:DataSourceKey`): another hub does not accept it.
- Handle a code like a password. Never paste it in a chat, ticket or email.
- **With codes on, `Hub:DataSourceKey` is a super admin credential:** anyone who holds it can make a valid code for any name in `CodeNames`. Use a different key per deployment.
- A code session appears in the audit log as `super:code:<name>`. `SuperAdminCodeIssued` is written by the command, then
  `SuperAdminSignedIn` with method `code`.
- Attempts are limited to 10 per 60 seconds per address by default (`Hub:SignIn:Codes:PermitLimit` and `WindowSeconds`), then `429`.
- To revoke: remove the name from `CodeNames` and restart. To stop all code sessions: set `Hub:SignIn:Codes:Enabled` to `false` and restart.

#### Using both

With both on, `https://<hub>/querybuilder/signin` shows a **Sign in with ...** button and a code field.

### A8. First start and verification

1. Start the hub (service, IIS site or `dotnet QueryBuilder.Hub.dll`). Watch the log: a refusal names the setting.
2. Browse to `https://<hub>/healthz`: it answers `ok`.
3. Browse to `https://<hub>/querybuilder/signin`. Sign in with your provider or a code.
4. You land in the console as a super admin. Open **Apps** (`/querybuilder/hub/apps`). A new hub shows only the built-in Default app, turned off.
5. If sign-in fails, go to Troubleshooting (section 7).

## 3. Part B: build your own hub on the `QueryBuilder.Editor` package

Read this part only if you chose your own host. The package does the hub work (launch, sessions, roles, the console, the
registry). Your host must supply what the ready-made host adds around it. **Those jobs are now yours.**

### B1. What the package requires in hub mode

At startup, `AddQueryBuilderEditor` refuses to run in hub mode unless:

- `options.Hub.Audience` is not blank (the ready-made host also demands a clean `https://host[:port]` with no path or trailing slash; do the same);
- `options.DataSourceManagement.EncryptionKey` is set (base64 of 32 bytes);
- `options.Hub.IdleTimeout` and `AbsoluteLifetime` are above zero and `AbsoluteLifetime` is not shorter than `IdleTimeout`;
- there is a way for a super admin to sign in: `options.Hub.SuperAdminSignInScheme` is set, or `options.Hub.SignInCodes.Enabled` is true;
- with `SignInCodes.Enabled`, at least one entry in `options.Hub.SuperAdmins.CodeNames`, and `DefaultMinutes` from 1 to 60;
- with `SuperAdminSignInScheme` set and `ProviderKind` = `Entra` (the default): `TenantIds` has an entry, and at least one of `ObjectIds`, `Emails`, `GroupIds`;
- with `ProviderKind` = `OpenIdConnect`: at least one of `UserIds`, `Emails`, `GroupIds`, and a non-blank `GroupsClaim`.

The package takes over Data Protection (keys stored in the metadata database, encrypted with the data source key) and registers the
`__Host-qb-hub` session cookie scheme and a short-lived cookie scheme named `QueryBuilder.HubExternal`. It does not register
the OpenID Connect handler. You add it.

If the metadata connection string (or a data source's) uses Entra authentication (`Authentication=Active Directory Managed
Identity`, `Active Directory Default` and so on), add the package that holds those methods. Since SqlClient 7 they are not in
SqlClient itself, and without it the connection fails with "Cannot find an authentication provider for '...'". Keep its version
in step with the `Microsoft.Data.SqlClient` that QueryBuilder.Editor uses (7.1.0 for 2.0.0):

```bash
dotnet add package Microsoft.Data.SqlClient.Extensions.Azure --version 7.1.0
```

Nothing else is needed: SqlClient finds the methods once the package is in the app. The database roles are in A2.

### B2. A complete minimal `Program.cs`

This is a minimal host for Entra sign-in with one-time codes available. It was compiled against this repository's package
code on `net10.0` (a throwaway project with a project reference to `QueryBuilder.Editor` and the package
`Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.14). It was not run against a database or a provider.

The project file:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <!-- 2.0.0 is the first version with hub mode; 2.1.0 adds chart data downloads (no database change). -->
    <PackageReference Include="QueryBuilder.Editor" Version="2.1.0" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.14" />
  </ItemGroup>
</Project>
```

(`QueryBuilder.Editor` also targets `net8.0`. This guide's sample is `net10.0`; use the OpenID Connect package version that matches your
framework.)

`Program.cs`:

```csharp
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Hub;

const string ProviderScheme = "entra";
const string LaunchPattern = "/querybuilder/launch";
const string CodePattern = "/querybuilder/signin/code";

// "signin-code" runs the code issuer on this machine and exits; it never starts the web server.
var issuingCode = args.Length > 0 && args[0] == "signin-code";

var builder = WebApplication.CreateBuilder(issuingCode ? [] : args);
if (issuingCode)
{
    builder.Logging.ClearProviders();   // stdout must carry only the Code: and Expires: lines
}

var config = builder.Configuration;
string Required(string key) =>
    config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{key} is required.");

var tenantId = Required("Hub:SignIn:Provider:TenantId");
if (tenantId.Trim().ToLowerInvariant() is "common" or "organizations" or "consumers")
{
    throw new InvalidOperationException("Hub:SignIn:Provider:TenantId must name one tenant.");
}

var audience = Required("Hub:Audience");
if (!Uri.TryCreate(audience, UriKind.Absolute, out var audienceUri)
    || audienceUri.Scheme != Uri.UriSchemeHttps
    || audience.EndsWith('/') || audience.Contains('?') || audience.Contains('#')
    || !string.IsNullOrEmpty(audienceUri.AbsolutePath.Trim('/')))
{
    throw new InvalidOperationException("Hub:Audience must be the hub's public https:// address only, with no path or trailing slash.");
}

builder.Services.AddQueryBuilderEditor(o =>
{
    o.ConnectionString = Required("Hub:ConnectionString");
    o.ApplyMigrations = !issuingCode && config.GetValue("Hub:ApplyMigrations", true);
    o.Hub.Enabled = true;
    o.Hub.Audience = audience;                                                  // https://hub.example.com, no trailing slash
    o.Hub.SuperAdminSignInScheme = ProviderScheme;
    o.Hub.ProviderDisplayName = "Contoso";
    o.Hub.SuperAdmins.ProviderKind = SuperAdminProviderKind.Entra;
    o.Hub.SuperAdmins.TenantIds = [tenantId];
    o.Hub.SuperAdmins.ObjectIds = [.. config.GetSection("Hub:SuperAdmins:ObjectIds").Get<string[]>() ?? []];
    o.Hub.SignInCodes.Enabled = config.GetValue("Hub:SignIn:Codes:Enabled", false);
    o.Hub.SuperAdmins.CodeNames = [.. config.GetSection("Hub:SuperAdmins:CodeNames").Get<string[]>() ?? []];
    o.DataSourceManagement.EncryptionKey = Required("Hub:DataSourceKey");
    o.DataSourceManagement.PreviousEncryptionKeys =
        [.. config.GetSection("Hub:PreviousDataSourceKeys").Get<string[]>() ?? []];
});

builder.Services.AddAuthentication().AddOpenIdConnect(ProviderScheme, options =>
{
    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
    options.ClientId = Required("Hub:SignIn:Provider:ClientId");
    options.ClientSecret = Required("Hub:SignIn:Provider:ClientSecret");
    options.CallbackPath = "/signin-oidc";
    options.SignInScheme = "QueryBuilder.HubExternal";
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.UsePkce = true;
    options.MapInboundClaims = false;
    options.SaveTokens = false;
    options.Scope.Clear();
    foreach (var scope in new[] { "openid", "profile", "email" })
    {
        options.Scope.Add(scope);
    }
});
builder.Services.AddAuthorization();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = config.GetValue("Hub:ForwardedHeaders:ForwardLimit", 1);
    var proxies = config.GetSection("Hub:ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    var networks = config.GetSection("Hub:ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
    if (proxies.Length > 0 || networks.Length > 0)
    {
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in proxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));   // .NET 8: KnownNetworks and Microsoft.AspNetCore.HttpOverrides.IPNetwork
        }
    }
});

static bool IsPost(HttpContext context, string pattern) =>
    HttpMethods.IsPost(context.Request.Method)
    && context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: var raw }
    && raw == pattern;

int launchPermits = config.GetValue("Hub:LaunchRateLimit:PermitLimit", 30);
int launchWindow = config.GetValue("Hub:LaunchRateLimit:WindowSeconds", 60);
int codePermits = config.GetValue("Hub:SignIn:Codes:PermitLimit", 10);
int codeWindow = config.GetValue("Hub:SignIn:Codes:WindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (IsPost(context, LaunchPattern))
        {
            return RateLimitPartition.GetFixedWindowLimiter("launch:" + ip,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = launchPermits, Window = TimeSpan.FromSeconds(launchWindow), QueueLimit = 0 });
        }

        return IsPost(context, CodePattern)
            ? RateLimitPartition.GetFixedWindowLimiter("code:" + ip,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = codePermits, Window = TimeSpan.FromSeconds(codeWindow), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other");
    });
    options.OnRejected = (rejected, _) =>
    {
        var seconds = IsPost(rejected.HttpContext, CodePattern) ? codeWindow : launchWindow;
        rejected.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();

if (issuingCode)
{
    var name = args.SkipWhile(a => a != "--name").Skip(1).FirstOrDefault()
        ?? throw new ArgumentException("Usage: signin-code --name <name> [--minutes <1-60>]");
    int? minutes = int.TryParse(args.SkipWhile(a => a != "--minutes").Skip(1).FirstOrDefault(), out var m) ? m : null;
    var issued = await app.Services.GetRequiredService<ISuperAdminCodeIssuer>().IssueAsync(name, minutes, CancellationToken.None);
    Console.WriteLine($"Code: {issued.Code}");
    Console.WriteLine($"Expires: {issued.ExpiresUtc.UtcDateTime:u}");
    return;
}

app.UseForwardedHeaders();
app.Use((context, next) =>
{
    context.Response.OnStarting(static state =>
    {
        var headers = ((HttpContext)state).Response.Headers;
        headers["Content-Security-Policy"] = "frame-ancestors 'self'";
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "same-origin";
        return Task.CompletedTask;
    }, context);
    return next();
});
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseQueryBuilderEditorUI();
app.MapQueryBuilderEditor();
app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();
app.MapGet("/", () => Results.Redirect("/querybuilder/")).AllowAnonymous();
app.Run();
```

Notes on the sample:

- **Settings names.** The sample reads the same `Hub:*` names as the ready-made host (`Hub:SignIn:Provider:TenantId`, `ClientId`, `ClientSecret`,
  `Hub:ForwardedHeaders:*`, `Hub:LaunchRateLimit:*`, `Hub:SignIn:Codes:*` and so on), so the environment variables in [A4](#a4-secrets-in-environment-variables) and the
  [A3](#a3-settings) table work unchanged. It supports Entra only: `Hub:SignIn:Provider:Kind`, `Instance` and `Authority` are not read. Your own host may read any names. Only the `options.*` assignments matter to the package.
- **Generic OpenID Connect instead of Entra.** Set `o.Hub.SuperAdmins.ProviderKind = SuperAdminProviderKind.OpenIdConnect`; fill
  `UserIds`, `Emails` or `GroupIds` (not `TenantIds`/`ObjectIds`); set `options.Authority` to the provider's issuer; and, to read
  userinfo claims as the ready-made host does, add:

  ```csharp
  options.GetClaimsFromUserInfoEndpoint = true;
  options.ClaimActions.MapUniqueJsonKey("email_verified", "email_verified");
  options.ClaimActions.MapJsonKey("groups", "groups");   // the claim name you set in o.Hub.SuperAdmins.GroupsClaim
  ```

  Only claims you map reach the principal that the allowlist checks. `MapInboundClaims = false` keeps the raw claim names.
- **Codes only.** Leave out `AddOpenIdConnect` and `SuperAdminSignInScheme`, and set `o.Hub.SignInCodes.Enabled = true` with at least one
  `CodeNames` entry.
- **.NET 8.** `KnownIPNetworks` and `System.Net.IPNetwork` exist from .NET 9. On .NET 8 use `options.KnownNetworks` with `Microsoft.AspNetCore.HttpOverrides.IPNetwork`.
- **The `signin-code` branch.** It clears the log providers so standard output carries only the `Code:` and `Expires:` lines. It builds the services with `ApplyMigrations` off and never starts the host. Run it from the app
  folder with the same environment variables as the site: `dotnet MyHub.dll signin-code --name ops-alex --minutes 10`.

### B3. How a custom host issues sign-in codes

Codes are created only by your own server-side code. The package registers `ISuperAdminCodeIssuer` (namespace
`QueryBuilder.Editor.Hub`) when hub mode is on:

```csharp
public interface ISuperAdminCodeIssuer
{
    Task<IssuedCode> IssueAsync(string name, int? minutes, CancellationToken ct);
}

public sealed record IssuedCode(string Code, string Jti, DateTimeOffset ExpiresUtc);
```

`IssueAsync` refuses (throws) when `SignInCodes.Enabled` is off. `minutes` of `null` means `SignInCodes.DefaultMinutes`; the
value must be 1 to 60. It writes a `SuperAdminCodeIssued` audit entry (actor `server-command`, never the code) before it returns the code. The `name`
must be in `SuperAdmins.CodeNames` or the code will be refused at sign-in (`NotListed`).

**With codes on, the `EncryptionKey` is a super admin credential:** whoever holds it can mint a code for any name in `CodeNames`, so keep it as secret as an admin password and use a different key per deployment.

Do not expose this over HTTP. Call it from a command, a script or a tool that only an operator on the server can run, as the
`signin-code` branch in B2 does. The person pastes the code at `/querybuilder/signin`, which posts to
`POST /querybuilder/signin/code` (form field `code`; the request's `Origin` header must equal the hub's own origin).

### B4. What you must not change

Moving from the ready-made host to your own host moves these jobs to you. Each of them was put in place for a reason.

- **HTTPS everywhere.** The session cookie is `__Host-qb-hub`; over http nobody signs in. Use `UseHttpsRedirection` and HSTS outside development.
- **Forwarded headers** behind a proxy (`UseForwardedHeaders`, with `KnownProxies`/`KnownNetworks` and `ForwardLimit` set to match your chain). Without them the
  hub sees http, and every user shares one client address (so rate limits misfire).
- **Security headers** on every response, including errors and redirects: `frame-ancestors 'self'` (clickjacking), and the others in the sample.
- **Per-IP rate limits** on `POST /querybuilder/launch` and `POST /querybuilder/signin/code`, matched by route, not by path text (routing also serves a trailing slash and any letter case).
- **`UseExceptionHandler()`**, or every refusal looks like a 500.
- **No `UsePathBase`.** The hub is served from the root of its host name.
- **Do not configure Data Protection yourself.** No `PersistKeysTo...`, no `ProtectKeysWith...`. Hub mode owns it.
- **OIDC scheme**: `SignInScheme = "QueryBuilder.HubExternal"`, PKCE on, `MapInboundClaims = false`, and `o.Hub.SuperAdminSignInScheme` set to your scheme's name. Never allow a multi-tenant Entra authority (`common`, `organizations`, `consumers`).
- **The data source key** is the same on every instance, and is a secret.
- **Do not start hosted services concurrently** (`HostOptions.ServicesStartConcurrently`).
- **Do not use the same database as an embedded QueryBuilder.**
- **Do not add your own scheme that signs into `QueryBuilder.HubExternal`.** The package accepts the ticket only if it was started at `/querybuilder/signin`
  and stamped by the scheme you named, so another handler cannot create a super admin.
- **Check the pipeline order** shown in the sample: forwarded headers, security headers, exception handler, HSTS, HTTPS redirection, rate limiter,
  authentication, authorization, `UseQueryBuilderEditorUI`, `MapQueryBuilderEditor`.

## 4. Part C: connect an app ("App A")

Do these in order. Steps C1 to C3 are the hub operator's; C4 to C8 are the app owner's. They exchange only **public** things.

### C1. Register the app in the console

1. A super admin opens `https://<hub>/querybuilder/hub/apps` and chooses **New app**.
2. Enter a **name** and a **key**: 2 to 63 characters, lowercase letters, digits and hyphens, starting with a letter or digit (for
   example `agency-a`). The key is the token's `iss` claim.
3. Open the app's detail page and set:
   - **Scope keys**: the names of the row-level values the app will send in its tokens (for example `ProgramId`). At most 20. A token naming a key the hub does not know is refused.
   - **Record ceiling**: an optional upper bound on rows per query for this app. The lower of the ceiling and the data source's own limit applies.
   - **App admins can add data sources**: off by default. Turn it on only if the app's own admins should add, edit, test and turn off the unlocked data sources of their own app.
   - **Turned on**: **a new app starts turned on.** Turn it off now and turn it back on in C3 step 4, once the owner is ready.
4. Give the app owner: the **app key**, the hub **audience** (`Hub:Audience`, exactly), the **scope keys**, and a link to the
   [`QueryBuilder.Launch` guide](../src/QueryBuilder.Launch/README.md).

### C2. Make a key pair (app owner)

Use RSA 2048 to 8192 bits (RS256) or ECDSA P-256 (ES256). Keep the **private** key only where the app's account can read it
(a key store, a certificate store, or a file outside the web root). Never put it in `web.config`, `appsettings.json` or source
control. Only the public key goes to the hub operator. Where the app keeps it is in C2a; the simplest start is a key file in the
app's deployment package.

**PowerShell 7.4 or later** (older 7.x: use openssl):

```powershell
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$rsa.ExportPkcs8PrivateKeyPem() | Set-Content -Encoding ascii qb-launch-private.pem
$rsa.ExportSubjectPublicKeyInfoPem() | Set-Content -Encoding ascii qb-launch-public.pem

# The fingerprint the console will show: SHA-256 of the public key, lowercase hex
[Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
```

**Windows PowerShell 5.1** cannot write PEM. Keep the private key in a certificate (RS256 only) and write only the public key as PEM:

```powershell
$cert = New-SelfSignedCertificate -Subject "CN=my-app-querybuilder-launch" `
    -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy Exportable `
    -Provider 'Microsoft Software Key Storage Provider' -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(2) -CertStoreLocation Cert:\CurrentUser\My

function Get-DerLength([int]$n) {
    if ($n -lt 128) { return [byte[]]@($n) }
    if ($n -lt 256) { return [byte[]]@(0x81, $n) }
    return [byte[]]@(0x82, [byte]($n -shr 8), [byte]($n -band 0xFF))
}
$rsaPublicKey = $cert.PublicKey.EncodedKeyValue.RawData
$algorithmId  = [byte[]]@(0x30,0x0D,0x06,0x09,0x2A,0x86,0x48,0x86,0xF7,0x0D,0x01,0x01,0x01,0x05,0x00)
$bitString    = [byte[]]@(0x03) + (Get-DerLength ($rsaPublicKey.Length + 1)) + [byte[]]@(0x00) + $rsaPublicKey
$body         = $algorithmId + $bitString
$spki         = [byte[]]@(0x30) + (Get-DerLength $body.Length) + $body

$base64 = [Convert]::ToBase64String($spki, 'InsertLineBreaks')
"-----BEGIN PUBLIC KEY-----`n$base64`n-----END PUBLIC KEY-----" | Set-Content -Encoding ascii qb-launch-public.pem

$sha = [System.Security.Cryptography.SHA256]::Create()
($sha.ComputeHash($spki) | ForEach-Object { $_.ToString('x2') }) -join ''

$cert.Thumbprint    # your app finds the private key by this thumbprint
```

For IIS, create the certificate in `Cert:\LocalMachine\My` as an administrator and give the app pool identity read access to the
private key (`certlm.msc`, right-click the certificate, All Tasks, Manage Private Keys). To keep a backup, export a PFX with a
password you read in with `Read-Host -AsSecureString` and keep the file and its password in your password manager.

**openssl** (Linux, macOS, Git Bash):

```bash
# RSA (RS256)
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out qb-launch-private.pem
# ... or ECDSA P-256 (ES256)
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out qb-launch-private.pem

openssl pkey -in qb-launch-private.pem -pubout -out qb-launch-public.pem

# The fingerprint the console will show
openssl pkey -pubin -in qb-launch-public.pem -outform DER | openssl dgst -sha256
```

Send only the file that starts with `-----BEGIN PUBLIC KEY-----`, plus the fingerprint you computed, and tell the
operator your **key id** (`kid`): for example `2026-11`, 1 to 64 characters, letters, digits, `.`, `_`, `-`, starting with a letter or digit.

### C2a. Where the app keeps its private key (app owner)

| Option | Effort | Works on |
|---|---|---|
| **Key file in the deployment package** (start here) | Lowest: a file next to the app's code | .NET 6+ (`.pem`), .NET Framework 4.8 (`.xml`); Azure App Service Windows and Linux, IIS |
| Certificate (PFX) loaded by the platform | Medium: upload once per app, one app setting | All; on App Service needs the Basic plan or higher |
| Key Vault | Highest; rotation in one place | All |

**A key file in the deployment package.** Anyone who can read the deployed files (Kudu, FTP, deployment credentials, a copy of the
package) can sign launches into that app as anyone, including Admin. Accept that knowingly, and:

1. **Never commit the key file**: `.gitignore` it; it lives on the machine you publish from (copy in a password manager), or a
   pipeline adds it from its secure files.
2. **Keep it out of what the web server serves**: a `Keys` folder at the project root for .NET 6+ (not `wwwroot`), `App_Data` for
   .NET Framework.
3. **One key per app**, each with its own key id.

*.NET 6, 8 and later*: put `qb-launch-private.pem` (from C2) in `Keys\`, `.gitignore` `Keys/*.pem`, publish it with
`<None Update="Keys\qb-launch-private.pem" CopyToPublishDirectory="PreserveNewest" />` in the `.csproj`, and load it:

```csharp
var keyPath = Path.Combine(builder.Environment.ContentRootPath, "Keys", "qb-launch-private.pem");
var rsa = RSA.Create();
rsa.ImportFromPem(File.ReadAllText(keyPath));
builder.Services.AddSingleton(LaunchSigningKey.Rsa(builder.Configuration["QueryBuilder:KeyId"]!, rsa));
```

*.NET Framework 4.8* cannot read PEM, so its file holds the same RSA key as XML. Make it in PowerShell 7:

```powershell
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
# ...or, to keep a key already registered: $rsa = [System.Security.Cryptography.RSA]::Create(); $rsa.ImportFromPem((Get-Content qb-launch-private.pem -Raw))
Set-Content -Encoding ascii qb-launch-private.xml $rsa.ToXmlString($true)
Set-Content -Encoding ascii qb-launch-public.pem  $rsa.ExportSubjectPublicKeyInfoPem()
[Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
```

Put it in `App_Data\qb-launch\` (Build Action = Content), `.gitignore` `App_Data/qb-launch/*.xml`, and load it once:

```csharp
var path = HostingEnvironment.MapPath("~/App_Data/qb-launch/qb-launch-private.xml");
var rsa = new RSACng();          // in-memory key: no user profile or key container needed (App Service has neither)
rsa.FromXmlString(File.ReadAllText(path));
var key = LaunchSigningKey.Rsa(ConfigurationManager.AppSettings["QueryBuilder:KeyId"], rsa);   // keep in a static field
```

This was checked on .NET Framework 4.8 with `QueryBuilder.Launch` 1.0.0: the token it signed verified against the public `.pem`.
The full walk-through, with `appsettings.json`/`web.config` entries, is in the
[`QueryBuilder.Launch` guide, section 2a](../src/QueryBuilder.Launch/README.md#2a-the-simplest-start-a-key-file-in-your-deployment-package).

**Changing the key** (and moving to a certificate or Key Vault later): make a new key with a new key id, have the hub operator add
it, deploy the app with it, then revoke the old key (revoking ends that app's sessions, so do it after the deployment).

### C3. Register the public key and return URLs (hub operator)

1. In the console, open the app, **Signing keys**, **Add a key**. Enter the **key id**, choose the **algorithm** (RS256 or ES256) and
   paste the PEM. The console refuses a private key, a certificate, an RSA key under 2048 or over 8192 bits and an EC key that is not P-256.
2. The list then shows the key's **fingerprint**. **Compare it with the one the app owner sent you.** If they differ, the wrong key was pasted.
3. Under **Return URLs**, add where "Back to your app" and logout should send users: the canonical form, for example
   `https://app.example.com/` (https only; `http://localhost` is allowed for development), up to 800 characters, at most 20 per app.
   A token's `returnUrl` must equal one of them exactly (case-sensitive).
4. Turn the app on once the owner is ready (a new app starts turned on, so this turns it back on if you turned it off in C1).

### C4. Add the package and the button (app owner): ASP.NET Core

Add the package (`netstandard2.0`, no dependencies, RS256 or ES256 only):

```powershell
dotnet add package QueryBuilder.Launch
```

Load the private key once at startup. The path comes from configuration and the key file from your secret store (or from the
deployment package, C2a).

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using QueryBuilder.Launch;

var builder = WebApplication.CreateBuilder(args);

var rsa = RSA.Create();
rsa.ImportFromPem(File.ReadAllText(builder.Configuration["QueryBuilder:PrivateKeyPath"]!));
builder.Services.AddSingleton(LaunchSigningKey.Rsa("2026-11", rsa));   // kid as registered in the hub

var app = builder.Build();

app.MapGet("/open-querybuilder", (ClaimsPrincipal user, LaunchSigningKey key, IConfiguration config, HttpContext http) =>
{
    var hub = config["QueryBuilder:HubUrl"]!;                          // e.g. https://hub.example.com

    // Which rows this user may see. Keys must be declared for your app in the hub console.
    var scope = LaunchScope.For(new Dictionary<string, IEnumerable<string>>
    {
        ["ProgramId"] = new[] { "10", "20" },
    });
    // Or, for a user who may see everything: LaunchScope.Unrestricted

    var token = LaunchToken.Create(
        appKey: "my-app",
        audience: hub,
        key: key,
        user: new LaunchUser(user.FindFirstValue(ClaimTypes.NameIdentifier)!, user.Identity!.Name, user.FindFirstValue(ClaimTypes.Email)),
        role: LaunchRole.Author,                                       // Admin, Author or Viewer
        scope: scope,
        returnUrl: "https://my-app.example.com/");                     // optional, must be registered

    http.Response.Headers.CacheControl = "no-store";
    return Results.Content(LaunchForm.Html(hub, token), "text/html");
}).RequireAuthorization();

app.Run();
```

Then put a link on a page the signed-in user sees: `<a href="/open-querybuilder">Go to QueryBuilder</a>`. The page that comes back is a
complete HTML document that posts the token to the hub by itself, so return it as `text/html` with `Cache-Control: no-store`. For ES256, use
`LaunchSigningKey.EcdsaP256("2026-11", ecdsa)` with an `ECDsa` loaded the same way. In an MVC controller:

```csharp
public class QueryBuilderController : Controller
{
    private readonly LaunchSigningKey _key;
    public QueryBuilderController(LaunchSigningKey key) => _key = key;

    [Authorize, HttpGet("/open-querybuilder")]
    public IActionResult Open()
    {
        var token = LaunchToken.Create("my-app", "https://hub.example.com", _key,
            new LaunchUser("user-42", "Mary Smith", "mary@example.com"), LaunchRole.Viewer, LaunchScope.Unrestricted);
        Response.Headers["Cache-Control"] = "no-store";
        return Content(LaunchForm.Html("https://hub.example.com", token), "text/html");
    }
}
```

If your app sends a Content Security Policy with a nonce, pass it as `LaunchForm.Html(hub, token, cspNonce)`. If it sends a
`form-action` directive, that must include the hub's origin. Without the script, the page still works and shows a
**Continue to QueryBuilder** button.

**The rules for your page.** Only offer the button to a user who is signed in to your app. Work out that user's role and scope on
your server, from your own data, every time. Never take them from the query string or a form field. Create the token when the user
clicks, not earlier (default lifetime 60 seconds, maximum 120).

### C5. ASP.NET MVC 5 on .NET Framework 4.8 (app owner)

`QueryBuilder.Launch` targets `netstandard2.0`, which .NET Framework 4.7.2 and later can use. Install it with the Package Manager Console
(`Install-Package QueryBuilder.Launch`). The simplest key is an XML file in `App_Data` (C2a); this section shows the certificate route.
On Azure App Service, upload the PFX under the app's **Certificates**, set the app setting `WEBSITE_LOAD_CERTIFICATES` to its
thumbprint, and use `StoreLocation.CurrentUser` instead of `LocalMachine` in the code below.
Load the private key from the Windows certificate store with `GetRSAPrivateKey()` (not `cert.PrivateKey`).
Keep only the thumbprint in `web.config`, never the key or a PFX password.

```csharp
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Web.Mvc;
using QueryBuilder.Launch;

public class QueryBuilderController : Controller
{
    // Created once for the whole process; the key itself is never exported.
    private static readonly LaunchSigningKey Key = LoadKey();

    [Authorize]
    public ActionResult Open()
    {
        var hub = ConfigurationManager.AppSettings["QueryBuilder:HubUrl"];       // https://hub.example.com
        var user = new LaunchUser(User.Identity.Name, name: null, email: null);  // use your own stable user id
        var scope = LaunchScope.For(new Dictionary<string, IEnumerable<string>>
        {
            ["ProgramId"] = new[] { "10" },
        });

        var token = LaunchToken.Create("my-app", hub, Key, user, LaunchRole.Author, scope);
        Response.Cache.SetNoStore();
        return Content(LaunchForm.Html(hub, token), "text/html");
    }

    private static LaunchSigningKey LoadKey()
    {
        var thumbprint = ConfigurationManager.AppSettings["QueryBuilder:KeyThumbprint"];   // not a secret
        using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
        {
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            if (found.Count == 0)
            {
                throw new InvalidOperationException("The launch signing certificate was not found in LocalMachine\\My.");
            }

            RSA rsa = found[0].GetRSAPrivateKey()
                ?? throw new InvalidOperationException("The certificate has no private key the app pool can read.");
            return LaunchSigningKey.Rsa("2026-11", rsa);
        }
    }
}
```

For a key in an ECDSA certificate use `GetECDsaPrivateKey()` with `LaunchSigningKey.EcdsaP256`. Do not dispose the `RSA` while the
app runs: `LaunchSigningKey` does not own it. The `hub` value must be only the scheme, host and port. The `web.config` keys are
`QueryBuilder:HubUrl` and `QueryBuilder:KeyThumbprint` in `appSettings`. Add the "Go to QueryBuilder" link in a Razor view with
`@Html.ActionLink("Go to QueryBuilder", "Open", "QueryBuilder")`.

### C6. Apps that are not .NET

Anything that can sign a JWT can launch. In short: a compact JWT with header `{"alg":"RS256","typ":"JWT","kid":"<kid>"}`
(or `ES256`, signature in the 64-byte `r || s` form), claims `iss` (app key), `aud` (the hub's exact `Hub:Audience`), `sub` (user id,
up to 256 characters, never starting with `super:`), `role` (exactly `Admin`, `Author` or `Viewer`), either `scope` (an object of key to array of strings) or
`unrestricted: true` (never both, never neither), `iat` and `exp` in **whole seconds** with `exp - iat` at most 120, a unique `jti`
(a token works once), and optionally `name`, `email` and `returnUrl`. Post it to the hub from the user's browser as a form
(`application/x-www-form-urlencoded`, exactly one field `token`) to `https://<hub>/querybuilder/launch`. Never put it in a URL or send it server to server.
The full claims table, the 6,144-byte scope limit and a decoded example are in
[`src/QueryBuilder.Launch/README.md`](../src/QueryBuilder.Launch/README.md), section 5.

### C7. Scope values and roles

- **Role** (`LaunchRole`): `Admin` sees and manages everything in the app and can publish dashboards. `Author` builds, saves, charts, runs, exports and
  shares. `Viewer` sees only published dashboards and those shared with them.
- **Scope**: `LaunchScope.For(...)` (a key may carry several values) for a user who may see only some rows, or `LaunchScope.Unrestricted`. At most 20 keys, 1,000 values
  per key, 200 characters per value, and 6,144 UTF-8 bytes once serialised. A declared key that the token leaves out means "no value": views scoped by it return no rows.
- **User id** (`LaunchUser`): stable, never reused for another person, up to 256 characters. The hub compares it exactly. A launch is refused if
  the app already has the same id in a different case (`UserIdCaseConflict`).
- A new launch replaces the user's hub session, so role and scope changes take effect on the next click. A session lasts at most 8 hours and ends after 1 hour unused.
- Keep your servers' clocks on NTP: the hub allows 30 seconds of difference.

### C8. Test the first launch

1. The operator turns the app on. The app owner signs in to the app and clicks **Go to QueryBuilder**.
2. Expected: the browser lands at `https://<hub>/querybuilder/` with a banner showing the app's name.
3. If you instead see "This link to QueryBuilder isn't valid or has expired" (status 400), the page never says why. A super admin opens
   `/querybuilder/hub/audit` and reads the `LaunchRefused` entry. The entry reads `Launch refused: <Reason>`:

| Reason | What to do |
|---|---|
| `Malformed` | Compare with the claims table; check `iat`/`exp` are integers, `sub` is at most 256 characters and does not start with `super:`. |
| `UnknownApp` | `iss` is not a registered app key. |
| `AppDisabled` | The app is turned off: turn it on in the console. |
| `UnknownKey` | Register the public key under that exact key id. |
| `RevokedKey` | Add a new key and sign with it. |
| `AlgorithmNotAllowed` | Not RS256/ES256, or not the algorithm the key was registered with. |
| `BadSignature` | Wrong key pair, or the token was altered; compare fingerprints. |
| `WrongAudience` | `aud` differs from the hub's `Hub:Audience`. |
| `Expired` / `NotYetValid` | Clock off by more than 30 seconds, or the token sat unused. |
| `LifetimeTooLong` | `exp - iat` over 120 seconds; use 60. |
| `Replayed` | The `jti` was already used; make a new token each time. |
| `MissingClaim` | A required claim is missing. |
| `UnknownRole` | `role` is not exactly `Admin`, `Author` or `Viewer`. |
| `ReturnUrlNotRegistered` | Register the return URL or omit `returnUrl`. |
| `UndeclaredScopeKey` | Add the key to the app's scope keys. |
| `ScopeMissing` / `ScopeAndUnrestricted` | Send exactly one of `scope` and `unrestricted`. |
| `ScopeTooLarge` | Send fewer values (6,144 bytes serialised at most). |
| `UserIdCaseConflict` | Send the id exactly as before. |

A `429` with `Retry-After` is not a refusal: it is the per-address launch limit (30 per 60 seconds by default). Everyone behind one
office address shares it.

## 5. Part D: first day in the hub

### D1. Add a data source

1. A super admin signs in and picks the app with the switcher (or opens it from **Apps**), then opens **Admin** and adds a data source (SQL Server or PostgreSQL):
   host, port, database, login and password. The password is encrypted with the data source key. It is never returned by any screen.
2. **What the form can and cannot reach.** A SQL Server source needs **SQL authentication** over **TCP**: no Windows (integrated) authentication, no managed identity, no
   LocalDB, no named pipes, and no `np:` or `tcp:` prefixes. For a named instance, give `host\instance` plus its port: the form always connects over TCP and does not use
   SQL Browser lookups (see the Editor README, "Adding data sources in the admin UI"). If your source needs anything else, use the raw SQL route in step 6.
3. A data source added by a super admin is **locked**. The app's admins and users can use it, but only a super admin changes it until one unlocks it.
   (If **App admins can add data sources** is on, an app admin can add, edit, test and turn off the unlocked data sources of their own app.)
4. The hub never uses its own metadata database as a data source, and refuses a data source that points at it (this applies to the raw SQL route too).
5. A new data source starts as views only. Scoped users see nothing until data scope rules exist for it, so the admin page opens its policy page after you create it.
6. **Advanced, for a DBA: raw SQL registration.** The connection string stays in the host's configuration instead of the database.
   1. On **every hub instance**, set an environment variable `ConnectionStrings__<name>` (for example `ConnectionStrings__SalesDb`) to the connection string.
      Use an environment variable or a server-only store, never a file in the repository.
   2. Find the app's id: open the app in the console; the address ends in `/querybuilder/hub/apps/<id>`.
   3. Insert the row with an **explicit `AppId`**. The column default (`DF_DataSources_AppId`) is the built-in Default app, which a hub turns off, so a row
      that leaves `AppId` out belongs to an app nobody can launch into:

      ```sql
      INSERT INTO DataSources
        (Id, AppId, Name, Provider, ConnectionStringName, AllowedSchemas, AllowedObjects, CatalogScope, ScopeKeys,
         IsActive, LockedBySuperAdmin, CreatedBy, CreatedAtUtc)
      VALUES
        (NEWID(), '<app id>', 'Sales', 0, 'SalesDb', '', '', 0, '',
         1, 1, 'dba', SYSUTCDATETIME());
      ```

      `Provider` is `0` for SQL Server and `1` for PostgreSQL; `CatalogScope` `0` is views only and `2` is tables and views; `LockedBySuperAdmin` `1` makes it a
      locked source. The column list was taken from the `DataSource` entity (`src/QueryBuilder.Domain/Entities/DataSource.cs`); check it against the schema script you
      ran, and test the statement on a copy first. This route was not run end to end for this guide.

### D2. Data scope mappings

If the app sends scope values (for example `ProgramId`), map each scope key to a column per data source: on the data source's policy page,
under data scope, map each declared key to a view column, or mark a view "Not scoped". Views left undecided are hidden from scoped users (fail closed).
Super admins and `unrestricted` tokens see all rows.

### D3. What each role sees

| Role | Sees |
|---|---|
| Super admin | Everything, in the app chosen with the switcher, with no row restriction, plus the console. |
| Admin | Every query, dashboard and share of the app; the Admin page for unlocked data sources. |
| Author | Their own and shared queries and dashboards, and the dashboards published to the app; the builder, charts, export. |
| Viewer | Published dashboards and those shared with them. Charts render with the viewer's own data scope. Never the SQL. |

### D4. Publish a dashboard for Viewers

An Admin (or a super admin) opens a dashboard and chooses **Publish**. Every Viewer and Author of the app then sees it read-only.
**Unpublish** removes it. Viewers see the chart, its settings and parameter controls, never the SQL or the source query.

### D5. Sharing

In hub mode, a query or dashboard can be shared only with people the app already knows: those who have launched (the 2,000 most recent are offered)
plus the data source's `AppUsers` view (the picker reads its first 500 rows). The Viewer role wins over any share: sharing a query with a Viewer gives them nothing beyond dashboards.

## 6. Operations

- **Rotate the data source key.** Make a new key. Set `Hub:DataSourceKey` to the new one and `Hub:PreviousDataSourceKeys` to the old one on **every instance**, restart,
  then press **Re-encrypt** on the console's Apps page (`/querybuilder/hub/apps`). **Keep the old key for at least 90 days plus 8 hours**
  (the session key ring lives 90 days; a session lasts up to 8 hours). Removing it sooner makes older sessions unreadable. With codes on, codes are signed with a key derived from `Hub:DataSourceKey`, so codes created with the old key and not yet used stop working. Full steps in [`MANAGING-KEYS.md`](../MANAGING-KEYS.md).
- **Rotate an app's signing key.** The app owner makes a new pair (C2). The operator adds the new public key (a new `kid`). The app switches to the new key and
  deploys. Then the operator revokes the old key. **Revoking a key ends every session of that app** (the hub does not record which key signed a session),
  and the key can never be used again, so add the replacement first.
- **Turn an app off.** In the console, turn it off. Its sessions end within about 30 seconds.
- **Remove a super admin.** Remove the id, email, group id or code name from `Hub:SuperAdmins` and **restart**. The allowlist is checked on every request but read once at start. A
  super admin matched through a group stays signed in until their session ends (8 hours at most) unless you remove the group id and restart.
- **Audit log.** Console, **Audit log** (`/querybuilder/hub/audit`). It records launches refused (`LaunchRefused`), sign-ins (`SuperAdminSignedIn`, `SuperAdminSignInRefused`), code issues (`SuperAdminCodeIssued`) and the changes made in the console. Never contains tokens or codes.
- **Backups.** Back up the hub database. It holds the registry, the saved queries, dashboards, the audit log, the encrypted data source passwords and the encrypted session key ring.
  Keep the data source key in a password manager: a backup is unreadable without it for the encrypted parts.
- **Upgrading.** Deploy the new build. With `Hub:ApplyMigrations` true, it migrates itself at start. With a DBA, run the new idempotent script first (`sqlcmd -I -b`).
  Read the release notes: an upgrade can add migrations.
- **Monitoring.** Poll `https://<hub>/healthz` (it answers `ok` without signing in). It checks that the process answers, not that the database is reachable.
  Watch for `429`s and for `LaunchRefused` entries in the audit log.

## 7. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Host won't start: `Hub:... is required` | A required setting is empty | Set it (A3, A4). |
| Host won't start, message names `DataSourceManagement.EncryptionKey` | Missing or bad `Hub:DataSourceKey` | Set base64 of 32 bytes. |
| Message names `HubOptions.SuperAdmins.TenantIds` | Entra, no tenant id | Set `Hub:SuperAdmins:TenantIds`. |
| Message names `HubOptions.SuperAdmins` (object ids or user ids, emails or groups) | Provider set, allowlist empty | Add at least one entry (A7). |
| Message names `HubOptions.SuperAdminSignInScheme` | No provider and codes off | Set `Hub:SignIn:Provider:Kind`, or turn codes on. |
| Message names `HubOptions.SignInCodes` ... `CodeNames` | Codes on, no names | Add `Hub:SuperAdmins:CodeNames`. |
| Message names `HubOptions.Audience` or "Hub:Audience must be the hub's public https:// address only" | Missing, or has a path, query or trailing slash | `https://hub.example.com`, nothing more. |
| "Hub:Entra is no longer used" | Old settings | Move to `Hub:SignIn:Provider:*` and set `Kind` to `Entra`. |
| "Hub:SignIn:Provider:Kind must be empty, Entra or OpenIdConnect" | Spelling or case | Use exactly one of them. |
| "...TenantId must name one tenant..." | `common`, `organizations` or `consumers` | Use your tenant's id. |
| "needs its own empty metadata database" / "belongs to an Embedded-mode installation" | The database was used by an embedded QueryBuilder | Use a new empty database. |
| "Cannot find an authentication provider for 'ActiveDirectoryManagedIdentity'" (or another `ActiveDirectory...` name) | The connection string uses Entra authentication and the app lacks `Microsoft.Data.SqlClient.Extensions.Azure` | Rebuild the ready-made hub from current `main` (it includes the package), or add the package to your own host (B1). |
| `CREATE TABLE permission denied in database '...'` at the first start | `Hub:ApplyMigrations` is `true` and the login cannot create tables | Grant `db_ddladmin` (plus `db_datareader`, `db_datawriter`), or let a DBA run the script and set `Hub:ApplyMigrations` to `false` (A2). |
| `AADSTS50011` at Entra, or a redirect-URI error at another provider | Registered redirect URI differs from `https://<host>` + `CallbackPath`, or the proxy's headers are not trusted (the URI is built as `http://...`) | Register the exact URL; fix `KnownProxies`/`KnownNetworks` (A5). |
| Sign-in loops, or redirects to `http://` | Proxy headers not trusted | Fix A5. |
| After provider sign-in: generic "This link to QueryBuilder isn't valid or has expired" (403) | Not on the allowlist, or in too many groups for Entra to list | A super admin reads `SuperAdminSignInRefused` in the audit log (`NotAllowlisted`, `WrongTenant`, `GroupsOverage`, `NoStableId`); add the entry and restart. |
| "Sign-in didn't start from this site" | The provider sign-in began on another site | Open `/querybuilder/signin` and try again. |
| Pasting a code gives "Sign-in code not accepted" (403) | Used already (`Replayed`), `Expired`, `WrongAudience`/`BadSignature` (another hub or key), `NotListed`, `OriginMismatch` | Read the reason in the audit log; make a new code. |
| Pasting a code gives 404 | Codes are off | `Hub:SignIn:Codes:Enabled` = `true`, restart. |
| `signin-code` prints "Could not issue a sign-in code: ..." | Codes off, name not in `CodeNames` (case matters), minutes outside 1 to 60, or settings/database missing where you ran it | Run it from the site's folder with its environment variables. |
| `429` on launch or code sign-in | Per-address rate limit; behind a proxy everyone may share one address | Wait `Retry-After` seconds; raise `Hub:LaunchRateLimit:PermitLimit`; fix proxy settings (A5). |
| An app's users get "This link to QueryBuilder isn't valid or has expired" (400) | Launch refused | Read `LaunchRefused` in `/querybuilder/hub/audit` and use the table in C8. |
| Nobody can sign in, over plain http | The `__Host-qb-hub` cookie is refused when not `Secure` | Use https end to end (or a proxy that sets `X-Forwarded-Proto` and is trusted). |
| Sessions stop working after a key change | The old key was removed from `PreviousDataSourceKeys` too soon | Put it back; keep it 90 days plus 8 hours. |
| Large scope: users randomly lose their session, or `ScopeTooLarge` | Cookie or scope too large | Send fewer scope values; raise the proxy's header size limit. |

## 8. Checklists

### Go-live checklist for the hub

- [ ] Empty hub database, its own; login roles as in A2 (`db_ddladmin` too unless a DBA runs the script); backups planned.
- [ ] Entra authentication in `Hub:ConnectionString`: the hub was built with `Microsoft.Data.SqlClient.Extensions.Azure` (the ready-made hub from current `main` is).
- [ ] `Hub:Audience` is the public https address, no trailing slash, and the same address the apps put in `aud`.
- [ ] `Hub:DataSourceKey` set on every instance; a copy in the password manager; different from every other deployment.
- [ ] Every secret is in environment variables or a server-only store, none in git or in `appsettings.json`.
- [ ] HTTPS certificate; `AllowedHosts` is the public host name.
- [ ] Behind a proxy: `KnownProxies`/`KnownNetworks` and `ForwardLimit` set; `/healthz` answers `ok`.
- [ ] Super admin sign-in works (provider and/or a code), and a second operator can sign in.
- [ ] The Entra client secret's expiry date is in a calendar.
- [ ] An alert polls `/healthz`.
- [ ] Several instances: same database, same key.
- [ ] You know how to remove a super admin (edit settings, restart).

### Onboarding checklist for each new app

- [ ] App created in the console (it starts turned on; turn it off until ready): key, name, scope keys, record ceiling, "App admins can add data sources" decided.
- [ ] App owner generated a key pair; private key stays with them (C2a: key file in the package is `.gitignore`d and outside `wwwroot`/inside `App_Data`; `.xml` for .NET Framework).
- [ ] Public key registered; **fingerprint compared**.
- [ ] Return URLs registered (canonical form, https).
- [ ] The owner has the app key, the audience, the scope keys and the Launch guide.
- [ ] Data sources added by a super admin while viewing the app; data scope mappings saved.
- [ ] The app is turned on; first launch tested for each role (Admin, Author, Viewer).
- [ ] A dashboard published for the Viewers.
- [ ] Clock sync (NTP) on the app's servers.
