using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class HubRegistryTests
{
    private const string Apps = "/querybuilder/api/hub/apps";

    private static async Task<HubApiHost.Browser> SuperAsync(HubApiHost env)
    {
        var browser = env.NewBrowser();
        await browser.SignInSuperAsync();
        return browser;
    }

    private static async Task<Guid> CreateAppAsync(HubApiHost.Browser super, string key = "new-app", string name = "New app")
    {
        var response = await super.PostAsync(Apps, new { key, name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await HubApiHost.Browser.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static string Fingerprint(string spkiPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(spkiPem);
        return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    }

    [MetadataSqlFact]
    public Task Registry_creates_updates_lists_and_audits_without_key_material() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        var id = await CreateAppAsync(super);

        var updated = await super.PutAsync($"{Apps}/{id}", new { name = "Renamed", isEnabled = true, scopeKeys = new[] { "ProgramId", "Region" } });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(["ProgramId", "Region"], (await HubApiHost.Browser.JsonAsync(updated)).GetProperty("scopeKeys").EnumerateArray().Select(v => v.GetString()));

        using var rsa = RSA.Create(2048);
        var pem = TestTokens.Pem(rsa);
        var added = await super.PostAsync($"{Apps}/{id}/keys", new { kid = "k1", algorithm = "RS256", publicKeyPem = pem });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(Fingerprint(pem), (await HubApiHost.Browser.JsonAsync(added)).GetProperty("fingerprint").GetString());

        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(HttpStatusCode.Created, (await super.PostAsync($"{Apps}/{id}/keys", new { kid = "k2", algorithm = "ES256", publicKeyPem = TestTokens.Pem(ec) })).StatusCode);

        var urls = await super.PutAsync($"{Apps}/{id}/return-urls", new { urls = new[] { "https://a.example/home", "http://localhost:5000/x", "https://a.example/home" } });
        Assert.Equal(["https://a.example/home", "http://localhost:5000/x"], (await HubApiHost.Browser.JsonAsync(urls)).GetProperty("urls").EnumerateArray().Select(v => v.GetString()));

        var list = await HubApiHost.Browser.JsonAsync(await super.GetAsync(Apps));
        var row = list.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == id);
        Assert.Equal("Renamed", row.GetProperty("name").GetString());
        Assert.Equal(2, row.GetProperty("keys").GetArrayLength());
        Assert.Equal(2, row.GetProperty("returnUrls").GetArrayLength());
        Assert.DoesNotContain("BEGIN", row.GetRawText());

        // Replacing the set drops what is no longer listed.
        await super.PutAsync($"{Apps}/{id}/return-urls", new { urls = new[] { "https://b.example/" } });
        await using var db = env.HubWideDb();
        Assert.Equal(["https://b.example/"], await db.AppReturnUrls.Where(u => u.AppId == id).Select(u => u.Url).ToListAsync());

        var audit = await db.AuditLogEntries.Where(a => a.Action == AuditAction.AppRegistryChanged && a.AppId == id).OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal(6, audit.Count);
        Assert.All(audit, a => Assert.Equal("super:admin-oid", a.Actor));
        Assert.All(audit, a => Assert.DoesNotContain("BEGIN", a.DetailsJson));
        Assert.Contains(Fingerprint(pem), audit.Single(a => a.Summary.Contains("'k1'")).DetailsJson);
        using var update = JsonDocument.Parse(audit.Single(a => a.Summary.StartsWith("Updated")).DetailsJson!);
        Assert.Equal("New app", update.RootElement.GetProperty("before").GetProperty("name").GetString());
        Assert.Equal("Renamed", update.RootElement.GetProperty("after").GetProperty("name").GetString());
    });

    [MetadataSqlFact]
    public Task Registry_rejects_bad_keys_and_urls() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        var id = await CreateAppAsync(super);

        async Task<HttpStatusCode> AddKey(string kid, string alg, string pem) =>
            (await super.PostAsync($"{Apps}/{id}/keys", new { kid, algorithm = alg, publicKeyPem = pem })).StatusCode;

        using var small = RSA.Create(1024);
        using var good = RSA.Create(2048);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        // Another 256-bit curve ES256 does not allow. Which ones the platform can create varies, so try both.
        ECDsa? other256 = null;
        foreach (var curve in new[] { "secp256k1", "brainpoolP256r1" })
        {
            try { other256 = ECDsa.Create(ECCurve.CreateFromFriendlyName(curve)); break; }
            catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException or ArgumentException) { }
        }

        Assert.NotNull(other256);
        using var _ = other256;
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("a", "RS256", TestTokens.Pem(small)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("b", "ES256", TestTokens.Pem(p384)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("b2", "ES256", TestTokens.Pem(other256!)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("c", "ES256", TestTokens.Pem(good)));
        foreach (var dotted in new[] { ".", "..", ".x", "-x", "_x" })
        {
            Assert.True(HttpStatusCode.BadRequest == await AddKey(dotted, "RS256", TestTokens.Pem(good)), $"kid '{dotted}'");
        }

        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("a/b", "RS256", TestTokens.Pem(good)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("a b", "RS256", TestTokens.Pem(good)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("a%2Fb", "RS256", TestTokens.Pem(good)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("a?b", "RS256", TestTokens.Pem(good)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("d", "RS256", TestTokens.Pem(p256)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("e", "HS256", TestTokens.Pem(good)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("f", "RS256", good.ExportPkcs8PrivateKeyPem()));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("g", "RS256", good.ExportRSAPrivateKeyPem()));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("h", "RS256", TestTokens.Pem(good) + good.ExportPkcs8PrivateKeyPem()));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("i", "RS256", "not a key"));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("j", "RS256", TestTokens.Pem(good) + new string(' ', 4000)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey(new string('k', 65), "RS256", TestTokens.Pem(good)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("", "RS256", TestTokens.Pem(good)));

        using var huge = RSA.Create(8448);
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("huge", "RS256", TestTokens.Pem(huge)));

        Assert.Equal(HttpStatusCode.Created, await AddKey("ok", "RS256", TestTokens.Pem(good)));
        Assert.Equal(HttpStatusCode.BadRequest, await AddKey("ok", "RS256", TestTokens.Pem(good)));

        async Task<HttpStatusCode> SetUrls(params string[] urls) =>
            (await super.PutAsync($"{Apps}/{id}/return-urls", new { urls })).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("http://a.example/x"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("http://localhost.evil.test/x"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https://a.example/x#frag"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https://user:pw@a.example/x"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("/relative"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("ftp://a.example/x"));
        // Not in the form a browser follows: no authority, or a spelling the URI parser rewrites.
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https:evil.com"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https:/evil.com/x"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https://A.example/x"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https://a.example"));
        var hint = await super.PutAsync($"{Apps}/{id}/return-urls", new { urls = new[] { "https://a.example" } });
        Assert.Equal(HttpStatusCode.BadRequest, hint.StatusCode);
        Assert.Contains("`https://a.example/`", await hint.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https://a.example/a b"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https://a.example:443/x"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("javascript:alert(1)"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https://a.example/" + new string('x', 800)));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls(Enumerable.Range(0, 21).Select(i => $"https://a.example/{i}").ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, await SetUrls("https://a.example/X", "https://a.example/x"));
        Assert.Equal(HttpStatusCode.OK, await SetUrls(Enumerable.Range(0, 20).Select(i => $"https://a.example/{i}").ToArray()));
        Assert.Equal(HttpStatusCode.OK, await SetUrls("https://a.example/" + new string('x', 782)));

        foreach (var bad in new[] { "A", "-ab", "Ab-cd", "ab_c", "ab c", new string('a', 64), "" })
        {
            Assert.True(HttpStatusCode.BadRequest == (await super.PostAsync(Apps, new { key = bad, name = "x" })).StatusCode, $"key '{bad}'");
        }

        Assert.Equal(HttpStatusCode.Created, (await super.PostAsync(Apps, new { key = new string('a', 63), name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(Apps, new { key = "new-app", name = "dup" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(Apps, new { key = "default", name = "dup" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(Apps, new { key = "fine-key", name = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsync(Apps, new { key = "fine-key", name = new string('n', 201) })).StatusCode);

        async Task<HttpStatusCode> Update(object body) => (await super.PutAsync($"{Apps}/{id}", body)).StatusCode;
        Assert.Equal(HttpStatusCode.BadRequest, await Update(new { name = "n", isEnabled = true, scopeKeys = new[] { "bad key" } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Update(new { name = "n", isEnabled = true, scopeKeys = new[] { "A", "a" } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Update(new { name = "n", isEnabled = true, scopeKeys = Enumerable.Range(0, 21).Select(i => $"k{i}").ToArray() }));
        Assert.Equal(HttpStatusCode.BadRequest, await Update(new { name = "n", scopeKeys = Array.Empty<string>() }));
        Assert.Equal(HttpStatusCode.BadRequest, await Update(new { name = " ", isEnabled = true, scopeKeys = Array.Empty<string>() }));

        Assert.Equal(HttpStatusCode.NotFound, (await super.PutAsync($"{Apps}/{Guid.NewGuid()}", new { name = "n", isEnabled = true, scopeKeys = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await super.PutAsync($"{Apps}/{Guid.Empty}", new { name = "n", isEnabled = true, scopeKeys = Array.Empty<string>() })).StatusCode);

        // Nothing rejected above left a key or URL behind.
        await using var db = env.HubWideDb();
        Assert.Equal(["ok"], await db.AppSigningKeys.Where(k => k.AppId == id).Select(k => k.Kid).ToListAsync());
        Assert.Equal(1, await db.AppReturnUrls.CountAsync(u => u.AppId == id));
    });

    [MetadataSqlFact]
    public Task A_kid_with_dots_underscores_and_hyphens_can_be_added_and_revoked_exactly() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        var id = await CreateAppAsync(super);
        using var one = RSA.Create(2048);
        using var two = RSA.Create(2048);
        Assert.Equal(HttpStatusCode.Created, (await super.PostAsync($"{Apps}/{id}/keys", new { kid = "Key.2026_11-a", algorithm = "RS256", publicKeyPem = TestTokens.Pem(one) })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await super.PostAsync($"{Apps}/{id}/keys", new { kid = "key.2026_11-b", algorithm = "RS256", publicKeyPem = TestTokens.Pem(two) })).StatusCode);

        using var three = RSA.Create(2048);
        Assert.Equal(HttpStatusCode.Created, (await super.PostAsync($"{Apps}/{id}/keys", new { kid = "x.y", algorithm = "RS256", publicKeyPem = TestTokens.Pem(three) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await super.SendAsync(HttpMethod.Delete, $"{Apps}/{id}/keys/x.y")).StatusCode);

        // A case variant of an existing kid names no key, and must not revoke the other one.
        Assert.Equal(HttpStatusCode.NotFound, (await super.SendAsync(HttpMethod.Delete, $"{Apps}/{id}/keys/KEY.2026_11-A")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await super.SendAsync(HttpMethod.Delete, $"{Apps}/{id}/keys/Key.2026_11-a")).StatusCode);

        await using var db = env.HubWideDb();
        var keys = await db.AppSigningKeys.Where(k => k.AppId == id).ToListAsync();
        Assert.NotNull(keys.Single(k => k.Kid == "Key.2026_11-a").RevokedAtUtc);
        Assert.Null(keys.Single(k => k.Kid == "key.2026_11-b").RevokedAtUtc);
    });

    [MetadataSqlFact]
    public Task The_default_app_can_be_renamed_and_there_is_no_delete() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        var rename = await super.PutAsync($"{Apps}/{App.DefaultId}", new { name = "Renamed default", isEnabled = true, scopeKeys = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        var delete = await super.SendAsync(HttpMethod.Delete, $"{Apps}/{App.DefaultId}");
        Assert.Contains(delete.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        await using var db = env.HubWideDb();
        Assert.Equal("Renamed default", (await db.Apps.SingleAsync(a => a.Id == App.DefaultId)).Name);
    });

    [MetadataSqlFact]
    public Task Registry_changes_need_the_csrf_header_and_a_super_admin() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.SendAsync(HttpMethod.Post, Apps, new { key = "no-header", name = "x" }, csrf: false)).StatusCode);

        var admin = env.NewBrowser();
        await admin.SignInAppAsync(env.AppA, HubRole.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync(Apps, new { key = "sneaky", name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.NewBrowser().PostAsync(Apps, new { key = "sneaky", name = "x" })).StatusCode);

        await using var db = env.HubWideDb();
        Assert.False(await db.Apps.AnyAsync(a => a.Key == "sneaky" || a.Key == "no-header"));
    });

    [MetadataSqlFact]
    public Task Revoked_key_no_longer_validates_launches() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        var id = await CreateAppAsync(super, "launch-app", "Launch app");
        using var rsa = RSA.Create(2048);
        Assert.Equal(HttpStatusCode.Created, (await super.PostAsync($"{Apps}/{id}/keys", new { kid = "live", algorithm = "RS256", publicKeyPem = TestTokens.Pem(rsa) })).StatusCode);

        async Task<HttpResponseMessage> Launch()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var token = TestTokens.Rs256(rsa, new Dictionary<string, object?>
            {
                ["iss"] = "launch-app", ["aud"] = "https://hub.test", ["sub"] = "u1", ["role"] = "Viewer",
                ["iat"] = now, ["exp"] = now + 60, ["jti"] = Guid.NewGuid().ToString("N"), ["unrestricted"] = true,
            }, "live");
            using var client = env.Host.GetTestClient();
            return await client.PostAsync("/querybuilder/launch", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        }

        Assert.Equal(HttpStatusCode.SeeOther, (await Launch()).StatusCode);

        var revoke = await super.SendAsync(HttpMethod.Delete, $"{Apps}/{id}/keys/live");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await super.SendAsync(HttpMethod.Delete, $"{Apps}/{id}/keys/live")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await super.SendAsync(HttpMethod.Delete, $"{Apps}/{id}/keys/never-added")).StatusCode);

        var refused = await Launch();
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        // At most a cookie being cleared, never one that starts a session.
        Assert.All(refused.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => Assert.Contains("expires=Thu, 01 Jan 1970", c, StringComparison.OrdinalIgnoreCase));

        await using var db = env.HubWideDb();
        var key = await db.AppSigningKeys.SingleAsync(k => k.AppId == id);
        Assert.NotNull(key.RevokedAtUtc);
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.AppRegistryChanged && a.Summary.Contains("Revoked")));
        Assert.Contains(await db.AuditLogEntries.Where(a => a.Action == AuditAction.LaunchRefused).Select(a => a.DetailsJson).ToListAsync(), d => d!.Contains("RevokedKey"));
    });

    [MetadataSqlFact]
    public Task Disabling_an_app_ends_its_sessions_within_the_cache_window() => HubApiHost.RunAsync(async env =>
    {
        var user = env.NewBrowser();
        await user.SignInAppAsync(env.AppA, HubRole.Viewer);
        Assert.Equal(HttpStatusCode.OK, await user.StatusAsync("/querybuilder/api/me"));

        var super = await SuperAsync(env);
        var off = await super.PutAsync($"{Apps}/{env.AppA}", new { name = "App A", isEnabled = false, scopeKeys = new[] { "ProgramId" } });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        // Within the 30 s the cached answer may still say enabled; after it, the session is gone.
        env.Clock.Now = env.Clock.Now.AddSeconds(31);
        Assert.Equal(HttpStatusCode.Unauthorized, await user.StatusAsync("/querybuilder/api/me"));

        // A super admin can still open the disabled app's registry row.
        Assert.False((await HubApiHost.Browser.JsonAsync(await super.GetAsync(Apps))).EnumerateArray()
            .Single(a => a.GetProperty("id").GetGuid() == env.AppA).GetProperty("isEnabled").GetBoolean());
    });
}
