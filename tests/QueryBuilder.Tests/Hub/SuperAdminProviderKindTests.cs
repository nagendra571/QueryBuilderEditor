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

    [Fact]
    public void StillAllowed_for_UserId_and_Code()
    {
        var o = Oidc(x => x.CodeNames = ["ops"]);
        Assert.True(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.UserId, "user-1", "", o));
        Assert.False(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.UserId, "user-2", "", o));
        Assert.True(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.Code, "ops", "", o));
        Assert.False(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.Code, "OPS", "", o));
        Assert.False(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.Code, "gone", "", o));
        // A UserId session is only valid while the provider kind is still OpenIdConnect.
        Assert.False(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.UserId, "user-1", "", new SuperAdminOptions { UserIds = ["user-1"] }));
        // Codes do not depend on a provider or tenant.
        Assert.True(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.Code, "ops", "", new SuperAdminOptions { CodeNames = ["ops"] }));
    }

    [Fact]
    public void StillAllowed_needs_the_tenant_only_for_Entra()
    {
        var entra = new SuperAdminOptions { TenantIds = [Tenant], Emails = ["a@b.com"] };
        Assert.True(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.Email, "a@b.com", Tenant, entra));
        Assert.False(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.Email, "a@b.com", "", entra));
        Assert.True(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.Email, "boss@example.com", "", Oidc()));
        Assert.True(SuperAdminRecognizer.StillAllowed(SuperAdminMatchKind.Group, "admins", "", Oidc()));
    }

    [Fact]
    public void A_session_without_a_tenant_can_be_read_back()
    {
        var principal = HubSession.ForSuperAdmin("user-1", null, null, null, DateTimeOffset.UtcNow, SuperAdminMatchKind.UserId, "user-1", "");
        Assert.True(HubSession.TryReadSuperMatch(principal, out var kind, out var value, out var tenant));
        Assert.Equal(SuperAdminMatchKind.UserId, kind);
        Assert.Equal("user-1", value);
        Assert.Equal("", tenant);
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
