# Managing the data source key

A quick guide to the encryption key that protects the saved connection details of data sources added in QueryBuilder's
admin page. Read the first section; jump to the rest only when you need it.

---

## The one-minute version

- **What it is.** When an admin adds a data source in `/admin`, its server name, username and **password** are saved in the
  database **locked** with one key: the data source key.
- **Why it matters.** Someone who copies the database sees only scrambled text. Only an app that has the key can unlock it.
- **The key never expires.** It works until you lose it or change it.
- **Losing the key** means retyping every saved data source's password. Nothing else is lost.
- **Keep the key in one place per app, outside the code**, and keep a backup in a password manager.
- **Data sources from `appsettings.json` connection strings don't use the key at all.** It only matters for sources added
  in the admin page.

| Do | Don't |
|---|---|
| Keep it in user secrets (your machine) or a file that stays only on the server | Put it in `appsettings.json` or anywhere in git |
| Keep a copy in a password manager | Paste it into chat, email or tickets |
| Use the **same** key in every app that shares one database | Give different keys to apps sharing a database |
| Change it with the admin page's **Re-encrypt** button (below) | Just replace it: the old sources would become unreadable |

---

## 1. First-time setup

### Step 1: make a key

Either:

- **Admin page:** `/admin` → **Encryption key** → **Generate a new key**. The key is made in your browser and never sent
  anywhere. Copy it before closing the dialog.
- **PowerShell** (works on Windows PowerShell 5.1 and PowerShell 7):
  ```powershell
  $b = New-Object byte[] 32
  [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
  [Convert]::ToBase64String($b)
  ```
- **Linux/macOS:** `openssl rand -base64 32`

A key looks like 44 characters ending in `=`, for example `q3Jx…Vb8=`.

### Step 2: save a backup

Put it in your password manager now, before anything else.

### Step 3: give it to the app

The host app passes the key to QueryBuilder in `Program.cs`. It reads it from configuration, so the value itself never
sits in code:

```csharp
builder.Services.AddQueryBuilderEditor(options =>
{
    options.DataSourceManagement.EncryptionKey = builder.Configuration["QueryBuilder:DataSourceKey"];
    options.DataSourceManagement.PreviousEncryptionKeys =
        builder.Configuration.GetSection("QueryBuilder:PreviousDataSourceKeys").Get<string[]>() ?? [];  // 1.4.0+
});
```

Then put the value where that app reads configuration, as shown in section 2.

### Step 4: turn on the add/edit form (optional)

The key alone is enough to **use** saved data sources. To **add or edit** them in `/admin`, the host also sets
`options.DataSourceManagement.Enabled = true`, which requires admin pages to be behind a real sign-in
(`AdminAuthorization`). Leave the form off on any public site that has no sign-in or HTTPS.

---

## 2. Where the key lives

| Where the app runs | Put the key in | How |
|---|---|---|
| Your machine | **User secrets** (stored in your Windows profile, never in the project) | `dotnet user-secrets set "QueryBuilder:DataSourceKey" "<key>"` in the project folder |
| A hosted server | **A file that only exists on the server**, e.g. `appsettings.Server.json` next to the app's `.dll` | See below |
| A host with environment variables (Azure, IIS app pool, Docker) | Environment variable `QueryBuilder__DataSourceKey` | Double underscore stands for `:` |

