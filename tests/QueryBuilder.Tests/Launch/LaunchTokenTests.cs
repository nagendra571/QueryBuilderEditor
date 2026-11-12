using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Hub;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure;
using QueryBuilder.Infrastructure.Hub;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Launch;
using QueryBuilder.Tests.Hub;
using Xunit;

namespace QueryBuilder.Tests.Launch;

[Collection(MetadataDatabaseCollection.Name)]
public class LaunchTokenTests
{
    private const string Audience = "https://hub.example";

    private static LaunchScope Scope(params string[] values) =>
        LaunchScope.For(new Dictionary<string, IEnumerable<string>> { ["ProgramId"] = values });

    private static string Payload(string token)
    {
        var s = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    private static async Task WithHubAsync(RSA? rsa, ECDsa? ec, Func<ILaunchTokenValidator, Guid, Task> body)
    {
        await MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
        {
            Guid appId;
            await using (var db = new AppDbContext(options, new FixedAppContext(App.NoneId, hubWide: true)))
            {
                var app = new App { Key = "app-a", Name = "A", ScopeKeys = ["ProgramId"] };
                appId = app.Id;
                db.Apps.Add(app);
                if (rsa is not null)
                {
                    db.AppSigningKeys.Add(new AppSigningKey { AppId = app.Id, Kid = "rsa-1", Algorithm = "RS256", PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem() });
                }

                if (ec is not null)
                {
                    db.AppSigningKeys.Add(new AppSigningKey { AppId = app.Id, Kid = "ec-1", Algorithm = "ES256", PublicKeyPem = ec.ExportSubjectPublicKeyInfoPem() });
                }

                await db.SaveChangesAsync();
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(connectionString, applyMigrations: false, hubMode: true);
            services.AddSingleton(new LaunchValidationSettings(Audience));
            await using var provider = services.BuildServiceProvider();
            await body(provider.GetRequiredService<ILaunchTokenValidator>(), appId);
        });
    }

    [MetadataSqlFact]
    public async Task Rs256_token_is_accepted_by_the_hub_validator()
    {
        using var rsa = RSA.Create(2048);
        await WithHubAsync(rsa, null, async (validator, appId) =>
        {
            var key = LaunchSigningKey.Rsa("rsa-1", rsa);
            var token = LaunchToken.Create("app-a", Audience, key, new LaunchUser("user-1", "Mary \"Q\"\n\U0001F600 O'Neil", "mary+qb@x.com"),
                LaunchRole.Author, Scope("10", "20"));
            var result = await validator.ValidateAsync(token, CancellationToken.None);
            Assert.Null(result.Refusal);
            var claims = result.Claims!;
            Assert.Equal(appId, claims.AppId);
            Assert.Equal("user-1", claims.UserId);
            Assert.Equal("Mary \"Q\"\n\U0001F600 O'Neil", claims.Name);
            Assert.Equal("mary+qb@x.com", claims.Email);
            Assert.Equal(HubRole.Author, claims.Role);
            Assert.Equal(["10", "20"], claims.Scope.ValuesFor("ProgramId"));
        });
    }

    [MetadataSqlFact]
    public async Task Es256_token_is_accepted_by_the_hub_validator()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await WithHubAsync(null, ec, async (validator, _) =>
        {
            var key = LaunchSigningKey.EcdsaP256("ec-1", ec);
            Assert.Equal("ES256", key.Algorithm);
            var token = LaunchToken.Create("app-a", Audience, key, new LaunchUser("user-1"), LaunchRole.Viewer, LaunchScope.Unrestricted);
            var result = await validator.ValidateAsync(token, CancellationToken.None);
            Assert.Null(result.Refusal);
            Assert.True(result.Claims!.Scope.IsUnrestricted);
            Assert.Equal(HubRole.Viewer, result.Claims.Role);
        });
    }

