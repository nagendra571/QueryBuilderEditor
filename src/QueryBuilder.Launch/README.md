# QueryBuilder.Launch

How your app sends a signed-in user to a QueryBuilder hub. This package signs the short-lived launch token and renders
the page that posts it to the hub. It has no package dependencies (RS256 or ES256 only), so it works from .NET
Framework 4.7.2+ and modern .NET. It never creates, stores or exports a private key: it signs with the one you hand it.

```
Your app (user already signed in)            QueryBuilder hub
  1. user clicks "Open QueryBuilder"
  2. your server signs a token with its   ──►  3. browser POSTs the token to
     private key and returns a tiny page         /querybuilder/launch
  4. the page posts the form by itself   ◄──  5. hub checks it against your registered
                                                 PUBLIC key, signs the user in, and
                                                 redirects to /querybuilder/
```

The hub only ever stores your public key. Leaking the hub's database cannot be used to impersonate your app.

## 1. What you need first

Agree these with the hub's super admin before you write code:

| Item | What it is |
|---|---|
| **App key** | The id the hub registered your app under (the token's `iss`). 2 to 63 characters: lowercase letters, digits and hyphens, for example `agency-a`. |
| **Audience** | The hub's public https address exactly as the hub has it in `Hub:Audience` (for example `https://hub.example.com`): scheme, host and optional port, no trailing slash. The token's `aud` must equal it character for character. |
| **Key id (`kid`)** | A name you give each signing key, for example `2026-11`. 1 to 64 characters: letters, digits, `.`, `_`, `-`, starting with a letter or digit. |
| **Scope keys** | The names of the row-level scope values you will send (for example `ProgramId`), declared for your app in the hub console. A token that names a key the hub does not know is refused. |
| **Return URL** (optional) | Where "Back to your app" and logout send the user. Registered in the console, in the canonical form `https://app.example.com/` (https only; `http://localhost` is allowed for development). The token's `returnUrl` must match one registered URL exactly. |

## 2. Make a key pair

Use RSA 2048 to 8192 bits (RS256) or ECDSA on the NIST P-256 curve (ES256). Keep the **private** key only where your app
can read it (a key store, a certificate store, or a file outside the web root that only the app's account can read). Never put
it in `web.config`, `appsettings.json` or source control. Only the **public** key goes to the hub.

### PowerShell 7.4 or later

`ExportPkcs8PrivateKeyPem` and `ExportSubjectPublicKeyInfoPem` are not available before PowerShell 7.4; on older 7.x use openssl.

```powershell
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$rsa.ExportPkcs8PrivateKeyPem() | Set-Content -Encoding ascii qb-launch-private.pem
$rsa.ExportSubjectPublicKeyInfoPem() | Set-Content -Encoding ascii qb-launch-public.pem

# The fingerprint the hub console will show: SHA-256 of the public key, lowercase hex
[Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
```

For ES256, replace the first line with
`$ec = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))`
and use `$ec` in the other lines.

### Windows PowerShell 5.1 (no `ExportRSAPrivateKeyPem`)

5.1 cannot write PEM, so keep the private key in a certificate (it stays in the Windows store or a PFX) and write only the
public key out as PEM. This is RS256 only; use PowerShell 7 or openssl for ES256.

```powershell
$cert = New-SelfSignedCertificate -Subject "CN=my-app-querybuilder-launch" `
    -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy Exportable -KeySpec Signature `
    -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(2) -CertStoreLocation Cert:\CurrentUser\My

# Turn the certificate's public key into a "BEGIN PUBLIC KEY" block (SubjectPublicKeyInfo)
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

# The fingerprint the hub console will show
$sha = [System.Security.Cryptography.SHA256]::Create()
($sha.ComputeHash($spki) | ForEach-Object { $_.ToString('x2') }) -join ''

$cert.Thumbprint    # your app finds the private key by this thumbprint
```

The certificate and its private key stay in `Cert:\CurrentUser\My`. To use it from IIS, put it in the machine store instead
(run the same command with `Cert:\LocalMachine\My` as an administrator) and give the app pool identity read access to the
private key (`certlm.msc`, right-click the certificate, All Tasks, Manage Private Keys). To keep a backup, export it to a PFX with
`Export-PfxCertificate -Cert $cert -FilePath my-app-launch.pfx -Password (Read-Host -AsSecureString)` and store the file and its
password in your password manager, not in the repository.

### openssl

```bash
# RSA (RS256)
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out qb-launch-private.pem
# ... or ECDSA P-256 (ES256)
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out qb-launch-private.pem

openssl pkey -in qb-launch-private.pem -pubout -out qb-launch-public.pem

# The fingerprint the hub console will show
openssl pkey -pubin -in qb-launch-public.pem -outform DER | openssl dgst -sha256
```

Whichever way you made it, the file you send is the one that starts with `-----BEGIN PUBLIC KEY-----`.

## 3. Register the public key

A hub super admin does this in the hub's console (`/querybuilder/hub/apps`, open the app, **Signing keys**, **Add a key**):

1. Enter your **key id**, choose the **algorithm** (RS256 or ES256) and paste the PEM.
2. The console refuses a private key, a certificate, an RSA key under 2048 or over 8192 bits and an EC key that is not P-256.
3. The key list then shows a **fingerprint**: the SHA-256 of the public key as lowercase hex. Compare it with the one you
   computed above. If they differ, the wrong key was pasted.

To retire a key, a super admin chooses **Revoke**. **Revoking a key ends every session of that app** (the hub does not record
which key signed a session's launch), and the key can never be used again, so add the replacement first.

## 4. Add the button

Your page must only offer the button to a user who is signed in to your app, and must work out that user's role and scope
on your server, from your own data, every time. Never take them from the query string or a form field.

The page that comes back is a complete HTML document that posts the token by itself, so return it with `text/html` and
`Cache-Control: no-store`.

### ASP.NET Core (minimal API)

Load the private key once, at startup. The path comes from configuration, the key file from your secret store.

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

For ES256, use `LaunchSigningKey.EcdsaP256("2026-11", ecdsa)` with an `ECDsa` you loaded the same way
(`ECDsa.Create()` then `ImportFromPem`).

### ASP.NET Core (MVC controller or Razor Pages)

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

In a Razor Pages handler, `return Content(...)` works the same way. If your app sends a Content Security Policy with a nonce,
pass it as `LaunchForm.Html(hub, token, cspNonce)` so the page's one script may run. If you send a `form-action` directive,
it must include the hub's origin. Without the script the page still works: it shows a **Continue to QueryBuilder** button.

### ASP.NET MVC 5 on .NET Framework 4.8

The package targets `netstandard2.0`, which .NET Framework 4.7.2 and later can use. Load the private key from the Windows
certificate store (or a PFX the account can read) and take it as an `RSA` with `GetRSAPrivateKey()`. Do not use
`cert.PrivateKey`. On .NET Framework this is an `RSACng` for a key created with the commands above; `LaunchSigningKey` accepts
any `RSA`. PEM loading is not built into .NET Framework, which is why the key lives in a certificate. Keep only the
thumbprint in `web.config`, never the key or a PFX password.

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
app runs: `LaunchSigningKey` does not own it. The `hub` value must be just the scheme, host and port: the hub is never
served under a path.

### What to send

- **`LaunchUser(id, name, email)`**: `id` is how the hub tells your users apart, so it must be stable and never reused for
  another person. Up to 256 characters, and it may not start with `super:` (reserved for the hub's own super admins).
  The hub compares it exactly, but if your app already has an owner or share for the same id in a different case, a launch with
  the differently-cased id is refused (see Troubleshooting). `name` is up to 200 characters, `email` up to 320; both are
  optional and only used for display and the sharing picker.
- **Role**: `Admin` sees and manages everything in your app and can publish dashboards. `Author` builds, saves, charts, runs,
  exports and shares. `Viewer` sees only the dashboards published to your app and those shared with them.
- **Scope**: `LaunchScope.For(...)` (a key may carry several values) or `LaunchScope.Unrestricted`; never both, never neither.
- **Lifetime**: the default is 60 seconds and the maximum is 120. Create the token when the user clicks, not earlier.
- A new launch replaces the user's current hub session, so role and scope changes take effect on the next click. A hub
  session lasts at most 8 hours and ends after 1 hour without use, after which the user clicks your button again.

## 5. Not using .NET: the token by hand

Anything that can sign a JWT can launch. The token must be a compact JWT (three base64url parts, no padding).

**Header**

```json
{ "alg": "RS256", "typ": "JWT", "kid": "2026-11" }
```

`alg` is `RS256` (RSASSA-PKCS1-v1_5 with SHA-256) or `ES256` (ECDSA P-256 with SHA-256, signature as the 64-byte `r || s`
form that JWS defines, not DER). It must be the algorithm the key was registered with. `none` and every HMAC algorithm are refused.

**Claims**

| Claim | Required | Meaning |
|---|---|---|
| `iss` | yes | Your app key. |
| `aud` | yes | The hub's configured audience. A string, or an array that contains it. |
| `sub` | yes | Your user's id. At most 256 characters, not starting with `super:` (any case). |
| `role` | yes | Exactly `Admin`, `Author` or `Viewer` (case-sensitive). |
| `name` | no | Display name, up to 200 characters. |
| `email` | no | Up to 320 characters. |
| `scope` | one of `scope` / `unrestricted` | An object of scope key to array of strings, for example `{"ProgramId":["10"],"RegionId":["US2"]}`. Up to 20 keys, 1,000 values per key, 200 characters per value, no empty arrays or values. Keys are matched to the declared ones ignoring case. |
| `unrestricted` | one of `scope` / `unrestricted` | `true` means no row restriction for this user. Send `scope` or `unrestricted`, never both. |
| `returnUrl` | no | Must equal one of your registered return URLs exactly (the check is case-sensitive). |
| `iat`, `exp` | yes | **Whole seconds** since 1970-01-01 UTC, as JSON integers. A fraction (`1731400000.5`) is refused. `exp - iat` must be at most 120 seconds (60 is a good value). |
| `nbf` | no | If present, an integer; the token is not valid before it. |
| `jti` | yes | A unique id (up to 200 characters), for example 16 random bytes in hex. A token works **once**; reusing an id is refused. |

The scope has a second limit, on the whole: once the hub serialises it for the session cookie (JSON with `System.Text.Json`'s
default escaping, which writes quotes, `&`, `<`, `>`, `'`, `+`, backticks and every non-ASCII character as six bytes), it
may not exceed **6,144 UTF-8 bytes**, roughly 150 to 300 values. It rides in a cookie sent on every request, so send fewer values
and let your own data group them if you are near it. A token over the limit is refused.

The hub allows 30 seconds of clock difference either way, so keep your servers' clocks on NTP. A declared scope key that
the token leaves out means "no value" for that key, so views scoped by it return no rows (the hub fails closed).

A decoded example:

```json
{ "alg": "RS256", "typ": "JWT", "kid": "2026-11" }
{
  "iss": "my-app", "aud": "https://hub.example.com", "sub": "user-42",
  "name": "Mary Smith", "email": "mary@example.com", "role": "Author",
  "scope": { "ProgramId": ["10", "20"] },
  "returnUrl": "https://my-app.example.com/",
  "iat": 1731400000, "exp": 1731400060, "jti": "c3f1a6e0b1d44f5a8f6a2b7d9e0c1a34"
}
```

**Transport.** Send the signed token to the user's browser inside a form that posts to the hub; never in a URL, a header
or a server-to-server call (the hub's session cookie has to land in the user's browser):

```html
<form method="post" action="https://hub.example.com/querybuilder/launch">
  <input type="hidden" name="token" value="eyJhbGciOi...">
  <button type="submit">Continue to QueryBuilder</button>
</form>
<script>document.forms[0].submit();</script>
```

The request must be `application/x-www-form-urlencoded` with exactly one `token` field. A `GET` gets 405 and any other
content type gets 415. On success the hub answers `303` to `/querybuilder/`. On any refusal it shows one generic page
("This link to QueryBuilder isn't valid or has expired") with status 400, and never says why.

## 6. Troubleshooting

The user (and your logs) only ever see the generic page. The **reason** is in the hub: ask a super admin to open the console
audit log (`/querybuilder/hub/audit`) and look at the `LaunchRefused` entries around the time of the click. The entry reads
`Launch refused: <Reason>` and never contains the token. An entry marked "unauthenticated, signature not verified" is one
whose app and key could not be proven, so it is not shown under any app.

A `429` with a `Retry-After` header is not a refusal: the hub limits `/querybuilder/launch` per client address (30 per minute
by default). Everyone behind one office network address shares that limit; the hub's operator can raise it.

| Reason in the audit log | What it means | What to do |
|---|---|---|
| `Malformed` | Not a JWT, a wrong type for a claim, a missing or oversized form field, a fractional `iat`/`exp`, a `sub` over 256 characters or starting with `super:`, a `jti` over 200 characters, a `name` or `email` over its limit, `unrestricted` set to anything but `true` (send `true` or leave it out), an empty string as a scope value | Compare with the claims table; check your JWT library writes `iat`/`exp` as integers |
| `UnknownApp` | `iss` is not a registered app key | Check the app key, character for character |
| `AppDisabled` | The app is turned off in the console | Ask a super admin to turn it on |
| `UnknownKey` | No key with that `kid` for that app | Register the public key under that exact key id |
| `RevokedKey` | That key was revoked | Add a new key, sign with it |
| `AlgorithmNotAllowed` | Not RS256/ES256, or not the algorithm the key was registered with | Register the key with the right algorithm |
| `BadSignature` | The signature does not verify against the registered public key | Wrong key pair, or the token was altered; compare fingerprints (step 3) |
| `WrongAudience` | `aud` differs from the hub's `Hub:Audience` | Use the hub's exact public https URL |
| `Expired` / `NotYetValid` | The clock is off by more than 30 seconds, or the token sat unused | Fix the server clock; create the token on click |
| `LifetimeTooLong` | `exp - iat` is over 120 seconds | Use 60 |
| `Replayed` | The `jti` was already used | Generate a new token each time (the page's Back button or a double click reuses one) |
| `MissingClaim` | `iss`, `iat`, `exp`, `jti`, `sub` or `role` is missing or `null`, or `jti`, `sub` or `role` is blank | Send every required claim |
| `UnknownRole` | `role` is not exactly `Admin`, `Author` or `Viewer` | Fix the spelling and case |
| `ReturnUrlNotRegistered` | `returnUrl` is not one of the app's registered URLs | Register it, or omit `returnUrl` |
| `UndeclaredScopeKey` | A scope key the app (or one of its data sources) has not declared | Ask a super admin to add it to the app's scope keys |
| `ScopeMissing` | Neither `scope` nor `unrestricted`, or a scope with no keys | Send one of them |
| `ScopeAndUnrestricted` | Both were sent | Send only one |
| `ScopeTooLarge` | Too many keys, too many values, a value over 200 characters, an empty value list (an array with no values), or more than 6,144 bytes serialised | Send fewer values (this library checks before signing) |
| `UserIdCaseConflict` | The hub already knows an id that differs from `sub` only by case (as a previous user, an owner or a share) | Send the id exactly as before; the hub treats ids as case-sensitive and will not merge two spellings |

`LaunchToken.Create` and `LaunchScope.For` check most of these before signing and throw with a message that says what to change.

## Limits at a glance

| | |
|---|---|
| Token lifetime | 1 to 120 seconds, default 60; `iat`/`exp` in whole seconds; 30 seconds clock skew allowed |
| Scope | at most 20 keys, 1,000 values per key, 200 characters per value, 6,144 UTF-8 bytes serialised (`LaunchScope.SerializedByteCount`) |
| Scope key name | starts with a letter or `_`; letters, digits and `_`; up to 128 characters |
| `kid` | `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$` |
| Keys | RSA 2048 to 8192 bits, or ECDSA P-256; public key PEM up to 4,000 characters |
| User id / name / email | 256 / 200 / 320 characters |
| Return URL | https (or `http://localhost`), canonical form, up to 800 characters, at most 20 per app |
| Hub session | 8 hours at most, 1 hour idle; role and scope come from the latest launch only |
