# QueryBuilder Hub

The deployable host for QueryBuilder in **hub mode**: one QueryBuilder serving several apps. Each app hands its users over
with a signed launch token (see [`QueryBuilder.Launch`](../QueryBuilder.Launch/README.md)); your own staff, the super admins,
sign in with your identity provider (Microsoft Entra ID or any OpenID Connect provider), with one-time sign-in codes, or with
both. This is an application you deploy (`net10.0`), not a NuGet package.

What the host adds around the `QueryBuilder.Editor` package: OpenID Connect sign-in for super admins, one-time sign-in codes, HTTPS with HSTS,
forwarded-header handling for a reverse proxy, a rate limit on the launch endpoint, security headers
(`frame-ancestors 'self'`, `X-Frame-Options: SAMEORIGIN`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`)
and a `/healthz` page that answers `ok` without signing in. `/` redirects to `/querybuilder/`. The hub is always served from
the root of its own host name; it cannot live under a path (there is no `UsePathBase`).

You need: .NET 10 on the server, an empty SQL Server database for the hub's own data (never one an embedded QueryBuilder uses),
and a way for the super admins to sign in (section 1).

## 1. Choose how super admins sign in

There are two options. Use either one, or both; the host refuses to start with neither.

| Option | Choose it when | Settings |
|---|---|---|
| **An identity provider** | Your staff already have accounts in Microsoft Entra ID, or in another OpenID Connect provider (Google Workspace, Okta, Auth0, Keycloak and others) | `Hub:SignIn:Provider:*`. `Kind` is `Entra` or `OpenIdConnect`. |
| **One-time sign-in codes** | You have no identity provider, you want a way in that does not depend on one (for example while setting it up, or if it is down), or only a few operators need access | `Hub:SignIn:Codes:*` and `Hub:SuperAdmins:CodeNames` |

With both on, `https://<hub>/querybuilder/signin` shows a small page with a **Sign in with ...** button (the text comes from
`Hub:SignIn:Provider:DisplayName`) and a field for a code. With only a provider, it goes straight to the provider. With only
codes, it shows the code field. Launch tokens can never make anyone a super admin, whichever option you use.

**Moving from an earlier build:** the old `Hub:Entra:*` section is gone, and a host that still has any `Hub:Entra` value
refuses to start with "Hub:Entra is no longer used. Move those settings to Hub:SignIn:Provider and set Hub:SignIn:Provider:Kind
to Entra." Rename each setting: `Hub:Entra:TenantId`, `ClientId`, `ClientSecret`, `Instance` and `CallbackPath` become
`Hub:SignIn:Provider:TenantId`, `ClientId`, `ClientSecret`, `Instance` and `CallbackPath` (environment variables:
`Hub__Entra__ClientSecret` becomes `Hub__SignIn__Provider__ClientSecret`), and add `Hub:SignIn:Provider:Kind` = `Entra`.
`Hub:SuperAdmins:*` is unchanged.

### 1a. Microsoft Entra ID (`Kind` = `Entra`)

In the Azure portal, **Microsoft Entra ID**, **App registrations**, **New registration**:

1. **Name**: for example `QueryBuilder Hub`. **Supported account types**: *Accounts in this organizational directory only*
   (single tenant). A multi-tenant registration would let users of any tenant reach the sign-in, so the host refuses a
   `Hub:SignIn:Provider:TenantId` of `common`, `organizations` or `consumers`: it must name one tenant.
2. **Redirect URI**: platform **Web**, `https://<your hub host>/signin-oidc`. This is the host's
   `Hub:SignIn:Provider:CallbackPath` (default `/signin-oidc`); the two must match exactly.
3. After creating it, copy the **Application (client) ID** and **Directory (tenant) ID**.
4. **Certificates & secrets**, **New client secret**. Copy the secret value now (it is shown once) and note its expiry date:
   when it expires, sign-in stops until you replace it. The host authenticates with a client secret; certificate
   authentication is not supported.
