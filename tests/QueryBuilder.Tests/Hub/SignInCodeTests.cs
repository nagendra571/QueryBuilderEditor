using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Hub;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class SignInCodeTests
{
    private const string Code = "/querybuilder/signin/code";

    private static void WithCodes(QueryBuilderEditorOptions o, params string[] names)
    {
        o.Hub.SignInCodes.Enabled = true;
        o.Hub.SuperAdmins.CodeNames = [.. names];
    }

    private static Task Run(Func<HubApiHost, Task> body, params string[] names) =>
        HubApiHost.RunAsync(body, configure: o => WithCodes(o, names.Length == 0 ? ["priya"] : names));

    private static SuperAdminSignInCodes Codes(HubApiHost env) => env.Host.Services.GetRequiredService<SuperAdminSignInCodes>();

    private static async Task<string> UserAsync(HubApiHost.Browser browser)
    {
        var me = await browser.GetAsync("/querybuilder/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return (await HubApiHost.Browser.JsonAsync(me)).GetProperty("userId").GetString()!;
    }

    private static async Task<List<(AuditAction Action, string Summary, string? Details)>> AuditAsync(HubApiHost env, AuditAction action)
    {
        await using var db = env.HubWideDb();
        return (await db.AuditLogEntries.Where(a => a.Action == action).OrderBy(a => a.TimestampUtc).ToListAsync())
            .Select(a => (a.Action, a.Summary, a.DetailsJson)).ToList();
    }

    [MetadataSqlFact]
    public Task Valid_code_signs_in_as_super_admin() => Run(async env =>
    {
        var browser = env.NewBrowser();
        var issued = Codes(env).Issue("priya", null);

        var response = await browser.PostFormAsync(Code, "code", issued.Code);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/querybuilder/", response.Headers.Location!.OriginalString);
        var me = await HubApiHost.Browser.JsonAsync(await browser.GetAsync("/querybuilder/api/me"));
        Assert.True(me.GetProperty("isSuperAdmin").GetBoolean());
        Assert.Equal("super:code:priya", me.GetProperty("userId").GetString());
        var signedIn = Assert.Single(await AuditAsync(env, AuditAction.SuperAdminSignedIn));
        Assert.Contains("\"method\":\"code\"", signedIn.Details);
        Assert.DoesNotContain(issued.Code, signedIn.Details);
    });

    [MetadataSqlFact]
    public Task Code_used_twice_is_refused() => Run(async env =>
    {
        var issued = Codes(env).Issue("priya", null);
        Assert.Equal(HttpStatusCode.SeeOther, (await env.NewBrowser().PostFormAsync(Code, "code", issued.Code)).StatusCode);

        var second = env.NewBrowser();
        Assert.Equal(HttpStatusCode.Forbidden, (await second.PostFormAsync(Code, "code", issued.Code)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/querybuilder/api/me")).StatusCode);
        Assert.Contains("Replayed", Assert.Single(await AuditAsync(env, AuditAction.SuperAdminSignInRefused)).Summary);
    });

    [MetadataSqlFact]
    public Task Code_expired_is_refused() => Run(async env =>
    {
        var issued = Codes(env).Issue("priya", 5);
        env.Clock.Now += TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(31);

        Assert.Equal(CodeRefusal.Expired, Codes(env).Verify(issued.Code).Refusal);
        var browser = env.NewBrowser();
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostFormAsync(Code, "code", issued.Code)).StatusCode);
        Assert.Contains("Expired", Assert.Single(await AuditAsync(env, AuditAction.SuperAdminSignInRefused)).Summary);

        // Within the skew it still works.
        var fresh = Codes(env).Issue("priya", 5);
        env.Clock.Now += TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(29);
        Assert.Null(Codes(env).Verify(fresh.Code).Refusal);
    });

    [MetadataSqlFact]
    public Task Code_for_another_audience_is_refused() => Run(async env =>
    {
        var other = new QueryBuilderEditorOptions { ConnectionString = "x" };
        WithCodes(other, "priya");
        other.Hub.Audience = "https://another-hub.test";
        other.DataSourceManagement.EncryptionKey = HubApiHost.Key;
        var foreign = new SuperAdminSignInCodes(other, env.Clock).Issue("priya", null);

        // The audience is part of the derived key too, so a foreign code fails the signature before its aud is even read.
        Assert.Equal(CodeRefusal.BadSignature, Codes(env).Verify(foreign.Code).Refusal);
        Assert.Equal(HttpStatusCode.Forbidden, (await env.NewBrowser().PostFormAsync(Code, "code", foreign.Code)).StatusCode);
    });

    [MetadataSqlFact]
    public Task Code_with_bad_signature_is_refused() => Run(async env =>
    {
        var issued = Codes(env).Issue("priya", null);
        var dot = issued.Code.IndexOf('.');
        var forgedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{{\"purpose\":\"qb-super-signin\",\"aud\":\"https://hub.test\",\"name\":\"priya\",\"jti\":\"x\",\"iat\":{env.Clock.Now.ToUnixTimeSeconds()},\"exp\":{env.Clock.Now.ToUnixTimeSeconds() + 600}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var forged = forgedPayload + issued.Code[dot..];
        var truncated = issued.Code[..^4];

        Assert.Equal(CodeRefusal.BadSignature, Codes(env).Verify(forged).Refusal);
        Assert.Equal(CodeRefusal.BadSignature, Codes(env).Verify(truncated).Refusal);
        Assert.Equal(CodeRefusal.Malformed, Codes(env).Verify("not a code").Refusal);
        var browser = env.NewBrowser();
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostFormAsync(Code, "code", forged)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/querybuilder/api/me")).StatusCode);
    });

    [MetadataSqlFact]
    public Task Code_for_unlisted_name_cannot_be_issued_or_used() => Run(async env =>
    {
        var unlisted = Assert.Throws<InvalidOperationException>(() => Codes(env).Issue("mallory", null));
        Assert.DoesNotContain("Parameter", unlisted.Message);
        Assert.Throws<ArgumentException>(() => Codes(env).Issue("priya", 0));
        Assert.Throws<ArgumentException>(() => Codes(env).Issue("priya", 61));
        Assert.Throws<InvalidOperationException>(() => Codes(env).Issue("Priya", null));

        var issued = Codes(env).Issue("priya", null);
        await env.RestartAsync(o => WithCodes(o, "someone-else"));

        Assert.Equal(CodeRefusal.NotListed, Codes(env).Verify(issued.Code).Refusal);
        Assert.Equal(HttpStatusCode.Forbidden, (await env.NewBrowser().PostFormAsync(Code, "code", issued.Code)).StatusCode);
    }, "priya");

    [MetadataSqlFact]
    public Task Code_post_without_matching_origin_is_refused() => Run(async env =>
    {
        var issued = Codes(env).Issue("priya", null);
        var browser = env.NewBrowser();

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostFormAsync(Code, "code", issued.Code, origin: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostFormAsync(Code, "code", issued.Code, origin: "http://evil.test")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/querybuilder/api/me")).StatusCode);

        // A refused origin does not burn the code.
        Assert.Equal(HttpStatusCode.SeeOther, (await browser.PostFormAsync(Code, "code", issued.Code)).StatusCode);
    });

    [MetadataSqlFact]
    public Task Refusals_are_audited_with_reason_and_never_the_code() => Run(async env =>
    {
        var issued = Codes(env).Issue("priya", null);
        var bad = issued.Code[..^3] + "AAA";
        await env.NewBrowser().PostFormAsync(Code, "code", bad);
        await env.NewBrowser().PostFormAsync(Code, "code", issued.Code, origin: "http://evil.test");

        var refusals = await AuditAsync(env, AuditAction.SuperAdminSignInRefused);
        Assert.Equal(2, refusals.Count);
        Assert.Contains("BadSignature", refusals[0].Summary + refusals[1].Summary);
        Assert.Contains("OriginMismatch", refusals[0].Summary + refusals[1].Summary);
        foreach (var r in refusals)
        {
            Assert.DoesNotContain(issued.Code, r.Summary + r.Details);
            Assert.DoesNotContain(bad, r.Summary + r.Details);
            Assert.DoesNotContain(issued.Code[..^3], r.Summary + r.Details);
        }
    });

    [MetadataSqlFact]
    public Task Signin_page_shows_only_enabled_options() => HubApiHost.RunAsync(async env =>
    {
        var codesOnly = await env.NewBrowser().GetAsync("/querybuilder/signin");
        var html = await codesOnly.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, codesOnly.StatusCode);
        Assert.Contains("no-store", codesOnly.Headers.CacheControl!.ToString());
        Assert.Contains("name=\"code\"", html);
        Assert.DoesNotContain("/signin/provider", html);

        await env.RestartAsync(o =>
        {
            WithCodes(o, "priya");
            o.Hub.SuperAdminSignInScheme = "host";
            o.Hub.ProviderDisplayName = "Acme <Corp>";
        });
        var both = await (await env.NewBrowser().GetAsync("/querybuilder/signin")).Content.ReadAsStringAsync();
        Assert.Contains("name=\"code\"", both);
        Assert.Contains("/querybuilder/signin/provider", both);
        Assert.Contains("Sign in with Acme &lt;Corp&gt;", both);
        Assert.DoesNotContain("Acme <Corp>", both);
    }, configure: o =>
    {
        WithCodes(o, "priya");
        o.Hub.SuperAdminSignInScheme = null;
    });

    [MetadataSqlFact]
    public Task Code_session_re_checked_against_CodeNames() => Run(async env =>
    {
        var browser = env.NewBrowser();
        Assert.Equal(HttpStatusCode.SeeOther, (await browser.PostFormAsync(Code, "code", Codes(env).Issue("priya", null).Code)).StatusCode);
        Assert.Equal("super:code:priya", await UserAsync(browser));

        await env.RestartAsync(o => WithCodes(o, "someone-else"));
        var moved = browser.MovedTo(env.Host.GetTestClient());

        Assert.Equal(HttpStatusCode.Unauthorized, (await moved.GetAsync("/querybuilder/api/me")).StatusCode);
    });

    [MetadataSqlFact]
    public Task Codes_disabled_post_is_404() => HubApiHost.RunAsync(async env =>
    {
        var codes = Codes(env);
        var response = await env.NewBrowser().PostFormAsync(Code, "code", "anything");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // With only the provider the sign-in GET still challenges, as it always did.
        Assert.NotEqual(HttpStatusCode.OK, (await env.NewBrowser().GetAsync("/querybuilder/signin")).StatusCode);
        Assert.NotNull(codes);
    }, configure: o => o.Hub.SuperAdmins.CodeNames = ["priya"]);

    [MetadataSqlFact]
    public Task Issuer_writes_audit_before_returning() => Run(async env =>
    {
        var issuer = env.Host.Services.GetRequiredService<ISuperAdminCodeIssuer>();
        var issued = await issuer.IssueAsync("priya", 10, CancellationToken.None);

        var entry = Assert.Single(await AuditAsync(env, AuditAction.SuperAdminCodeIssued));
        Assert.Contains(issued.Jti, entry.Details);
        Assert.Contains("priya", entry.Details);
        Assert.DoesNotContain(issued.Code, entry.Details + entry.Summary);
        Assert.Null(Codes(env).Verify(issued.Code).Refusal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync("mallory", null, CancellationToken.None));
        Assert.Single(await AuditAsync(env, AuditAction.SuperAdminCodeIssued));
    });

    [MetadataSqlFact]
    public Task Launch_token_still_cannot_make_a_super_admin() => Run(async env =>
    {
        var token = TestTokens.Rs256(env.RsaA, new Dictionary<string, object?>
        {
            ["iss"] = "app-a", ["aud"] = "https://hub.test", ["sub"] = "priya", ["role"] = "Admin", ["super"] = true, ["isSuperAdmin"] = true,
            ["iat"] = env.Clock.Now.ToUnixTimeSeconds(), ["exp"] = env.Clock.Now.ToUnixTimeSeconds() + 60, ["jti"] = Guid.NewGuid().ToString("N"),
            ["scope"] = new Dictionary<string, string[]> { ["ProgramId"] = ["10"] },
        }, "rsa-a");
        var browser = env.NewBrowser();
        var response = await browser.SendAsync(HttpMethod.Post, "/querybuilder/launch", csrf: false,
            content: new FormUrlEncodedContent([new("token", token)]));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var me = await HubApiHost.Browser.JsonAsync(await browser.GetAsync("/querybuilder/api/me"));
        Assert.False(me.GetProperty("isSuperAdmin").GetBoolean());
        Assert.StartsWith("priya", me.GetProperty("userId").GetString());
    });

    [Fact]
    public void Default_minutes_outside_1_to_60_stops_startup()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() => services.AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "x";
            o.Hub.Enabled = true;
            o.Hub.Audience = "https://hub.test";
            o.DataSourceManagement.EncryptionKey = HubApiHost.Key;
            WithCodes(o, "priya");
            o.Hub.SignInCodes.DefaultMinutes = 61;
        }));
        Assert.Contains("DefaultMinutes", ex.Message);
    }

    [MetadataSqlFact]
    public Task Refused_cross_site_post_leaves_an_existing_session_working() => Run(async env =>
    {
        var browser = env.NewBrowser();
        Assert.Equal(HttpStatusCode.SeeOther, (await browser.PostFormAsync(Code, "code", Codes(env).Issue("priya", null).Code)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostFormAsync(Code, "code", "x", origin: "http://evil.test")).StatusCode);

        Assert.Equal("super:code:priya", await UserAsync(browser));
    });

    [MetadataSqlFact]
    public Task Code_with_out_of_range_times_is_malformed() => Run(async env =>
    {
        var ikm = Convert.FromBase64String(HubApiHost.Key);
        var key = System.Security.Cryptography.HKDF.DeriveKey(System.Security.Cryptography.HashAlgorithmName.SHA256, ikm, 32, [], Encoding.UTF8.GetBytes("qb-super-signin-code-v1|https://hub.test"));
        string Mint(long iat, long exp)
        {
            var payload = Encoding.UTF8.GetBytes($"{{\"purpose\":\"qb-super-signin\",\"aud\":\"https://hub.test\",\"name\":\"priya\",\"jti\":\"j\",\"iat\":{iat},\"exp\":{exp}}}");
            string B(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var body = B(payload);
            return body + "." + B(System.Security.Cryptography.HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(body)));
        }

        var now = env.Clock.Now.ToUnixTimeSeconds();
        Assert.Equal(CodeRefusal.Malformed, Codes(env).Verify(Mint(now, long.MaxValue)).Refusal);
        Assert.Equal(CodeRefusal.Malformed, Codes(env).Verify(Mint(-5, now + 60)).Refusal);
        Assert.Equal(CodeRefusal.Malformed, Codes(env).Verify(Mint(long.MinValue, long.MaxValue)).Refusal);
        Assert.Null(Codes(env).Verify(Mint(now, now + 60)).Refusal);
    });

    private static async Task AssertGenericRefusalAsync(HttpResponseMessage response, string heading, string sentence)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(heading, body);
        Assert.Contains(sentence, body);
        Assert.Contains("href=\"/querybuilder/signin\"", body);
        foreach (var word in new[] { "signature", "expired", "audience", "replay", "Replayed", "BadSignature", "OriginMismatch" })
        {
            Assert.DoesNotContain(word, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [MetadataSqlFact]
    public Task Refused_code_and_provider_posts_show_the_new_generic_pages() => HubApiHost.RunAsync(async env =>
    {
        var browser = env.NewBrowser();
        var issued = Codes(env).Issue("priya", null);
        var bad = await browser.PostFormAsync(Code, "code", "garbage");
        await AssertGenericRefusalAsync(bad, "Sign-in code not accepted", "That code didn't work. Codes work once and expire after a few minutes. Ask for a new code and try again.");
        var origin = await browser.PostFormAsync(Code, "code", issued.Code, origin: "http://evil.test");
        await AssertGenericRefusalAsync(origin, "Sign-in code not accepted", "That code didn't work.");
        var provider = await browser.PostFormAsync("/querybuilder/signin/provider", "x", "y", origin: "http://evil.test");
        await AssertGenericRefusalAsync(provider, "Sign-in didn't start from this site", "Open the sign-in page and try again.");
    }, configure: o =>
    {
        WithCodes(o, "priya");
        o.Hub.SuperAdminSignInScheme = "host";
    });

    [MetadataSqlFact]
    public Task Provider_post_needs_a_matching_origin() => HubApiHost.RunAsync(async env =>
    {
        const string path = "/querybuilder/signin/provider";
        var browser = env.NewBrowser();
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostFormAsync(path, "x", "y", origin: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostFormAsync(path, "x", "y", origin: "http://evil.test")).StatusCode);
        // A matching origin gets as far as the challenge (the test host has no real "host" scheme, so it errors there, never 403).
        var ok = await Record.ExceptionAsync(async () => Assert.NotEqual(HttpStatusCode.Forbidden, (await browser.PostFormAsync(path, "x", "y")).StatusCode));
        Assert.True(ok is null or InvalidOperationException);
    }, configure: o =>
    {
        WithCodes(o, "priya");
        o.Hub.SuperAdminSignInScheme = "host";
    });

    [MetadataSqlFact]
    public Task Issuer_refuses_when_codes_are_turned_off_and_writes_no_audit() => HubApiHost.RunAsync(async env =>
    {
        var issuer = env.Host.Services.GetRequiredService<ISuperAdminCodeIssuer>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync("priya", null, CancellationToken.None));

        Assert.Contains("HubOptions.SignInCodes.Enabled", ex.Message);
        Assert.Contains("Hub:SignIn:Codes:Enabled", ex.Message);
        Assert.Empty(await AuditAsync(env, AuditAction.SuperAdminCodeIssued));
    }, configure: o => o.Hub.SuperAdmins.CodeNames = ["priya"]);

    [MetadataSqlFact]
    public Task Issuer_audits_the_resolved_default_minutes() => Run(async env =>
    {
        await env.NewBrowser().GetAsync("/");
        var issuer = env.Host.Services.GetRequiredService<ISuperAdminCodeIssuer>();
        env.Clock.Now += TimeSpan.FromMinutes(3);
        await issuer.IssueAsync("priya", null, CancellationToken.None);

        var entry = Assert.Single(await AuditAsync(env, AuditAction.SuperAdminCodeIssued));
        Assert.Contains("\"minutes\":15", entry.Details);
    });
}
