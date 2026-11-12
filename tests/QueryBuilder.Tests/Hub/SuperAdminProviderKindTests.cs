using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Hub;
using Xunit;

namespace QueryBuilder.Tests.Hub;

public class SuperAdminProviderKindTests
{
    private const string Tenant = "aaaaaaaa-1111-2222-3333-444444444444";
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static ClaimsPrincipal P(params (string, string)[] c) =>
        new(new ClaimsIdentity(c.Select(x => new Claim(x.Item1, x.Item2)), "host"));

    private static SuperAdminOptions Oidc(Action<SuperAdminOptions>? configure = null)
    {
        var o = new SuperAdminOptions
        {
            ProviderKind = SuperAdminProviderKind.OpenIdConnect,
            UserIds = ["user-1"], Emails = ["boss@example.com"], GroupIds = ["admins"],
        };
        configure?.Invoke(o);
        return o;
    }

    [Fact]
    public void OpenIdConnect_sub_in_UserIds_matches()
    {
        var r = SuperAdminRecognizer.Recognize(P(("sub", "user-1"), ("name", "Una")), Oidc());

        Assert.Equal(SuperAdminMatchKind.UserId, r.Match!.Kind);
        Assert.Equal("user-1", r.Match.ObjectId);
        Assert.Equal("user-1", r.Match.MatchedValue);
        Assert.Null(r.Match.TenantId);
        Assert.Null(SuperAdminRecognizer.Recognize(P(("sub", "user-2")), Oidc()).Match);
    }

    [Fact]
    public void OpenIdConnect_needs_no_tenant_and_ignores_one()
    {
        Assert.NotNull(SuperAdminRecognizer.Recognize(P(("sub", "user-1"), ("tid", "whatever")), Oidc()).Match);
    }