5. Under **Enterprise applications**, open the same app, **Properties**, and set **Assignment required?** to **Yes**. Then
   **Users and groups**: assign only the people (or the one security group) who may use the hub. Assigning a group needs Microsoft Entra ID P1 or higher (assigning individual
   users works on the Free tier). Without this, anyone in your
   tenant can complete the Entra sign-in; the hub's own allowlist (below) would still refuse them, but they would show up in
   the audit log as refusals.
6. Optional, only if you allow super admins by **group** (`Hub:SuperAdmins:GroupIds`): in the app registration's
   **Manifest** set `"groupMembershipClaims": "SecurityGroup"` so the ID token carries a `groups` claim. When a user is in too
   many groups, Entra leaves the claim out of the token; such a user can only match by object id or email.

Then set `Hub:SignIn:Provider:Kind` to `Entra`, plus `TenantId`, `ClientId` and `ClientSecret` (section 2). The host asks for
the `openid`, `profile` and `email` scopes (`Hub:SignIn:Provider:Scopes` can replace the list, but it must include `openid`).
It reads the `tid` (tenant), `oid` (object id), `email` or `preferred_username` or `upn`, `name` and `groups` claims from the
ID token. With Entra the allowlist is `Hub:SuperAdmins:TenantIds` (required) plus `ObjectIds`, `Emails` or `GroupIds`; the
`sub` claim never matches anything, and `UserIds` is not used.

### 1b. Another OpenID Connect provider (`Kind` = `OpenIdConnect`)

Register the hub as a **confidential web application** in your provider (one that has a client secret and uses the
authorization code flow; the host also uses PKCE) and note the **client id** and **client secret**.

1. **Redirect URI**: `https://<your hub host>/signin-oidc` (the default `Hub:SignIn:Provider:CallbackPath`; the two must
   match exactly).
2. Set `Hub:SignIn:Provider:Kind` to `OpenIdConnect`, `Hub:SignIn:Provider:Authority` to the provider's issuer address (an
   absolute `https://` address; the host reads its `.well-known/openid-configuration`), and `ClientId` and `ClientSecret`.
   `Hub:SignIn:Provider:DisplayName` is the text of the sign-in button ("Sign in with ...").
3. List who may sign in in `Hub:SuperAdmins`, at least one entry in `UserIds`, `Emails` or `GroupIds`:
   - `UserIds`: the person's `sub` claim, the provider's own stable id for them. Compared exactly (ids that are GUIDs also
     match ignoring case and braces). **Use this where you can.**
   - `Emails`: matched ignoring case, **and only when the provider says the email is verified** (`email_verified` is `true`).
     Many providers let users set an email nobody checked; without that claim, an email entry never matches.
   - `GroupIds`: matched against the claim named by `Hub:SignIn:Provider:GroupsClaim` (default `groups`). The claim name is
     the provider's raw name; the host does not rename claims.
   - `TenantIds` and `ObjectIds` mean nothing here. If you leave them set, the host starts but logs a warning naming them.
4. The host reads the provider's userinfo endpoint as well as the ID token, so `email`, `email_verified` and the groups claim
   may come from either. The allowlist looks only at `sub`, `email`, `email_verified` and the groups claim.
5. **To find your own `sub`**: try to sign in once. The refusal page is deliberately generic, but the refused sign-in is in the
   hub audit log (`SuperAdminSignInRefused`, which records the `sub` it saw as `objectId`), where an existing super admin (or
   a sign-in code session) can read it. Add it to `UserIds` and restart. You can also read it from the provider's own user page.

