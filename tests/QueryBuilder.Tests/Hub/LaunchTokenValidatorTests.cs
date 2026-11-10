using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Hub;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure;
using QueryBuilder.Infrastructure.Hub;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

public class LaunchTokenValidatorTests
{
    private const string Audience = "https://hub.example";
    private const string ReturnUrl = "https://a.example/home";

    private sealed class Env : IDisposable
    {
        public Guid AppA, AppB, AppOff;
        public RSA RsaA = RSA.Create(2048), RsaB = RSA.Create(2048), RsaRevoked = RSA.Create(2048), RsaOff = RSA.Create(2048);
        public ECDsa EcA = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public ServiceProvider Services = null!;
        public DbContextOptions<AppDbContext> Options = null!;

        public ILaunchTokenValidator Validator => Services.GetRequiredService<ILaunchTokenValidator>();
        public Task<LaunchResult> Validate(string token) => Validator.ValidateAsync(token, CancellationToken.None);

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public Dictionary<string, object?> Claims(Action<Dictionary<string, object?>>? change = null)
        {
            var claims = new Dictionary<string, object?>
            {
                ["iss"] = "app-a", ["aud"] = Audience, ["sub"] = "user-1", ["role"] = "Admin",
                ["iat"] = Now, ["exp"] = Now + 60, ["jti"] = Guid.NewGuid().ToString("N"),
                ["scope"] = new Dictionary<string, string[]> { ["ProgramId"] = ["10", "20"] },
            };
            change?.Invoke(claims);
            return claims;
        }

        public string Token(Action<Dictionary<string, object?>>? change = null) => TestTokens.Rs256(RsaA, Claims(change));

        public void Dispose()
        {
            Services.Dispose();
            RsaA.Dispose(); RsaB.Dispose(); RsaRevoked.Dispose(); RsaOff.Dispose(); EcA.Dispose();
        }
    }