    [Fact]
    public void OpenIdConnect_without_a_sub_has_no_stable_id()
    {
        var r = SuperAdminRecognizer.Recognize(P(("oid", "user-1"), ("email", "boss@example.com"), ("email_verified", "true")), Oidc());
        Assert.Null(r.Match);
        Assert.Equal(SuperAdminRefusalReason.NoStableId, r.Refusal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("")]
    [InlineData("yes")]
    public void Unverified_email_never_matches(string? verified)
    {
        var claims = new List<(string, string)> { ("sub", "other"), ("email", "boss@example.com") };
        if (verified is not null)
        {
            claims.Add(("email_verified", verified));
        }

        var r = SuperAdminRecognizer.Recognize(P([.. claims]), Oidc());
        Assert.Null(r.Match);
        Assert.Equal(SuperAdminRefusalReason.NotAllowlisted, r.Refusal);
    }

    [Fact]
    public void A_subject_starting_with_code_colon_is_not_a_stable_id()
    {
        var r = SuperAdminRecognizer.Recognize(P(("sub", "code:priya")), Oidc(x => x.UserIds = ["code:priya"]));
        Assert.Null(r.Match);
        Assert.Equal(SuperAdminRefusalReason.NoStableId, r.Refusal);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void Verified_email_matches(string verified)
    {
        var r = SuperAdminRecognizer.Recognize(P(("sub", "other"), ("email", "BOSS@example.com"), ("email_verified", verified)), Oidc());
        Assert.Equal(SuperAdminMatchKind.Email, r.Match!.Kind);
    }

    [Fact]
    public void A_conflicting_email_verified_is_not_verified()
    {
        var r = SuperAdminRecognizer.Recognize(
            P(("sub", "other"), ("email", "boss@example.com"), ("email_verified", "true"), ("email_verified", "false")), Oidc());
        Assert.Null(r.Match);
    }

    [Fact]
    public void Only_email_claims_count_for_OpenIdConnect_not_preferred_username()
    {
        var r = SuperAdminRecognizer.Recognize(P(("sub", "other"), ("preferred_username", "boss@example.com"), ("email_verified", "true")), Oidc());
        Assert.Null(r.Match);
    }

    [Fact]
    public void Groups_read_from_configured_claim()
    {
        var o = Oidc(x => x.GroupsClaim = "roles");
        Assert.Equal(SuperAdminMatchKind.Group, SuperAdminRecognizer.Recognize(P(("sub", "other"), ("roles", "admins")), o).Match!.Kind);
        Assert.Null(SuperAdminRecognizer.Recognize(P(("sub", "other"), ("groups", "admins")), o).Match);
        Assert.Equal(SuperAdminMatchKind.Group, SuperAdminRecognizer.Recognize(P(("sub", "other"), ("groups", "admins")), Oidc()).Match!.Kind);
    }

    [Fact]
    public void Sub_matches_only_for_OpenIdConnect_kind()
    {
        var entra = new SuperAdminOptions { TenantIds = [Tenant], UserIds = ["user-1"] };
        var r = SuperAdminRecognizer.Recognize(P(("sub", "user-1"), ("tid", Tenant)), entra);
        Assert.Null(r.Match);
        Assert.Equal(SuperAdminRefusalReason.NotAllowlisted, r.Refusal);
    }

    [Fact]
    public void Entra_object_ids_do_not_match_under_OpenIdConnect()
    {
        var o = Oidc(x => x.ObjectIds = ["oid-1"]);
        Assert.Null(SuperAdminRecognizer.Recognize(P(("oid", "oid-1"), ("sub", "other")), o).Match);
    }

    [Fact]
    public void Entra_still_needs_the_tenant()
    {
        var entra = new SuperAdminOptions { TenantIds = [Tenant], ObjectIds = ["oid-1"] };
        Assert.Equal(SuperAdminRefusalReason.WrongTenant, SuperAdminRecognizer.Recognize(P(("oid", "oid-1")), entra).Refusal);
        Assert.NotNull(SuperAdminRecognizer.Recognize(P(("oid", "oid-1"), ("tid", Tenant)), entra).Match);
    }

    private static HubOptions Hub(SuperAdminOptions a, bool codes = false, string? scheme = "host") =>
        new() { SuperAdmins = a, SuperAdminSignInScheme = scheme, SignInCodes = { Enabled = codes } };

    private static bool Still(SuperAdminMatchKind kind, string value, string tenant, string? provider, HubOptions hub) =>
        SuperAdminRecognizer.StillAllowed(kind, value, tenant, provider, hub);

    private const string O = "OpenIdConnect";

    [Fact]
    public void StillAllowed_for_UserId_and_Code()
    {
        var h = Hub(Oidc(x => x.CodeNames = ["ops"]), codes: true);
        Assert.True(Still(SuperAdminMatchKind.UserId, "user-1", "", O, h));
        Assert.False(Still(SuperAdminMatchKind.UserId, "user-2", "", O, h));
        Assert.True(Still(SuperAdminMatchKind.Code, "ops", "", "Code", h));
        Assert.False(Still(SuperAdminMatchKind.Code, "OPS", "", "Code", h));
        Assert.False(Still(SuperAdminMatchKind.Code, "gone", "", "Code", h));
        // A UserId session is only valid while the provider kind is still OpenIdConnect.
        Assert.False(Still(SuperAdminMatchKind.UserId, "user-1", "", "Entra", Hub(new SuperAdminOptions { UserIds = ["user-1"] })));
        // Codes do not depend on a provider or tenant.
        Assert.True(Still(SuperAdminMatchKind.Code, "ops", "", "Code", Hub(new SuperAdminOptions { CodeNames = ["ops"] }, codes: true, scheme: null)));
    }

    [Fact]
    public void Turning_a_sign_in_method_off_ends_its_sessions()
    {
        var a = Oidc(x => x.CodeNames = ["ops"]);
        Assert.False(Still(SuperAdminMatchKind.Code, "ops", "", "Code", Hub(a, codes: false)));
        Assert.False(Still(SuperAdminMatchKind.UserId, "user-1", "", O, Hub(a, codes: true, scheme: null)));
        Assert.False(Still(SuperAdminMatchKind.Email, "boss@example.com", "", O, Hub(a, scheme: " ")));
        Assert.False(Still(SuperAdminMatchKind.Group, "admins", "", O, Hub(a, scheme: null)));
    }

    [Fact]
    public void A_session_must_name_the_current_provider_kind()
    {
        var entra = Hub(new SuperAdminOptions { TenantIds = [Tenant], Emails = ["a@b.com"] });
        Assert.True(Still(SuperAdminMatchKind.Email, "a@b.com", Tenant, "Entra", entra));
        Assert.False(Still(SuperAdminMatchKind.Email, "a@b.com", Tenant, null, entra));
        Assert.False(Still(SuperAdminMatchKind.Email, "a@b.com", Tenant, O, entra));
        // An Entra session does not survive a switch to OpenID Connect, even when the email is also listed.
        Assert.False(Still(SuperAdminMatchKind.Email, "boss@example.com", Tenant, "Entra", Hub(Oidc())));
        Assert.False(Still(SuperAdminMatchKind.Code, "ops", "", "Entra", Hub(new SuperAdminOptions { CodeNames = ["ops"] }, codes: true)));
    }

    [Fact]
    public void StillAllowed_needs_the_tenant_only_for_Entra()
    {
        var entra = Hub(new SuperAdminOptions { TenantIds = [Tenant], Emails = ["a@b.com"] });
        Assert.True(Still(SuperAdminMatchKind.Email, "a@b.com", Tenant, "Entra", entra));
        Assert.False(Still(SuperAdminMatchKind.Email, "a@b.com", "", "Entra", entra));
        Assert.True(Still(SuperAdminMatchKind.Email, "boss@example.com", "", O, Hub(Oidc())));
        Assert.True(Still(SuperAdminMatchKind.Group, "admins", "", O, Hub(Oidc())));
    }

    [Fact]
    public void OpenIdConnect_match_email_is_the_verified_one_or_null()
    {
        var ok = SuperAdminRecognizer.Recognize(P(("sub", "user-1"), ("email", "x@y.com"), ("email_verified", "true")), Oidc());
        Assert.Equal("x@y.com", ok.Match!.Email);
        var unverified = SuperAdminRecognizer.Recognize(P(("sub", "user-1"), ("email", "x@y.com"), ("email_verified", "false")), Oidc());
        Assert.Null(unverified.Match!.Email);
    }

    [Fact]
    public void A_session_without_a_tenant_can_be_read_back()
    {
        var principal = HubSession.ForSuperAdmin("user-1", null, null, null, DateTimeOffset.UtcNow, SuperAdminMatchKind.UserId, "user-1", "", O);
        Assert.True(HubSession.TryReadSuperMatch(principal, out var kind, out var value, out var tenant));
        Assert.Equal(SuperAdminMatchKind.UserId, kind);
        Assert.Equal("user-1", value);
        Assert.Equal("", tenant);
        Assert.Equal(O, HubSession.ReadProvider(principal));
    }

    private static InvalidOperationException StartupFailure(Action<QueryBuilderEditorOptions> configure) =>
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=unused;Database=unused";
            o.Hub.Enabled = true;
            o.DataSourceManagement.EncryptionKey = Key;
            o.Hub.Audience = "https://hub.test";
            configure(o);
        }));

    [Fact]
    public void Hub_refuses_to_start_with_no_sign_in_option()
    {
        var ex = StartupFailure(o => o.Hub.SuperAdmins = new SuperAdminOptions { Emails = ["secret@example.com"], TenantIds = ["tenant-secret"] });
        Assert.Contains("SuperAdminSignInScheme", ex.Message);
        Assert.Contains("SignInCodes", ex.Message);
        Assert.DoesNotContain("secret", ex.Message);

        // Codes enabled but nobody allowed to use one is still no option.
        var ex2 = StartupFailure(o =>
        {
            o.Hub.SignInCodes.Enabled = true;
            o.Hub.SuperAdmins = new SuperAdminOptions { CodeNames = [" "] };
        });
        Assert.Contains("CodeNames", ex2.Message);
    }

    [Fact]
    public void OpenIdConnect_kind_needs_an_allowlist_entry()
    {
        var ex = StartupFailure(o =>
        {
            o.Hub.SuperAdminSignInScheme = "host-scheme-secret";
            o.Hub.SuperAdmins = new SuperAdminOptions { ProviderKind = SuperAdminProviderKind.OpenIdConnect, UserIds = [" "], ObjectIds = ["oid-secret"] };
        });
        Assert.Contains("UserIds", ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public void OpenIdConnect_kind_does_not_need_tenant_ids()
    {
        var services = new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=unused;Database=unused";
            o.Hub.Enabled = true;
            o.DataSourceManagement.EncryptionKey = Key;
            o.Hub.Audience = "https://hub.test";
            o.Hub.SuperAdminSignInScheme = "host-scheme";
            o.Hub.SuperAdmins = new SuperAdminOptions { ProviderKind = SuperAdminProviderKind.OpenIdConnect, UserIds = ["u"] };
        });
        Assert.NotNull(services);
    }

    [Fact]
    public void Codes_only_passes_validation()
    {
        var services = new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=unused;Database=unused";
            o.Hub.Enabled = true;
            o.DataSourceManagement.EncryptionKey = Key;
            o.Hub.Audience = "https://hub.test";
            o.Hub.SignInCodes.Enabled = true;
            o.Hub.SuperAdmins = new SuperAdminOptions { CodeNames = ["ops"] };
        });
        Assert.NotNull(services);
    }
}
