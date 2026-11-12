# QueryBuilder.Launch

Signs the short-lived launch token your app hands to a QueryBuilder hub, and renders the auto-submitting form that
posts it. No package dependencies (RS256 or ES256 only), so it works from .NET Framework 4.7.2+ and modern .NET.

```csharp
using var key = LaunchSigningKey.Rsa("2026-11", rsa);          // your private key; never leaves your app
var scope = LaunchScope.For(new Dictionary<string, IEnumerable<string>> { ["ProgramId"] = new[] { "10", "20" } });
var token = LaunchToken.Create("my-app", "https://hub.example.com", key,
    new LaunchUser("user-42", "Mary Smith", "mary@example.com"), LaunchRole.Author, scope);
return Content(LaunchForm.Html("https://hub.example.com", token), "text/html");
```

Rules the hub enforces and this library checks before signing: token lifetime 1-120 s (default 60), `iat`/`exp` in
whole seconds, a scope of at most 20 keys, 1,000 values per key, 200 characters per value and at most 6,144 UTF-8
bytes once serialised the way the hub stores it (`LaunchScope.SerializedByteCount`); `kid` matching
`^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`; RSA 2048-8192 bits or ECDSA P-256. A full integration guide follows with the
hub release.