    [Fact]
    public void Iat_and_exp_are_whole_seconds_and_default_lifetime_is_60_seconds()
    {
        using var rsa = RSA.Create(2048);
        var key = LaunchSigningKey.Rsa("rsa-1", rsa);
        var now = new DateTimeOffset(2026, 11, 12, 10, 0, 0, 750, TimeSpan.Zero);
        var payload = Payload(LaunchToken.Create("app-a", Audience, key, new LaunchUser("u"), LaunchRole.Admin, LaunchScope.Unrestricted, now: now));
        var iat = now.ToUnixTimeSeconds();
        Assert.Contains($"\"iat\":{iat},\"exp\":{iat + 60}", payload);
        Assert.Contains("\"unrestricted\":true", payload);
        Assert.DoesNotContain("\"scope\"", payload);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    [InlineData(-5)]
    public void Lifetime_outside_1_to_120_seconds_is_refused(int seconds)
    {
        using var rsa = RSA.Create(2048);
        var key = LaunchSigningKey.Rsa("rsa-1", rsa);
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchToken.Create("a", Audience, key, new LaunchUser("u"),
            LaunchRole.Admin, LaunchScope.Unrestricted, lifetime: TimeSpan.FromSeconds(seconds)));
        LaunchToken.Create("a", Audience, key, new LaunchUser("u"), LaunchRole.Admin, LaunchScope.Unrestricted, lifetime: TimeSpan.FromSeconds(120));
    }

    [MetadataSqlFact]
    public async Task Scope_of_6144_bytes_is_accepted_by_the_hub_and_6145_refused_by_Launch()
    {
        using var rsa = RSA.Create(2048);
        await WithHubAsync(rsa, null, async (validator, _) =>
        {
            var key = LaunchSigningKey.Rsa("rsa-1", rsa);
            var atLimit = ScopeOfSize(ResolvedDataScope.MaxSessionBytes);
            Assert.Equal(ResolvedDataScope.MaxSessionBytes, atLimit.SerializedByteCount);
            var token = LaunchToken.Create("app-a", Audience, key, new LaunchUser("u"), LaunchRole.Admin, atLimit);
            var result = await validator.ValidateAsync(token, CancellationToken.None);
            Assert.Null(result.Refusal);
            Assert.Equal(ResolvedDataScope.MaxSessionBytes,
                Encoding.UTF8.GetByteCount(result.Claims!.Scope.SerializeForSession()));

            var over = Assert.Throws<ArgumentException>(() => LaunchToken.Create("app-a", Audience, key, new LaunchUser("u"),
                LaunchRole.Admin, ScopeOfSize(ResolvedDataScope.MaxSessionBytes + 1)));
            Assert.Contains("larger than the hub accepts (6 KB)", over.Message);
        });
    }

    // Full 200-character values, then one value sized so the serialised scope is exactly `bytes`.
    private static LaunchScope ScopeOfSize(int bytes)
    {
        var values = new List<string>();
        var fixedBytes = Encoding.UTF8.GetByteCount("{\"ProgramId\":[]}");
        var total = fixedBytes;
        while (bytes - total > 203 + 10)
        {
            values.Add(values.Count.ToString("D4") + new string('a', 196));
            total += values.Count == 1 ? 202 : 203;
        }

        var last = bytes - total - 2 - (values.Count > 0 ? 1 : 0);
        values.Add(new string('b', last));
        return Scope(values.ToArray());
    }

    [Fact]
    public void Byte_count_matches_what_the_hub_measures_for_every_character()
    {
        var sb = new StringBuilder();
        for (var c = 1; c <= 0xFFFF; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF) continue;
            sb.Append((char)c);
            if (sb.Length == 150)
            {
                AssertSameCount(sb.ToString());
                sb.Clear();
            }
        }

