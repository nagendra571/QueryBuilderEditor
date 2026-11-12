# QueryBuilder Hub

The deployable host for QueryBuilder in **hub mode**: one QueryBuilder serving several apps. Each app hands its users over
with a signed launch token (see [`QueryBuilder.Launch`](../QueryBuilder.Launch/README.md)); your own staff, the super admins,
sign in with Microsoft Entra ID. This is an application you deploy (`net10.0`), not a NuGet package.

What the host adds around the `QueryBuilder.Editor` package: Entra (OpenID Connect) sign-in for super admins, HTTPS with HSTS,
forwarded-header handling for a reverse proxy, a rate limit on the launch endpoint, security headers
(`frame-ancestors 'self'`, `X-Frame-Options: SAMEORIGIN`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`)
and a `/healthz` page that answers `ok` without signing in. `/` redirects to `/querybuilder/`. The hub is always served from
the root of its own host name; it cannot live under a path (there is no `UsePathBase`).

You need: .NET 10 on the server, an empty SQL Server database for the hub's own data (never one an embedded QueryBuilder uses),
and a Microsoft Entra tenant for the super admins.

## 1. Register the hub in Microsoft Entra

In the Azure portal, **Microsoft Entra ID**, **App registrations**, **New registration**:

1. **Name**: for example `QueryBuilder Hub`. **Supported account types**: *Accounts in this organizational directory only*
   (single tenant).
2. **Redirect URI**: platform **Web**, `https://<your hub host>/signin-oidc`. This is the host's `Hub:Entra:CallbackPath`
   (default `/signin-oidc`); the two must match exactly.
3. After creating it, copy the **Application (client) ID** and **Directory (tenant) ID**.
4. **Certificates & secrets**, **New client secret**. Copy the secret value now (it is shown once) and note its expiry date:
   when it expires, sign-in stops until you replace it. The host authenticates with a client secret; certificate
   authentication is not supported.
5. Under **Enterprise applications**, open the same app, **Properties**, and set **Assignment required?** to **Yes**. Then
   **Users and groups**: assign only the people (or the one security group) who may use the hub. Without this, anyone in your
   tenant can complete the Entra sign-in; the hub's own allowlist (below) would still refuse them, but they would show up in
   the audit log as refusals.
6. Optional, only if you allow super admins by **group** (`Hub:SuperAdmins:GroupIds`): in the app registration's
   **Manifest** set `"groupMembershipClaims": "SecurityGroup"` so the ID token carries a `groups` claim. When a user is in too
   many groups, Entra leaves the claim out of the token; such a user can only match by object id or email.

The host asks for the `openid`, `profile` and `email` scopes. It reads the `tid` (tenant), `oid` (object id), `email` or
`preferred_username` or `upn`, `name` and `groups` claims from the ID token.

## 2. Settings

Settings live in the `Hub` section of configuration. The committed `appsettings.json` holds placeholders only; real values go
in environment variables or a file that exists only on the server (see section 3).

| Setting | Required | Default | What it does |
|---|---|---|---|
| `Hub:ConnectionString` | yes | none | The hub's own SQL Server metadata database. It must be empty, or already a hub database. |
| `Hub:Audience` | yes | none | The hub's public https URL, for example `https://hub.example.com`. Every launch token's `aud` must equal it exactly. The host refuses to start if it is not an absolute `https://` URL. |
| `Hub:Entra:TenantId` | yes | none | Directory (tenant) ID. |
| `Hub:Entra:ClientId` | yes | none | Application (client) ID. |
| `Hub:Entra:ClientSecret` | yes | none | The client secret. Environment variable or server-only file only. |
| `Hub:Entra:Instance` | no | `https://login.microsoftonline.com/` | Change only for a sovereign cloud. The authority becomes `<Instance><TenantId>/v2.0`. |
| `Hub:Entra:CallbackPath` | no | `/signin-oidc` | Must match the registered redirect URI's path. |
| `Hub:SuperAdmins:TenantIds` | yes | none | Entra tenant ids super admins may sign in from. An object id or email means nothing outside the tenant that issued it, so at least one is required. |
| `Hub:SuperAdmins:ObjectIds` | one of these three | none | Entra object ids (the `oid` claim) of super admins. The most reliable choice. |
| `Hub:SuperAdmins:Emails` | one of these three | none | Email or user principal names, compared ignoring case. A tenant administrator can change a user's email, so prefer object ids or groups. |
| `Hub:SuperAdmins:GroupIds` | one of these three | none | Entra security group ids. Needs the `groups` claim (step 6 above). |
| `Hub:DataSourceKey` | yes | none | Base64 of 32 random bytes. Encrypts the stored data source passwords **and the session key ring** (section 5). Make one with `openssl rand -base64 32`. |
| `Hub:PreviousDataSourceKeys` | no | empty | Old keys, read-only, kept while rotating (see [`MANAGING-KEYS.md`](../../MANAGING-KEYS.md)). |
| `Hub:LaunchRateLimit:PermitLimit` | no | `30` | Launch requests allowed per client address per window. Must be at least 1. |
| `Hub:LaunchRateLimit:WindowSeconds` | no | `60` | The window length. Must be at least 1. |
| `Hub:ForwardedHeaders:KnownProxies` | no | none | IP addresses of reverse proxies whose `X-Forwarded-*` headers are trusted. |
| `Hub:ForwardedHeaders:KnownNetworks` | no | none | The same, as CIDR ranges such as `10.0.0.0/8`. |
| `Hub:DefaultMaxRecords` | no | none | The row limit (1 to 100,000) for data sources that have none of their own. An app's record ceiling still applies on top. |
| `Hub:ApplyMigrations` | no | `true` | Set to `false` when a DBA applies the schema script (section 7): the host then never runs DDL. |
| `AllowedHosts` | no | `*` | Set to the hub's public host name, for example `hub.example.com`. |

The host refuses to start, with a message that names the setting, if a required one is missing, the data source key is not
base64 of 32 bytes, an old key repeats the current one, or the allowlist has a tenant but no object id, email or group (a hub
nobody can administer is refused).

Super admins are checked against `Hub:SuperAdmins` on every request, not only at sign-in. The options are read once when the
process starts, so removing someone means editing the setting and restarting. A user matched through a **group** is checked
against Entra membership only at sign-in: removing them from the group ends nothing until their session ends (at most 8
hours) or you remove the group id from the setting and restart.

## 3. Keep secrets out of the repository

The Entra client secret, the data source key and the connection string never go in `appsettings.json` or source control.
Use environment variables (a double underscore stands for `:`):

```
Hub__ConnectionString=Server=...;Database=QueryBuilderHub;...
Hub__Audience=https://hub.example.com
Hub__Entra__TenantId=<tenant guid>
Hub__Entra__ClientId=<client guid>
Hub__Entra__ClientSecret=<client secret>
Hub__DataSourceKey=<base64 of 32 bytes>
Hub__SuperAdmins__TenantIds__0=<tenant guid>
Hub__SuperAdmins__ObjectIds__0=<your object id>
Hub__PreviousDataSourceKeys__0=<old key, while rotating>
Hub__ForwardedHeaders__KnownProxies__0=10.0.0.4
AllowedHosts=hub.example.com
```

On Azure App Service these are **Environment variables** (or Key Vault references); in a container they are `-e` or secrets; on
IIS they are set per app pool or in a server-only file. Keep a copy of the data source key in a password manager: if it is
lost, stored data source passwords cannot be read again. Never put a real value in the repository.

## 4. HTTPS and a reverse proxy

The hub **requires HTTPS**. Its session cookie is named `__Host-qb-hub`; browsers refuse a `__Host-` cookie that is not
`Secure`, so over plain http nobody can sign in. The host redirects http to https and sends HSTS (except in Development).

If a reverse proxy, load balancer or an Azure service terminates TLS in front of the hub, the host must trust that proxy's
`X-Forwarded-For` and `X-Forwarded-Proto` headers, and only that proxy's:

- Set `Hub:ForwardedHeaders:KnownProxies` (addresses) or `KnownNetworks` (CIDR ranges). With neither set, only a proxy on
  the same machine (loopback) is trusted. **Configuring either one replaces that loopback default**, so include loopback too
  if the proxy runs beside the hub.
- Wrong values have two effects at once: every request looks like plain http (so the `__Host-` cookies fail and sign-in
  loops), and every user appears to come from the proxy's address, so they all share one launch rate-limit allowance.
- `ForwardLimit` is 1: only the last proxy hop is read. With chained proxies (for example Front Door in front of an
  Application Gateway) list the address of the **last** hop, the one that connects to the hub, and make sure it passes on the
  client address. `X-Forwarded-Prefix` is ignored.
- **To check it**: open `https://<hub>/healthz` (must answer `ok`), then sign in as a super admin. If sign-in returns to the
  sign-in page, or the audit log shows one address for every user, the proxy settings are wrong.

The launch limit is **per client address** (30 per minute by default, then `429` with `Retry-After`). A whole office behind one
NAT shares it: if people launch in bursts, raise `Hub:LaunchRateLimit:PermitLimit`.

Set `AllowedHosts` to the hub's public host name so requests with another `Host` header are refused. Set `Hub:Audience` to the
hub's public https URL; it must equal the `aud` your apps put in their tokens. Do not serve the hub under a sub-path.

## 5. Running several instances

Every instance must use the same metadata database and the same `Hub:DataSourceKey` (and `Hub:PreviousDataSourceKeys`).

The hub stores its Data Protection keys (the ones that protect the session cookie) **in its own metadata database,
encrypted with the data source key**. **Do not configure Data Protection yourself** (no `PersistKeysTo...`, no
`ProtectKeysWith...`, no shared folder): hub mode takes over where the keys are stored and how they are encrypted, and
the host in this folder configures nothing of its own.

Because session keys are encrypted with `Hub:DataSourceKey`, rotating the key has one extra rule. After you move to a new key,
keep the old one in `Hub:PreviousDataSourceKeys` for **at least 90 days plus 8 hours** (Data Protection keys live 90 days and a
session lasts up to 8 hours), not only until **Re-encrypt** is done. Remove it sooner and sessions signed with an
older key ring entry stop working. See [`MANAGING-KEYS.md`](../../MANAGING-KEYS.md).

If you start all hosted services at once (`HostOptions.ServicesStartConcurrently = true`), the startup checks that claim the
database and the key ring can run out of order; keep the default.

## 6. First run

1. Create an **empty** database for the hub and a SQL login or identity with read and write rights on it. With
   `Hub:ApplyMigrations` left at `true`, that login also needs the rights to create and alter tables on the first start.
2. Set the settings above and start the host. It applies the schema and records `MetadataSettings` `Mode` = `Hub`: the hub
   **claims** the database. A database that already holds QueryBuilder data but no recorded mode is refused ("the hub
   needs its own empty metadata database"), and so is one recorded as an embedded installation. The built-in *Default* app
   (the one embedded mode uses) is turned off in the same step, since nothing can launch into it.
3. Open `https://<hub>/querybuilder/signin`. Entra signs you in, the hub checks the allowlist, and you land as a super admin. A
   super admin has no app yet: pick one with the switcher, or open the console at `/querybuilder/hub/apps`.
4. **Create the first app**: **New app**, then give it a name and a key (2 to 63 characters: lowercase letters, digits,
   hyphens). The app's detail page has: **Scope keys** (the names its tokens may send, at most 20), **Record ceiling**, **App
   admins can add data sources** (off by default), **Signing keys** (paste the public key the app owner gives you and compare
   the fingerprint with theirs) and **Return URLs** (up to 20). Turn the app on only when its owner is ready.
5. Add the app's **data sources** while viewing it as a super admin (a data source added by a super admin is *locked*:
   app admins use it but cannot change it, until you unlock it). The hub never uses its own metadata database as a data
   source, and refuses a data source that points at it.
6. Send the app owner the app key, the audience (`Hub:Audience`), the scope keys and the
   [`QueryBuilder.Launch` guide](../QueryBuilder.Launch/README.md).

Turning an app off, or revoking one of its keys, ends that app's sessions within about 30 seconds.

## 7. Upgrades and DBA-run schema scripts

By default the host migrates the database itself at start. If the login has no DDL rights, set `Hub:ApplyMigrations` to
`false` and have your DBA run the idempotent script from
[`QueryBuilder.Editor/Scripts`](../QueryBuilder.Editor/Scripts/README.md) before each upgrade (with `sqlcmd`, pass `-I`).
The hub's login then needs `SELECT`, `INSERT`, `UPDATE` and `DELETE` on all QueryBuilder tables, including `MetadataSettings`
(the first start inserts the mode row; if you would rather not grant that, your DBA can insert `('Mode', 'Hub')`
and the hub then only reads it), `LaunchReplays` and `DataProtectionKeys`.

## 8. Troubleshooting

| What you see | Why | Fix |
|---|---|---|
| Host won't start, `Hub:... is required` | A required setting is empty | Set it (environment variable names are in section 3) |
| Host won't start, "needs its own empty metadata database" | The database holds QueryBuilder data from an embedded app | Give the hub a new empty database |
| Host won't start, "belongs to an Embedded-mode installation" | The database was claimed by an embedded host | Same: a separate database |
| After Entra sign-in you are sent back to sign in again | The proxy's headers are not trusted, so the app believes it is on http | Fix `KnownProxies`/`KnownNetworks` (section 4) |
| `AADSTS50011` (reply URL mismatch) at Entra | The redirect URI differs from `https://<host>` + `Hub:Entra:CallbackPath` | Register the exact URL |
| After Entra sign-in you get the generic page "This link to QueryBuilder isn't valid or has expired" (403) | Your tenant id, object id, email or group is not in `Hub:SuperAdmins`, or you belong to too many groups for Entra to list them | Add the entry and restart. Another super admin can read the reason in the audit log (`SuperAdminSignInRefused`) |
| An app's users get the generic "link isn't valid" page | See the [launch troubleshooting table](../QueryBuilder.Launch/README.md#6-troubleshooting) | Read the `LaunchRefused` reason in `/querybuilder/hub/audit` |
| Users all launch fine for a while, then `429` | Launch rate limit per address | Raise `Hub:LaunchRateLimit:PermitLimit`, check proxy settings |