What to expect from common providers (check your provider's documentation; these are starting points):

| Provider | `Authority` looks like | What `sub` is | Groups |
|---|---|---|---|
| Google Workspace | `https://accounts.google.com` | A long number (about 21 digits), the same for that Google account everywhere. It is not the email. | Google does not put groups in the token, so use `UserIds`, or `Emails` (Google sets `email_verified`). |
| Okta | `https://<your-org>.okta.com` (or `.../oauth2/default`) | A short id starting with `00u`. | Add a **groups claim** to the app's sign-on settings (a claim named `groups`). Okta fills it with group names unless you choose otherwise, and `GroupIds` must hold whatever the claim contains. |
| Auth0 | `https://<your-tenant>.auth0.com/` | `auth0\|<id>` for database users, `google-oauth2\|<id>` and similar for social logins. Copy it exactly, including the part before the `\|`. | Auth0 puts no groups in the token by default; add them with an Action, as a claim whose name is a full URL (set `GroupsClaim` to that exact name). |
| Keycloak | `https://<host>/realms/<realm>` | A GUID, the user id. | Add a **Group Membership** mapper to the client, with the claim name you want (for example `groups`) and **Full group path** off; the values are group names, so list those in `GroupIds`. |

If the provider you use signs people in with a different, non-OpenID-Connect protocol (for example SAML only), it is not
supported: use sign-in codes, or put an OpenID Connect broker in front of it.

### 1c. One-time sign-in codes

A code is a short-lived, single-use sign-in an operator creates **on the server**. There is no screen or endpoint that creates
one, so someone who is not already on the server cannot get one.

1. Turn them on and name who may be given one. Set `Hub:SignIn:Codes:Enabled` to `true` and put at least one name in
   `Hub:SuperAdmins:CodeNames` (for example `Hub__SuperAdmins__CodeNames__0=ops-nagendra`). The name identifies the person or
   role the code is for; it is compared exactly, **including case**. The host refuses to start with codes on and no name.
2. Restart the hub (settings are read once when the process starts).
3. Create a code from the app folder of the deployed hub, with the same configuration and environment variables as the site:

   ```
   QueryBuilder.Hub signin-code --name ops-nagendra --minutes 10
   ```

   (`QueryBuilder.Hub.exe` on Windows) or, where there is no executable, `dotnet QueryBuilder.Hub.dll signin-code --name ops-nagendra --minutes 10`.
   `--minutes` is optional: the default is `Hub:SignIn:Codes:DefaultMinutes` (15), and it must be from 1 to 60. The command
   prints two lines and exits with 0:

   ```
   Code: <a long string of letters and digits with one dot in it>
   Expires: 2026-11-12 10:15:00Z
   ```

   (the time is UTC). If something is wrong it prints `Could not issue a sign-in code: <reason>` on the error output and exits
   with 1. It needs the hub's database (it writes the audit record before it prints the code), reads the `appsettings` file in
   its own folder, and starts no web server and runs no migrations. Other `--Setting:Name=value` arguments override
   configuration, as for the site.
   **On IIS or Azure App Service**, run it from the site's own console (on App Service, the Kudu console, `https://<app>.scm.azurewebsites.net`),
   in the folder that holds `QueryBuilder.Hub.dll`: the environment variables and app settings you set for the site are
   visible there. If you run it from somewhere else, give it the same `Hub__...` values.
4. Open `https://<hub>/querybuilder/signin`, paste the code into **One-time sign-in code** and press **Sign in with code**. You
   land in the console as a super admin; the session lasts at most 8 hours and ends after 1 hour unused, like any other.

How codes behave:

- **Single use, short life.** A code works once. The default life is 15 minutes (`Hub:SignIn:Codes:DefaultMinutes`) and the
  longest possible is 60. Clocks may differ by up to 30 seconds.
- **One deployment only.** A code carries the hub's `Hub:Audience` and is signed with a key derived from `Hub:DataSourceKey`, so
  it works only on a hub with both the same, and a launch token can never be used as a code or the reverse.
- **Who it is.** A code session is its own identity, `super:code:<name>` in the audit log (for example
  `super:code:ops-nagendra`). It is separate from the same person's identity-provider sign-in, so their audit trail shows which way they
  came in. Audit entries: `SuperAdminCodeIssued` (name, minutes and a code id; written by the command, never the code itself),
  then `SuperAdminSignedIn` with method `code`.
- **Revoking.** Remove the name from `Hub:SuperAdmins:CodeNames` and restart: no new code for it works, and any session it
  already started ends at its next request. Setting `Hub:SignIn:Codes:Enabled` to `false` and restarting ends every code
  session. Codes already created but not used need no action beyond that: they expire within 60 minutes.
- **Rate limit.** `POST /querybuilder/signin/code` is limited per client address: 10 attempts per 60 seconds by default
  (`Hub:SignIn:Codes:PermitLimit` and `WindowSeconds`), then `429` with `Retry-After`. The proxy settings in section 4 matter
  here too: without them everyone shares one address.
- **Refusals** show the same generic page as any refused sign-in (403) and write `SuperAdminSignInRefused` to the hub audit
  log with the reason (`OriginMismatch`, `Malformed`, `BadSignature`, `WrongPurpose`, `WrongAudience`, `Expired`, `NotYetValid`,
  `LifetimeTooLong`, `NotListed` or `Replayed`), never the code. The form must be posted from the hub's own page: a request
  without an `Origin` header equal to the hub's own address is refused.
- **Handle codes like passwords.** Whoever holds an unused code is a super admin for up to 8 hours. Never paste one into a
  ticket, a chat or an email; use the shortest `--minutes` that works; give it only to the person it is for; and look for
  `SuperAdminCodeIssued` entries you did not expect.

## 2. Settings

Settings live in the `Hub` section of configuration. The committed `appsettings.json` holds placeholders only; real values go
in environment variables or a file that exists only on the server (see section 3).

| Setting | Required | Default | What it does |
|---|---|---|---|
| `Hub:ConnectionString` | yes | none | The hub's own SQL Server metadata database. It must be empty, or already a hub database. |
| `Hub:Audience` | yes | none | The hub's public https address, for example `https://hub.example.com`: scheme, host and optional port only, no path, query, fragment or trailing slash. Every launch token's `aud` must equal it exactly. The host refuses to start otherwise. |
| `Hub:SignIn:Provider:Kind` | one of provider or codes | empty | Empty (no identity provider; codes only), `Entra` or `OpenIdConnect`. Exactly that spelling. |
| `Hub:SignIn:Provider:DisplayName` | no | `your organisation` | The text on the sign-in button ("Sign in with ..."). |
| `Hub:SignIn:Provider:ClientId` | with a `Kind` | none | Application (client) ID. |
| `Hub:SignIn:Provider:ClientSecret` | with a `Kind` | none | The client secret. Environment variable or server-only file only. |
| `Hub:SignIn:Provider:TenantId` | `Entra` | none | Directory (tenant) ID. `common`, `organizations` and `consumers` are refused. |
| `Hub:SignIn:Provider:Instance` | no | `https://login.microsoftonline.com/` | `Entra` only. Change only for a sovereign cloud; must be an absolute `https://` address. The authority becomes `<Instance><TenantId>/v2.0`. |
| `Hub:SignIn:Provider:Authority` | `OpenIdConnect` | none | The provider's issuer address, an absolute `https://` address. |
| `Hub:SignIn:Provider:CallbackPath` | no | `/signin-oidc` | Must match the registered redirect URI's path. |
| `Hub:SignIn:Provider:Scopes` | no | empty (`openid`, `profile`, `email`) | If you list scopes, `openid` must be one of them. |
| `Hub:SignIn:Provider:GroupsClaim` | no | `groups` | `OpenIdConnect` only: the claim that carries group ids. |
| `Hub:SignIn:Codes:Enabled` | no | `false` | Turns on one-time sign-in codes (section 1c). Needs at least one `Hub:SuperAdmins:CodeNames`. |
| `Hub:SignIn:Codes:DefaultMinutes` | no | `15` | How long a new code lives when the command does not say. 1 to 60. |
| `Hub:SignIn:Codes:PermitLimit` | no | `10` | Code attempts allowed per client address per window. Must be at least 1. |
| `Hub:SignIn:Codes:WindowSeconds` | no | `60` | The window length. Must be at least 1. |
| `Hub:SuperAdmins:TenantIds` | `Entra` | none | Entra tenant ids super admins may sign in from. An object id or email means nothing outside the tenant that issued it, so at least one is required. Ignored (with a warning) for `OpenIdConnect`. |
| `Hub:SuperAdmins:ObjectIds` | `Entra`: one of these three | none | Entra object ids (the `oid` claim) of super admins. The most reliable choice. Ignored (with a warning) for `OpenIdConnect`. |
| `Hub:SuperAdmins:UserIds` | `OpenIdConnect`: one of these three | none | The provider's `sub` values of super admins. The most reliable choice. |
| `Hub:SuperAdmins:Emails` | one of these three | none | Email or user principal names, compared ignoring case. With `OpenIdConnect` only when the provider reports `email_verified`. A provider or tenant administrator can change a user's email, so prefer ids or groups. |
| `Hub:SuperAdmins:GroupIds` | one of these three | none | Group ids from the groups claim. With Entra this needs the `groups` claim (step 6 above). |
| `Hub:SuperAdmins:CodeNames` | with codes on | none | Names a sign-in code may be issued for, compared exactly including case. |
| `Hub:DataSourceKey` | yes | none | Base64 of 32 random bytes. Encrypts the stored data source passwords **and the session key ring** (section 5). Make one with `openssl rand -base64 32`. |
| `Hub:PreviousDataSourceKeys` | no | empty | Old keys, read-only, kept while rotating (see [`MANAGING-KEYS.md`](../../MANAGING-KEYS.md)). |
| `Hub:LaunchRateLimit:PermitLimit` | no | `30` | Launch requests allowed per client address per window. Must be at least 1. |
| `Hub:LaunchRateLimit:WindowSeconds` | no | `60` | The window length. Must be at least 1. |
| `Hub:ForwardedHeaders:KnownProxies` | no | none | IP addresses of reverse proxies whose `X-Forwarded-*` headers are trusted. |
| `Hub:ForwardedHeaders:KnownNetworks` | no | none | The same, as CIDR ranges such as `10.0.0.0/8`. |
| `Hub:ForwardedHeaders:ForwardLimit` | no | `1` | How many proxy hops to read from `X-Forwarded-For`, counted from the right. Must be at least 1. Set it to the number of proxies in front of the hub (section 4). |
| `Hub:DefaultMaxRecords` | no | none | The row limit (1 to 100,000) for data sources that have none of their own. An app's record ceiling still applies on top. |
| `Hub:ApplyMigrations` | no | `true` | Set to `false` when a DBA applies the schema script (section 7): the host then never runs DDL. |
| `AllowedHosts` | no | `*` | Set to the hub's public host name, for example `hub.example.com`. |

The host refuses to start if a required setting is missing, the data source key is not base64 of 32 bytes, an old key
repeats the current one, there is neither an identity provider (`Kind`) nor sign-in codes, codes are on with no `CodeNames`,
or a provider is set but the allowlist for it is empty (a hub nobody can administer is refused). A missing `Hub:*` value
names that setting; the other refusals name the package's option instead (see the troubleshooting table). A message names
settings, never their values.

Super admins are checked against `Hub:SuperAdmins` on every request, not only at sign-in. The options are read once when the
process starts, so removing someone (an id, an email, a group or a code name) means editing the setting and restarting. A user
matched through a **group** is checked against the provider's membership only at sign-in: removing them from the group ends
nothing until their session ends (at most 8 hours) or you remove the group id from the setting and restart. Turning a sign-in
method off, or switching `Kind`, and restarting ends the sessions that method made.

## 3. Keep secrets out of the repository

The identity provider's client secret, the data source key and the connection string never go in `appsettings.json` or source control.
Use environment variables (a double underscore stands for `:`):

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
- Wrong values have two effects at once: every request looks like plain http (Entra then reports `AADSTS50011` because the
  redirect URI is built as `http://…/signin-oidc`, or sign-in redirects in a loop; the hub's own cookies are always
  `Secure` and keep working), and every user appears to come from the proxy's address, so they all share one launch rate-limit allowance.
- `Hub:ForwardedHeaders:ForwardLimit` (default 1) is how many hops of `X-Forwarded-For` are read, counted from the right,
  and every hop it reads must be a known proxy. With **chained proxies** (for example Front Door in front of an Application
  Gateway) list every proxy hop in `KnownProxies`/`KnownNetworks` and set `ForwardLimit` to the number of hops, so the client
  address two hops back is the one used. Alternatively, have the last hop replace `X-Forwarded-For` with the original client
  address. Leave it at 1 with a chain and every user appears to come from the first proxy's address: one shared launch
  rate-limit allowance and wrong addresses in the audit log. `X-Forwarded-Prefix` is ignored.
- **To check it**: open `https://<hub>/healthz` (must answer `ok`), then sign in as a super admin. If Entra answers `AADSTS50011`, sign-in
  loops, or the audit log shows one address for every user, the proxy settings are wrong.

The launch limit is **per client address** (30 per minute by default, then `429` with `Retry-After`). A whole office behind one
NAT shares it: if people launch in bursts, raise `Hub:LaunchRateLimit:PermitLimit`. Sign-in code attempts have their own
limit, also per client address (10 per 60 seconds by default, `Hub:SignIn:Codes:PermitLimit` and `WindowSeconds`).

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

If you start all hosted services at once (`HostOptions.ServicesStartConcurrently = true`), the startup steps that migrate and claim the
database can run out of order; keep the default.

## 6. First run

1. Create an **empty** database for the hub and a SQL login or identity with read and write rights on it. With
   `Hub:ApplyMigrations` left at `true`, that login also needs the rights to create and alter tables on the first start.
2. Set the settings above and start the host. It applies the schema and records `MetadataSettings` `Mode` = `Hub`: the hub
   **claims** the database. A database that already holds QueryBuilder data but no recorded mode is refused ("the hub
   needs its own empty metadata database"), and so is one recorded as an embedded installation. The built-in *Default* app
   (the one embedded mode uses) is turned off in the same step, since nothing can launch into it.
3. Open `https://<hub>/querybuilder/signin`. Your identity provider signs you in (or you paste a sign-in code, section 1c), the
   hub checks the allowlist, and you land as a super admin. A super admin has no app yet: pick one with the switcher, or open
   the console at `/querybuilder/hub/apps`. If you have no provider yet, start with codes only, create the first code, and add
   the provider afterwards.
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
and the hub then only reads it; with that route also run
`UPDATE Apps SET IsEnabled = 0 WHERE Id = '00000000-0000-0000-0000-00000000000d';`, because the hub turns the Default app off when it
claims a database and a DBA-inserted marker skips that step), `LaunchReplays` and `DataProtectionKeys`.

## 8. Troubleshooting

| What you see | Why | Fix |
|---|---|---|
| Host won't start, `Hub:... is required` | A required setting is empty | Set it (environment variable names are in section 3) |
| Host won't start and the message names `DataSourceManagement.EncryptionKey`, `HubOptions.SuperAdmins.TenantIds`, `HubOptions.SuperAdmins` (object ids or user ids, emails or groups), `HubOptions.SuperAdmins.CodeNames`, `HubOptions.SuperAdmins.GroupsClaim`, `HubOptions.SuperAdminSignInScheme`, `HubOptions.SignInCodes` or `HubOptions.Audience` | Those are the package's option names; the host fills them from `Hub:DataSourceKey` (and `Hub:PreviousDataSourceKeys`), `Hub:SuperAdmins:TenantIds`, `Hub:SuperAdmins:ObjectIds` / `UserIds` / `Emails` / `GroupIds` / `CodeNames`, `Hub:SignIn:Provider:GroupsClaim`, `Hub:SignIn:Provider:Kind` (no provider, no codes), `Hub:SignIn:Codes` and `Hub:Audience` | Set the matching `Hub:*` setting. "`SuperAdminSignInScheme` is required ... unless `SignInCodes` is enabled" means neither a `Kind` nor codes is set (section 1) |
| Host won't start, "Hub:Entra is no longer used" | An old `Hub:Entra:*` setting is still present (file or environment variable `Hub__Entra__...`) | Move them to `Hub:SignIn:Provider:*` and set `Kind` to `Entra` (section 1) |
| Host won't start, "Hub:SignIn:Provider:Kind must be empty, Entra or OpenIdConnect" | Misspelled or wrong case | Use exactly `Entra` or `OpenIdConnect`, or leave it empty for codes only |
| Host won't start, "Hub:SignIn:Provider:TenantId must name one tenant, not common, organizations or consumers" | A multi-tenant authority would let users of any tenant in | Use your own tenant's id |
| Host won't start, "Hub:SignIn:Provider:Authority must be an absolute https:// address" (`OpenIdConnect`) or "Hub:SignIn:Provider:Instance must be an absolute https:// address" (`Entra`) | An empty or non-https address | Use the provider's `https://` issuer address (for Entra, leave `Instance` at its default) |
| Host won't start, "Hub:SignIn:Provider:Scopes must include openid" | A custom scope list without `openid` | Add `openid` or empty the list |
| Host won't start, "Hub:SignIn:Codes:PermitLimit must be at least 1" (or `WindowSeconds`) | A zero or negative limit | Set 1 or more |
| Log warning "ProviderKind is OpenIdConnect, so TenantIds, ObjectIds are ignored" | Entra-only lists are still set | Remove them, or set `Kind` to `Entra` |
| `signin-code` prints "Could not issue a sign-in code: ..." | The message says why: codes are turned off (`Hub:SignIn:Codes:Enabled`), the name is not in `Hub:SuperAdmins:CodeNames` (case matters), minutes outside 1 to 60, or a setting or the database is missing from where you ran it | Run it from the site's folder with the site's environment variables; fix the setting |
| Pasting a code gives the generic page (403) | One of the reasons in section 1c: used already (`Replayed`), `Expired` (clocks more than 30 seconds apart count), `WrongAudience` or `BadSignature` (a code from another hub, or another `Hub:DataSourceKey`), `NotListed` (the name was removed), `OriginMismatch` (posted from somewhere other than the hub's sign-in page) | A super admin reads the reason in the audit log (`SuperAdminSignInRefused`); create a new code |
| Pasting a code gives `404` | Codes are turned off | Set `Hub:SignIn:Codes:Enabled` and restart |
| Pasting a code gives `429` | The per-address code limit | Wait for the `Retry-After` seconds; check proxy settings (section 4) |
| Host won't start, "needs its own empty metadata database" | The database holds QueryBuilder data from an embedded app | Give the hub a new empty database |
| Host won't start, "belongs to an Embedded-mode installation" | The database was claimed by an embedded host | Same: a separate database |
| Sign-in loops after Entra, or the redirect goes to an `http://` address | The proxy's headers are not trusted, so the app believes it is on http | Fix `KnownProxies`/`KnownNetworks` (section 4) |
| `AADSTS50011` (reply URL mismatch) at Entra, or a redirect-URI error at another provider | The redirect URI differs from `https://<host>` + `Hub:SignIn:Provider:CallbackPath`, or the proxy's headers aren't trusted (the URI is built as `http://…`) | Register the exact URL |
| After provider sign-in you get the generic page "This link to QueryBuilder isn't valid or has expired" (403) | Entra: your tenant id, object id, email or group is not in `Hub:SuperAdmins`, or you belong to too many groups for Entra to list them. OpenID Connect: your `sub`, verified email or group is not listed (an email without `email_verified` = `true` never matches; check the groups claim name) | Add the entry and restart. Another super admin (or a sign-in code session) can read the reason in the audit log (`SuperAdminSignInRefused`: `NotAllowlisted`, `WrongTenant`, `GroupsOverage`, `NoStableId`) |
| An app's users get the generic "link isn't valid" page | See the [launch troubleshooting table](../QueryBuilder.Launch/README.md#6-troubleshooting) | Read the `LaunchRefused` reason in `/querybuilder/hub/audit` |
| Users all launch fine for a while, then `429` | Launch rate limit per address | Raise `Hub:LaunchRateLimit:PermitLimit`, check proxy settings |