        AssertSameCount(sb.ToString());
        AssertSameCount("emoji \U0001F600 and quote \" slash \\ tab \t");
    }

    private static void AssertSameCount(string value)
    {
        var ours = Scope(value).SerializedByteCount;
        var hub = new ResolvedDataScope(false, new Dictionary<string, IReadOnlyList<string>> { ["ProgramId"] = [value] });
        Assert.Equal(Encoding.UTF8.GetByteCount(hub.SerializeForSession()), ours);
    }

    [Fact]
    public void Scope_rules_match_the_hub()
    {
        Assert.Throws<ArgumentException>(() => LaunchScope.For(new Dictionary<string, IEnumerable<string>> { ["bad key"] = new[] { "1" } }));
        Assert.Throws<ArgumentException>(() => LaunchScope.For(new Dictionary<string, IEnumerable<string>> { ["K"] = Array.Empty<string>() }));
        Assert.Throws<ArgumentException>(() => Scope(""));
        Assert.Throws<ArgumentException>(() => Scope(new string('a', 201)));
        Assert.Throws<ArgumentException>(() => LaunchScope.For(new Dictionary<string, IEnumerable<string>>()));
        Assert.Throws<ArgumentException>(() => Scope(Enumerable.Range(0, 1001).Select(i => i.ToString()).ToArray()));
        Assert.Throws<ArgumentException>(() => LaunchScope.For(Enumerable.Range(0, 21)
            .ToDictionary(i => "K" + i, _ => (IEnumerable<string>)new[] { "1" })));
        Assert.Throws<ArgumentException>(() => LaunchScope.For(new Dictionary<string, IEnumerable<string>>
            { ["K"] = new[] { "1" }, ["k"] = new[] { "2" } }));
    }

    [Fact]
    public void Weak_or_wrong_keys_are_refused()
    {
        using var weak = RSA.Create(1024);
        Assert.Throws<ArgumentException>(() => LaunchSigningKey.Rsa("k", weak));
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<ArgumentException>(() => LaunchSigningKey.EcdsaP256("k", p384));
        using var rsa = RSA.Create(2048);
        _ = LaunchSigningKey.Rsa("k", rsa);
        rsa.ExportParameters(false);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-a")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("k1\n")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Bad_kid_is_refused(string kid)
    {
        using var rsa = RSA.Create(2048);
        Assert.Throws<ArgumentException>(() => LaunchSigningKey.Rsa(kid, rsa));
    }

    [Fact]
    public void Scope_key_patterns_match_the_hub()
    {
        LaunchScope.For(new Dictionary<string, IEnumerable<string>> { ["_Region"] = new[] { "1" } });
        Assert.Throws<ArgumentException>(() => LaunchScope.For(new Dictionary<string, IEnumerable<string>> { ["Region\n"] = new[] { "1" } }));
    }

    [Fact]
    public void User_limits_match_the_hub()
    {
        Assert.Throws<ArgumentException>(() => new LaunchUser(""));
        Assert.Throws<ArgumentException>(() => new LaunchUser(new string('a', 257)));
        Assert.Throws<ArgumentException>(() => new LaunchUser("super:x"));
        Assert.Throws<ArgumentException>(() => new LaunchUser("u", new string('a', 201)));
        Assert.Throws<ArgumentException>(() => new LaunchUser("u", null, new string('a', 321)));
        _ = new LaunchUser(new string('a', 256), new string('a', 200), new string('a', 320));
    }

    [Fact]
    public void Form_html_encodes_the_token_and_url()
    {
        var html = LaunchForm.Html("https://hub.example.com/", "a.b\"><script>x</script>.c&d");
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("action=\"https://hub.example.com/querybuilder/launch\"", html);
        Assert.Contains("name=\"token\"", html);
        Assert.DoesNotContain("<script>x", html);
        Assert.Contains("&quot;&gt;&lt;script&gt;x&lt;/script&gt;.c&amp;d", html);
        Assert.Contains("<button type=\"submit\">Continue to QueryBuilder</button>", html);
        Assert.DoesNotContain("<noscript>", html);
        Assert.DoesNotContain("nonce", html);
        Assert.Contains(".submit()", html);
        Assert.Contains("http://localhost:5000/querybuilder/launch", LaunchForm.Html("http://localhost:5000", "t"));
    }

    [Fact]
    public void Form_has_a_visible_button_and_an_encoded_nonce()
    {
        var html = LaunchForm.Html("https://hub.example.com", "t", "ab\"c<d&e");
        Assert.Contains("<script nonce=\"ab&quot;c&lt;d&amp;e\">", html);
        Assert.Contains("<button type=\"submit\">Continue to QueryBuilder</button>", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/relative")]
    [InlineData("http://a.example/home")]
    [InlineData("https://a.example/home#frag")]
    [InlineData("https://u:p@a.example/")]
    [InlineData("https://A.example/home")]
    [InlineData("https://a.example")]
    [InlineData(" https://a.example/ ")]
    public void Bad_return_url_is_refused(string url)
    {
        using var rsa = RSA.Create(2048);
        var key = LaunchSigningKey.Rsa("k", rsa);
        Assert.Throws<ArgumentException>(() => LaunchToken.Create("a", Audience, key, new LaunchUser("u"), LaunchRole.Admin, LaunchScope.Unrestricted, returnUrl: url));
        Assert.Throws<ArgumentException>(() => LaunchToken.Create("a", Audience, key, new LaunchUser("u"), LaunchRole.Admin, LaunchScope.Unrestricted,
            returnUrl: "https://a.example/" + new string('x', 800)));
        Assert.Contains("\"returnUrl\":\"https://a.example/home\"", Payload(LaunchToken.Create("a", Audience, key, new LaunchUser("u"),
            LaunchRole.Admin, LaunchScope.Unrestricted, returnUrl: "https://a.example/home")));
        LaunchToken.Create("a", Audience, key, new LaunchUser("u"), LaunchRole.Admin, LaunchScope.Unrestricted, returnUrl: "http://localhost:5000/");
    }

    [Theory]
    [InlineData("http://hub.example.com")]
    [InlineData("hub.example.com")]
    [InlineData("https://hub.example.com/sub")]
    [InlineData("https://hub.example.com/?x=1")]
    [InlineData("https://u:p@hub.example.com")]
    [InlineData("ftp://hub.example.com")]
    [InlineData("")]
    public void Form_refuses_a_bad_hub_url(string hub) =>
        Assert.Throws<ArgumentException>(() => LaunchForm.Html(hub, "t"));

    [Fact]
    public void Role_and_scope_are_checked_and_unrestricted_has_no_scope_claim()
    {
        using var rsa = RSA.Create(2048);
        var key = LaunchSigningKey.Rsa("k", rsa);
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchToken.Create("a", Audience, key, new LaunchUser("u"), (LaunchRole)9, LaunchScope.Unrestricted));
        Assert.Throws<ArgumentNullException>(() => LaunchToken.Create("a", Audience, key, new LaunchUser("u"), LaunchRole.Admin, null!));
        var payload = Payload(LaunchToken.Create("a", Audience, key, new LaunchUser("u"), LaunchRole.Admin, Scope("1")));
        Assert.Contains("\"scope\":{\"ProgramId\":[\"1\"]}", payload);
        Assert.DoesNotContain("unrestricted", payload);
    }

    [Fact]
    public void Json_strings_are_escaped()
    {
        using var rsa = RSA.Create(2048);
        var key = LaunchSigningKey.Rsa("k", rsa);
        var payload = Payload(LaunchToken.Create("a", Audience, key, new LaunchUser("u", "Q\"\\\n\u0001é"), LaunchRole.Admin, LaunchScope.Unrestricted));
        Assert.Contains("\"name\":\"Q\\\"\\\\\\n\\u0001é\"", payload);
    }
}