**The server-only file.** Create it once on the server (FTP or the host's file manager):

```json
{ "QueryBuilder": { "DataSourceKey": "<key>" } }
```

and make the app load it, right after `WebApplication.CreateBuilder(args)`:

```csharp
builder.Configuration.AddJsonFile("appsettings.Server.json", optional: true, reloadOnChange: false);
```

It survives publishes as long as the publish profile keeps extra files on the server (`SkipExtraFilesOnServer` = true),
and it never goes into git because it isn't in the project. Check it is still there after the first publish.

To see the key you saved locally: `dotnet user-secrets list` in the project folder, in your own terminal.

---

## 3. Checking that it works

Open `/admin` → **Encryption key**:

```
Current key   c1f4531c          ← a fingerprint, not the key
Previous keys none
2 managed data sources: 2 on the current key, 0 on a previous key, 0 unreadable.
```

- The **fingerprint** is a short tag that lets you tell keys apart without revealing them. Two apps showing the same
  fingerprint have the same key.
- **Unreadable** means no configured key opens that source. See the troubleshooting table below.

---

## 4. Changing the key (rotation)

Do this if the key may have leaked, someone who knew it left, or on a regular schedule. It needs QueryBuilder 1.4.0 or later.

```
 old key only  ──►  new key + old key kept  ──►  Re-encrypt  ──►  new key only
                    (step 2–3)                   (step 4)         (step 5)
```

1. **Generate** a new key (admin page or PowerShell) and save it in your password manager.
2. **Set the new key as the current one and move the old one to the previous list:**
   ```json
   { "QueryBuilder": {
       "DataSourceKey": "<NEW key>",
       "PreviousDataSourceKeys": [ "<OLD key>" ] } }
   ```
   Do this in **every app that shares this database** before step 4.
3. **Restart** the app(s). The admin page now shows sources "On a previous key". Everything keeps working.
4. **Click Re-encrypt with the current key** in `/admin`. Sources are saved again under the new key. It is safe to
   click twice: the second run changes nothing.
5. When the page shows **0 on a previous key**, **remove the old key** from `PreviousDataSourceKeys` everywhere and restart.

> **Warning:** running step 4 before every app sharing the database has the new key leaves those apps unable to read
> the sources until they get it. That's why Re-encrypt is turned off on a site whose admin pages need no sign-in: run it
> from an app that has sign-in, or from your own machine pointed at the same database.

---

## 5. If the key is lost

There is no way to recover it, by design. To get going again:

1. Make a new key and set it (section 1).
2. Restart. Each managed data source shows as **Unreadable**.
3. Open each one in `/admin` and re-enter its password (and check the other details). It's saved with the new key.

Data sources from `appsettings.json` connection strings, saved queries, charts and dashboards are untouched.

---

## 6. Troubleshooting

| What you see | Why | Fix |
|---|---|---|
| App won't start: "`Enabled` needs `EncryptionKey`" | The form is on but there's no key | Set the key, or turn the form off |
| App won't start: "`EncryptionKey` is invalid" | Not base64 of exactly 32 bytes (often a copy/paste cut) | Paste the whole 44-character key again |
| App won't start: "`PreviousEncryptionKeys[n]` …" | That old key is invalid, repeated, the same as the current one, or set with no current key | Fix or remove entry `n` |
| App won't start: "`AdminAuthorization` can't be Anonymous" | The form is on, but admin pages have no sign-in | Add sign-in, or keep the form off |
| A data source says "Couldn't connect to this data source." on a site where it worked elsewhere | That app has a different key, or none | Give it the same key as the other app |
| `/admin` shows sources as **Unreadable** | No configured key opens them | Add the right key, or re-enter each password (section 5) |
| **Re-encrypt** button is greyed out | Nothing is on an old key, or admin pages have no sign-in on this site | Nothing to do, or run it from an app with sign-in |
| Re-encrypt reports "skipped because it changed" | An admin saved that source at the same moment | Click Re-encrypt again |
| Refusals show as "500 Internal Server Error" in Development | The host didn't call `app.UseExceptionHandler()` | Add it to `Program.cs` |

---

## Quick answers

- **Is the key ever sent to the browser?** No. Only the 8-character fingerprint.
- **Is it in the audit log or the logs?** No. The audit log records only counts and fingerprints.
- **Can I hardcode it?** Don't. Anyone with the code or the published `.dll` would have it, and the code already holds
  the database password, so the lock would protect nothing.
- **Does changing it affect users or saved queries?** No. Only the stored connection details are re-saved.
- **One key per data source?** No. One key per database of QueryBuilder settings, shared by every app using it.