    private static async Task RunAsync(Func<Env, Task> body, string audience = Audience)
    {
        await MetadataDb.WithDatabaseAsync(async (options, connectionString) =>
        {
            using var env = new Env { Options = options };
            await using (var db = new AppDbContext(options, new FixedAppContext(App.NoneId, hubWide: true)))
            {
                var a = new App { Key = "app-a", Name = "A", ScopeKeys = ["ProgramId"] };
                var b = new App { Key = "app-b", Name = "B" };
                var off = new App { Key = "app-off", Name = "Off", IsEnabled = false };
                env.AppA = a.Id; env.AppB = b.Id; env.AppOff = off.Id;
                db.Apps.AddRange(a, b, off);
                db.AppSigningKeys.AddRange(
                    new AppSigningKey { AppId = a.Id, Kid = "rsa-a", Algorithm = "RS256", PublicKeyPem = TestTokens.Pem(env.RsaA) },
                    new AppSigningKey { AppId = a.Id, Kid = "ec-a", Algorithm = "ES256", PublicKeyPem = TestTokens.Pem(env.EcA) },
                    new AppSigningKey { AppId = a.Id, Kid = "revoked-a", Algorithm = "RS256", PublicKeyPem = TestTokens.Pem(env.RsaRevoked), RevokedAtUtc = DateTimeOffset.UtcNow },
                    new AppSigningKey { AppId = b.Id, Kid = "rsa-b", Algorithm = "RS256", PublicKeyPem = TestTokens.Pem(env.RsaB) },
                    new AppSigningKey { AppId = off.Id, Kid = "rsa-off", Algorithm = "RS256", PublicKeyPem = TestTokens.Pem(env.RsaOff) });
                db.AppReturnUrls.Add(new AppReturnUrl { AppId = a.Id, Url = ReturnUrl });
                db.DataSources.Add(new DataSource { AppId = a.Id, Name = "a-source", ScopeKeys = ["Region"] });
                db.DataSources.Add(new DataSource { AppId = b.Id, Name = "b-source", ScopeKeys = ["OnlyB"] });
                await db.SaveChangesAsync();
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(connectionString, applyMigrations: false, hubMode: true);
            services.AddSingleton(new LaunchValidationSettings(audience));
            env.Services = services.BuildServiceProvider();
            await body(env);
        });
    }

    private static async Task ExpectAsync(Env env, string token, LaunchRefusalReason reason, Guid? appId = null)
    {
        var result = await env.Validate(token);
        Assert.Null(result.Claims);
        Assert.Equal(reason, result.Refusal);
        if (appId is not null)
        {
            Assert.Equal(appId, result.AppId);
        }
    }

    [MetadataSqlFact]
    public Task Valid_rs256_and_es256_tokens_are_accepted() => RunAsync(async env =>
    {
        var rs = await env.Validate(env.Token(c =>
        {
            c["name"] = "Mary"; c["email"] = "m@x.com"; c["returnUrl"] = ReturnUrl;
            c["scope"] = new Dictionary<string, string[]> { ["programid"] = ["10", "20", "10"], ["Region"] = ["east"] };
        }));
        Assert.Null(rs.Refusal);
        var claims = rs.Claims!;
        Assert.Equal(env.AppA, claims.AppId);
        Assert.Equal("app-a", claims.AppKey);
        Assert.Equal("user-1", claims.UserId);
        Assert.Equal("Mary", claims.Name);
        Assert.Equal("m@x.com", claims.Email);
        Assert.Equal(HubRole.Admin, claims.Role);
        Assert.Equal(ReturnUrl, claims.ReturnUrl);
        Assert.False(claims.Scope.IsUnrestricted);
        Assert.Equal(["10", "20"], claims.Scope.ValuesFor("ProgramId"));
        Assert.Equal(["east"], claims.Scope.ValuesFor("Region"));

        var es = await env.Validate(TestTokens.Es256(env.EcA, env.Claims(c => { c["role"] = "Viewer"; c.Remove("scope"); c["unrestricted"] = true; })));
        Assert.Null(es.Refusal);
        Assert.Equal(HubRole.Viewer, es.Claims!.Role);
        Assert.True(es.Claims.Scope.IsUnrestricted);
        Assert.Null(es.Claims.ReturnUrl);
    });

    [MetadataSqlFact]
    public Task Malformed_tokens_are_refused() => RunAsync(async env =>
    {
        await ExpectAsync(env, "not-a-token", LaunchRefusalReason.Malformed);
        await ExpectAsync(env, "", LaunchRefusalReason.Malformed);
        await ExpectAsync(env, "a.b.c", LaunchRefusalReason.Malformed);
        await ExpectAsync(env, new string('a', LaunchTokenValidatorLimits.MaxTokenLength + 1), LaunchRefusalReason.Malformed);
    });

    [MetadataSqlFact]
    public Task Unknown_app() => RunAsync(async env =>
        await ExpectAsync(env, env.Token(c => c["iss"] = "nobody"), LaunchRefusalReason.UnknownApp, appId: null));

    [MetadataSqlFact]
    public Task Disabled_app() => RunAsync(async env =>
        await ExpectAsync(env, TestTokens.Rs256(env.RsaOff, env.Claims(c => c["iss"] = "app-off"), "rsa-off"), LaunchRefusalReason.AppDisabled, env.AppOff));

    [MetadataSqlFact]
    public Task Unknown_key() => RunAsync(async env =>
    {
        await ExpectAsync(env, TestTokens.Rs256(env.RsaA, env.Claims(), "nope"), LaunchRefusalReason.UnknownKey, env.AppA);
        await ExpectAsync(env, TestTokens.Rs256(env.RsaA, env.Claims(), kid: null), LaunchRefusalReason.UnknownKey, env.AppA);
    });

    [MetadataSqlFact]
    public Task A_key_registered_to_another_app_is_unknown_key() => RunAsync(async env =>
        await ExpectAsync(env, TestTokens.Rs256(env.RsaB, env.Claims(), "rsa-b"), LaunchRefusalReason.UnknownKey, env.AppA));

    [MetadataSqlFact]
    public Task Swapped_iss_is_bad_signature_or_unknown_key() => RunAsync(async env =>
    {
        // A's key and kid, but claiming to be app B: B has no such kid.
        var result = await env.Validate(TestTokens.Rs256(env.RsaA, env.Claims(c => c["iss"] = "app-b"), "rsa-a"));
        Assert.Null(result.Claims);
        Assert.Contains(result.Refusal, new LaunchRefusalReason?[] { LaunchRefusalReason.UnknownKey, LaunchRefusalReason.BadSignature });
        Assert.Equal(env.AppB, result.AppId);

        // B's kid but signed by A's key.
        await ExpectAsync(env, TestTokens.Rs256(env.RsaA, env.Claims(c => c["iss"] = "app-b"), "rsa-b"), LaunchRefusalReason.BadSignature, env.AppB);
    });

    [MetadataSqlFact]
    public Task Revoked_key() => RunAsync(async env =>
        await ExpectAsync(env, TestTokens.Rs256(env.RsaRevoked, env.Claims(), "revoked-a"), LaunchRefusalReason.RevokedKey, env.AppA));

    [MetadataSqlFact]
    public Task Bad_signature() => RunAsync(async env =>
    {
        await ExpectAsync(env, TestTokens.Rs256(env.RsaB, env.Claims(), "rsa-a"), LaunchRefusalReason.BadSignature, env.AppA);

        var good = env.Token();
        var parts = good.Split('.');
        var tampered = $"{parts[0]}.{TestTokensEncode(env.Claims(c => c["role"] = "Admin"))}.{parts[2]}";
        await ExpectAsync(env, tampered, LaunchRefusalReason.BadSignature, env.AppA);
    });

    private static string TestTokensEncode(Dictionary<string, object?> payload) =>
        Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload));

    [MetadataSqlFact]
    public Task Algorithm_not_allowed() => RunAsync(async env =>
    {
        // HS256 using the app's public key as the secret: the classic confusion attack.
        var secret = System.Text.Encoding.UTF8.GetBytes(TestTokens.Pem(env.RsaA));
        await ExpectAsync(env, TestTokens.Hs256(secret, env.Claims()), LaunchRefusalReason.AlgorithmNotAllowed);
        await ExpectAsync(env, TestTokens.Unsigned(env.Claims()), LaunchRefusalReason.AlgorithmNotAllowed);
        await ExpectAsync(env, TestTokens.Rs256WithHeaderAlg(env.RsaA, "PS256", env.Claims()), LaunchRefusalReason.AlgorithmNotAllowed);

        // An RS256 token naming the app's ES256 key.
        await ExpectAsync(env, TestTokens.Rs256(env.RsaA, env.Claims(), "ec-a"), LaunchRefusalReason.AlgorithmNotAllowed, env.AppA);
    });

    [MetadataSqlFact]
    public Task Wrong_audience() => RunAsync(async env =>
    {
        await ExpectAsync(env, env.Token(c => c["aud"] = "https://elsewhere"), LaunchRefusalReason.WrongAudience, env.AppA);
        await ExpectAsync(env, env.Token(c => c.Remove("aud")), LaunchRefusalReason.WrongAudience, env.AppA);
        Assert.Null((await env.Validate(env.Token(c => c["aud"] = new[] { "other", Audience }))).Refusal);
    });

    [MetadataSqlFact]
    public Task A_blank_configured_audience_refuses_everything() => RunAsync(async env =>
        await ExpectAsync(env, env.Token(c => c["aud"] = ""), LaunchRefusalReason.WrongAudience, env.AppA), audience: "");

    [MetadataSqlFact]
    public Task Expired() => RunAsync(async env =>
        await ExpectAsync(env, env.Token(c => { c["iat"] = Env.Now - 200; c["exp"] = Env.Now - 100; }), LaunchRefusalReason.Expired, env.AppA));

    [MetadataSqlFact]
    public Task Not_yet_valid() => RunAsync(async env =>
    {
        await ExpectAsync(env, env.Token(c => { c["iat"] = Env.Now + 300; c["exp"] = Env.Now + 360; }), LaunchRefusalReason.NotYetValid, env.AppA);
        await ExpectAsync(env, env.Token(c => c["nbf"] = Env.Now + 300), LaunchRefusalReason.NotYetValid, env.AppA);
    });

    [MetadataSqlFact]
    public Task Lifetime_too_long() => RunAsync(async env =>
    {
        await ExpectAsync(env, env.Token(c => c["exp"] = Env.Now + 121), LaunchRefusalReason.LifetimeTooLong, env.AppA);
        var iat = Env.Now;
        Assert.Null((await env.Validate(env.Token(c => { c["iat"] = iat; c["exp"] = iat + 120; }))).Refusal);
    });

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static LaunchTokenValidator ValidatorAt(Env env, FakeClock clock, LaunchReplayStore? store = null) =>
        new(env.Services.GetRequiredService<IHubWideScopeFactory>(), store ?? new LaunchReplayStore(), new LaunchValidationSettings(Audience), clock);

    private static Dictionary<string, object?> ClaimsAt(Env env, long iat, long exp, Action<Dictionary<string, object?>>? change = null) =>
        env.Claims(c => { c["iat"] = iat; c["exp"] = exp; change?.Invoke(c); });

    [MetadataSqlFact]
    public Task Clock_skew_of_30s_is_tolerated_31s_is_not() => RunAsync(async env =>
    {
        var t0 = DateTimeOffset.FromUnixTimeSeconds(Env.Now);
        var clock = new FakeClock(t0);
        var validator = ValidatorAt(env, clock);
        var iat = t0.ToUnixTimeSeconds();
        var exp = iat + 60;

        clock.Now = DateTimeOffset.FromUnixTimeSeconds(exp + 30);
        Assert.Null((await validator.ValidateAsync(TestTokens.Rs256(env.RsaA, ClaimsAt(env, iat, exp)), default)).Refusal);
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(exp + 31);
        Assert.Equal(LaunchRefusalReason.Expired, (await validator.ValidateAsync(TestTokens.Rs256(env.RsaA, ClaimsAt(env, iat, exp)), default)).Refusal);

        clock.Now = DateTimeOffset.FromUnixTimeSeconds(iat - 30);
        Assert.Null((await validator.ValidateAsync(TestTokens.Rs256(env.RsaA, ClaimsAt(env, iat, exp)), default)).Refusal);
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(iat - 31);
        Assert.Equal(LaunchRefusalReason.NotYetValid, (await validator.ValidateAsync(TestTokens.Rs256(env.RsaA, ClaimsAt(env, iat, exp)), default)).Refusal);
    });

    [MetadataSqlFact]
    public Task A_used_token_stays_replayed_at_the_expiry_boundary_even_when_cleanup_is_due() => RunAsync(async env =>
    {
        var t0 = DateTimeOffset.FromUnixTimeSeconds(Env.Now);
        var clock = new FakeClock(t0);
        var validator = ValidatorAt(env, clock);
        var iat = t0.ToUnixTimeSeconds();
        var exp = iat + 60;
        var token = TestTokens.Rs256(env.RsaA, ClaimsAt(env, iat, exp));

        Assert.Null((await validator.ValidateAsync(token, default)).Refusal);

        // Whole seconds say "not expired yet" (exp + 30), while a full-precision cleanup would already have dropped the row.
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(exp + 30).AddMilliseconds(500);
        var again = await validator.ValidateAsync(token, default);
        Assert.NotNull(again.Refusal);
        Assert.Equal(LaunchRefusalReason.Replayed, again.Refusal);

        // Cleanup still removes rows once well past expiry.
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(exp + 30 + 600);
        await validator.ValidateAsync(TestTokens.Rs256(env.RsaA, ClaimsAt(env, clock.Now.ToUnixTimeSeconds(), clock.Now.ToUnixTimeSeconds() + 60)), default);
        await using var db = new AppDbContext(env.Options, new FixedAppContext(App.NoneId, hubWide: true));
        Assert.Single(await db.LaunchReplays.ToListAsync());
    });

    [MetadataSqlFact]
    public Task Oversized_tokens_are_malformed_not_bad_signature() => RunAsync(async env =>
    {
        // Above the handler's 250 KB default but within our cap: must verify normally.
        Assert.Null((await env.Validate(env.Token(c => c["pad"] = new string('x', 300 * 1024)))).Refusal);
        await ExpectAsync(env, env.Token(c => c["pad"] = new string('x', 520 * 1024)), LaunchRefusalReason.Malformed);
    });

    [MetadataSqlFact]
    public Task Es256_keys_must_be_p256() => RunAsync(async env =>
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.brainpoolP256r1);
        await using (var db = new AppDbContext(env.Options, new FixedAppContext(App.NoneId, hubWide: true)))
        {
            db.AppSigningKeys.Add(new AppSigningKey { AppId = env.AppA, Kid = "ec-brainpool", Algorithm = "ES256", PublicKeyPem = TestTokens.Pem(other) });
            await db.SaveChangesAsync();
        }

        // Same 256-bit size, wrong curve: refused even though the signature itself is genuine.
        await ExpectAsync(env, TestTokens.Es256(other, env.Claims(), "ec-brainpool"), LaunchRefusalReason.BadSignature, env.AppA);
        Assert.Null((await env.Validate(TestTokens.Es256(env.EcA, env.Claims()))).Refusal);
    });

    [MetadataSqlFact]
    public Task Name_and_email_have_length_caps() => RunAsync(async env =>
    {
        Assert.Null((await env.Validate(env.Token(c => { c["name"] = new string('n', 200); c["email"] = new string('e', 320); }))).Refusal);
        await ExpectAsync(env, env.Token(c => c["name"] = new string('n', 201)), LaunchRefusalReason.Malformed, env.AppA);
        await ExpectAsync(env, env.Token(c => c["email"] = new string('e', 321)), LaunchRefusalReason.Malformed, env.AppA);
    });

    [MetadataSqlFact]
    public Task An_embedded_jwk_in_the_header_is_never_trusted() => RunAsync(async env =>
    {
        using var attacker = RSA.Create(2048);
        var token = TestTokens.Rs256(attacker, env.Claims(), "rsa-a", new() { ["jwk"] = TestTokens.Jwk(attacker) });
        await ExpectAsync(env, token, LaunchRefusalReason.BadSignature, env.AppA);
    });

    [MetadataSqlFact]
    public Task Missing_claims() => RunAsync(async env =>
    {
        foreach (var name in new[] { "iat", "exp", "jti", "sub", "role" })
        {
            await ExpectAsync(env, env.Token(c => c.Remove(name)), LaunchRefusalReason.MissingClaim, env.AppA);
        }

        await ExpectAsync(env, env.Token(c => c.Remove("iss")), LaunchRefusalReason.MissingClaim);
        await ExpectAsync(env, env.Token(c => c["sub"] = ""), LaunchRefusalReason.MissingClaim, env.AppA);
    });

    [MetadataSqlFact]
    public Task Unknown_role() => RunAsync(async env =>
    {
        foreach (var role in new[] { "admin", "Owner", "2", "" })
        {
            var expected = role == "" ? LaunchRefusalReason.MissingClaim : LaunchRefusalReason.UnknownRole;
            await ExpectAsync(env, env.Token(c => c["role"] = role), expected, env.AppA);
        }
    });

    [MetadataSqlFact]
    public Task Return_url_must_exactly_match_a_registered_one() => RunAsync(async env =>
    {
        await ExpectAsync(env, env.Token(c => c["returnUrl"] = "https://evil.example/home"), LaunchRefusalReason.ReturnUrlNotRegistered, env.AppA);
        await ExpectAsync(env, env.Token(c => c["returnUrl"] = ReturnUrl.ToUpperInvariant()), LaunchRefusalReason.ReturnUrlNotRegistered, env.AppA);
        await ExpectAsync(env, env.Token(c => c["returnUrl"] = ReturnUrl + "/"), LaunchRefusalReason.ReturnUrlNotRegistered, env.AppA);
    });

    [MetadataSqlFact]
    public Task Undeclared_scope_key() => RunAsync(async env =>
    {
        await ExpectAsync(env, env.Token(c => c["scope"] = new Dictionary<string, string[]> { ["Nope"] = ["1"] }), LaunchRefusalReason.UndeclaredScopeKey, env.AppA);
        // Declared only on another app's data source.
        await ExpectAsync(env, env.Token(c => c["scope"] = new Dictionary<string, string[]> { ["OnlyB"] = ["1"] }), LaunchRefusalReason.UndeclaredScopeKey, env.AppA);
    });

    [MetadataSqlFact]
    public Task Scope_key_declared_only_on_one_of_the_apps_data_sources_is_accepted() => RunAsync(async env =>
    {
        var result = await env.Validate(env.Token(c => c["scope"] = new Dictionary<string, string[]> { ["Region"] = ["east"] }));
        Assert.Null(result.Refusal);
        Assert.Equal(["east"], result.Claims!.Scope.ValuesFor("region"));
    });

    [MetadataSqlFact]
    public Task Scope_missing_and_scope_with_unrestricted() => RunAsync(async env =>
    {
        await ExpectAsync(env, env.Token(c => c.Remove("scope")), LaunchRefusalReason.ScopeMissing, env.AppA);
        await ExpectAsync(env, env.Token(c => c["scope"] = new Dictionary<string, string[]>()), LaunchRefusalReason.ScopeMissing, env.AppA);
        await ExpectAsync(env, env.Token(c => c["unrestricted"] = true), LaunchRefusalReason.ScopeAndUnrestricted, env.AppA);
        await ExpectAsync(env, env.Token(c => { c.Remove("scope"); c["unrestricted"] = false; }), LaunchRefusalReason.Malformed, env.AppA);
    });

    [MetadataSqlFact]
    public Task Scope_of_the_wrong_shape_is_malformed() => RunAsync(async env =>
    {
        await ExpectAsync(env, env.Token(c => c["scope"] = "ProgramId=10"), LaunchRefusalReason.Malformed, env.AppA);
        await ExpectAsync(env, env.Token(c => c["scope"] = new Dictionary<string, object> { ["ProgramId"] = "10" }), LaunchRefusalReason.Malformed, env.AppA);
        await ExpectAsync(env, env.Token(c => c["scope"] = new Dictionary<string, object[]> { ["ProgramId"] = [10] }), LaunchRefusalReason.Malformed, env.AppA);
        await ExpectAsync(env, env.Token(c => c["scope"] = new Dictionary<string, string[]> { ["ProgramId"] = [""] }), LaunchRefusalReason.Malformed, env.AppA);
    });

    [MetadataSqlFact]
    public Task Scope_limits() => RunAsync(async env =>
    {
        var manyKeys = Enumerable.Range(0, 21).ToDictionary(i => $"K{i}", _ => new[] { "1" });
        await ExpectAsync(env, env.Token(c => c["scope"] = manyKeys), LaunchRefusalReason.ScopeTooLarge, env.AppA);

        var manyValues = Enumerable.Range(0, 1001).Select(i => i.ToString()).ToArray();
        await ExpectAsync(env, env.Token(c => c["scope"] = new Dictionary<string, string[]> { ["ProgramId"] = manyValues }), LaunchRefusalReason.ScopeTooLarge, env.AppA);

        await ExpectAsync(env, env.Token(c => c["scope"] = new Dictionary<string, string[]> { ["ProgramId"] = [new string('x', 201)] }), LaunchRefusalReason.ScopeTooLarge, env.AppA);
        await ExpectAsync(env, env.Token(c => c["scope"] = new Dictionary<string, string[]> { ["ProgramId"] = [] }), LaunchRefusalReason.ScopeTooLarge, env.AppA);

        var atLimit = Enumerable.Range(0, 1000).Select(i => i.ToString()).ToArray();
        var edge = await env.Validate(env.Token(c => c["scope"] = new Dictionary<string, string[]> { ["ProgramId"] = atLimit, ["Region"] = [new string('x', 200)] }));
        Assert.Null(edge.Refusal);
    });

    [MetadataSqlFact]
    public Task Same_jti_twice_is_replayed() => RunAsync(async env =>
    {
        var jti = "single-use";
        Assert.Null((await env.Validate(env.Token(c => c["jti"] = jti))).Refusal);
        await ExpectAsync(env, env.Token(c => c["jti"] = jti), LaunchRefusalReason.Replayed, env.AppA);
    });

    [MetadataSqlFact]
    public Task A_refused_token_does_not_burn_its_jti() => RunAsync(async env =>
    {
        var jti = "retry";
        await ExpectAsync(env, env.Token(c => { c["jti"] = jti; c["role"] = "Owner"; }), LaunchRefusalReason.UnknownRole);
        Assert.Null((await env.Validate(env.Token(c => c["jti"] = jti))).Refusal);
    });

    [MetadataSqlFact]
    public Task The_same_jti_from_two_apps_is_not_a_replay() => RunAsync(async env =>
    {
        Assert.Null((await env.Validate(env.Token(c => c["jti"] = "shared"))).Refusal);
        var other = TestTokens.Rs256(env.RsaB, env.Claims(c => { c["iss"] = "app-b"; c["jti"] = "shared"; c["scope"] = new Dictionary<string, string[]> { ["OnlyB"] = ["1"] }; }), "rsa-b");
        Assert.Null((await env.Validate(other)).Refusal);
    });

    [MetadataSqlFact]
    public Task Expired_replay_rows_are_deleted_opportunistically() => RunAsync(async env =>
    {
        await using (var db = new AppDbContext(env.Options, new FixedAppContext(App.NoneId, hubWide: true)))
        {
            db.LaunchReplays.Add(new LaunchReplay { AppId = env.AppA, Jti = "old", ExpiresUtc = DateTimeOffset.UtcNow.AddHours(-1) });
            db.LaunchReplays.Add(new LaunchReplay { AppId = env.AppA, Jti = "future", ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        }

        Assert.Null((await env.Validate(env.Token())).Refusal);

        await using var check = new AppDbContext(env.Options, new FixedAppContext(App.NoneId, hubWide: true));
        var jtis = await check.LaunchReplays.Select(r => r.Jti).ToListAsync();
        Assert.DoesNotContain("old", jtis);
        Assert.Contains("future", jtis);
    });

    [MetadataSqlFact]
    public Task The_hub_wide_factory_scope_ignores_the_callers_app() => RunAsync(async env =>
    {
        var factory = env.Services.GetRequiredService<IHubWideScopeFactory>();
        await using var caller = env.Services.CreateAsyncScope();
        caller.ServiceProvider.GetRequiredService<IAppContextScope>().UseApp(env.AppB);

        await using var wide = factory.Create();
        var context = wide.ServiceProvider.GetRequiredService<IAppContext>();
        Assert.True(context.IsHubWide);
        Assert.Equal(env.AppB, caller.ServiceProvider.GetRequiredService<IAppContext>().AppId);
        Assert.Equal(4, await wide.ServiceProvider.GetRequiredService<AppDbContext>().Apps.CountAsync()); // three seeded plus the Default app
    });
}

internal static class LaunchTokenValidatorLimits
{
    public const int MaxTokenLength = QueryBuilder.Infrastructure.Hub.LaunchTokenValidator.MaxTokenLength;
}
