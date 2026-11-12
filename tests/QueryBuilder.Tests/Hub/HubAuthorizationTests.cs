using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor;
using QueryBuilder.Editor.Hub;
using QueryBuilder.Infrastructure.Persistence;
using Xunit;

namespace QueryBuilder.Tests.Hub;

[Collection(MetadataDatabaseCollection.Name)]
public class HubAuthorizationTests
{
    private const string Api = "/querybuilder/api/data-sources";
    private const string Admin = "/querybuilder/api/admin/access";
    private const string Registry = "/querybuilder/api/hub/apps";
    private const string Me = "/querybuilder/api/me";

    [MetadataSqlFact]
    public Task Gate_matrix_for_api_admin_and_registry() => HubApiHost.RunAsync(async env =>
    {
        async Task<HubApiHost.Browser> Persona(string name)
        {
            var browser = env.NewBrowser();
            switch (name)
            {
                case "viewer": await browser.SignInAppAsync(env.AppA, HubRole.Viewer); break;
                case "author": await browser.SignInAppAsync(env.AppA, HubRole.Author); break;
                case "admin": await browser.SignInAppAsync(env.AppA, HubRole.Admin); break;
                case "super": await browser.SignInSuperAsync(); break;
                case "super-viewing": await browser.SignInSuperAsync(env.AppA); break;
            }

            return browser;
        }

        // api, admin, registry
        var expected = new (string Who, HttpStatusCode Api, HttpStatusCode Admin, HttpStatusCode Registry)[]
        {
            ("none", HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
            ("viewer", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("author", HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("admin", HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Forbidden),
            ("super", HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.OK),
            ("super-viewing", HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.OK),
        };

        foreach (var row in expected)
        {
            var browser = await Persona(row.Who);
            Assert.True(row.Api == await browser.StatusAsync(Api), $"{row.Who} api");
            Assert.True(row.Admin == await browser.StatusAsync(Admin), $"{row.Who} admin");
            Assert.True(row.Registry == await browser.StatusAsync(Registry), $"{row.Who} registry");
        }
    });

    [MetadataSqlFact]
    public Task A_challenge_never_redirects_to_the_host_login() => HubApiHost.RunAsync(async env =>
    {
        // A real challenge of the hub scheme on a path that is not an API path: the cookie handler would otherwise
        // redirect to /Account/Login. It answers a bare 401 instead, with no Location at all.
        var response = await env.NewBrowser().GetAsync("/querybuilder/test/challenge");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    });

    [MetadataSqlFact]
    public Task Hub_mode_ignores_the_hosts_own_authorization_options() => HubApiHost.RunAsync(async env =>
    {
        // The host's options default to Anonymous; without a session the hub still says 401.
        Assert.Equal(HttpStatusCode.Unauthorized, await env.NewBrowser().StatusAsync(Api));
    });

    [MetadataSqlFact]
    public Task Me_works_for_a_super_admin_who_has_not_chosen_an_app() => HubApiHost.RunAsync(async env =>
    {
        var browser = env.NewBrowser();
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.StatusAsync(Me));

        await browser.SignInSuperAsync();
        var me = await HubApiHost.Browser.JsonAsync(await browser.GetAsync(Me));
        Assert.True(me.GetProperty("isSuperAdmin").GetBoolean());
        Assert.Equal("Admin", me.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("viewingApp").ValueKind);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("app").ValueKind);
        Assert.Equal("super:admin-oid", me.GetProperty("userId").GetString());
    });

    [MetadataSqlFact]
    public Task Me_shows_hub_fields_only_in_hub_mode() => HubApiHost.RunAsync(async env =>
    {
        var user = env.NewBrowser();
        await user.SignInAppAsync(env.AppA, HubRole.Author, "mary");
        var me = await HubApiHost.Browser.JsonAsync(await user.GetAsync(Me));
        Assert.Equal("mary", me.GetProperty("userId").GetString());
        Assert.Equal("Author", me.GetProperty("role").GetString());
        Assert.False(me.GetProperty("isSuperAdmin").GetBoolean());
        Assert.Equal("/back", me.GetProperty("returnUrl").GetString());
        Assert.Equal("app-a", me.GetProperty("app").GetProperty("key").GetString());
        Assert.Equal("App A", me.GetProperty("app").GetProperty("name").GetString());
        Assert.Equal(env.AppA, me.GetProperty("app").GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("viewingApp").ValueKind);

        var super = env.NewBrowser();
        await super.SignInSuperAsync(env.AppA);
        var viewing = await HubApiHost.Browser.JsonAsync(await super.GetAsync(Me));
        Assert.Equal("app-a", viewing.GetProperty("viewingApp").GetProperty("key").GetString());
    });

    [MetadataSqlFact]
    public Task Embedded_me_keeps_its_exact_shape() => HubApiHost.RunAsync(async env =>
    {
        var response = await env.NewBrowser().GetAsync(Me);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await HubApiHost.Browser.JsonAsync(response);
        Assert.Equal(["displayName", "userId", "zone"], me.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }, hub: false);

    [MetadataSqlFact]
    public Task Request_scope_cannot_turn_hub_wide() => HubApiHost.RunAsync(async env =>
    {
        using (var scope = env.Host.Services.CreateScope())
        {
            Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAppContextScope>().UseHubWide());
            Assert.False(scope.ServiceProvider.GetRequiredService<IAppContext>().IsHubWide);
        }

        // The factory is the one way, and its scope is a different scope from the request's.
        await using var wide = env.Host.Services.GetRequiredService<IHubWideScopeFactory>().Create();
        Assert.True(wide.ServiceProvider.GetRequiredService<IAppContext>().IsHubWide);
        await Task.CompletedTask;
    });

    [Fact]
    public void Embedded_scopes_may_still_choose_hub_wide()
    {
        var accessor = new AppContextAccessor(new MetadataMode(false));
        accessor.UseHubWide();
        Assert.True(accessor.IsHubWide);

        Assert.Throws<InvalidOperationException>(() => new AppContextAccessor(new MetadataMode(true)).UseHubWide());
    }

    // ---- carry-overs from the super admin review ----

    private static SuperAdminRecognition Recognize(SuperAdminOptions allowed, params (string, string)[] claims) =>
        SuperAdminRecognizer.Recognize(
            new ClaimsPrincipal(new ClaimsIdentity(claims.Select(c => new Claim(c.Item1, c.Item2)).Append(new Claim("tid", "tenant-1")), "host")),
            allowed);

    [Fact]
    public void A_subject_claim_labels_the_session_but_never_matches_the_object_id_list()
    {
        var allowed = new SuperAdminOptions { TenantIds = ["tenant-1"], ObjectIds = ["sub-1"], Emails = ["boss@x.test"] };

        // "sub" equal to an allowlisted id is not an object id.
        var bySub = Recognize(allowed, ("sub", "sub-1"));
        Assert.Null(bySub.Match);
        Assert.Equal(SuperAdminRefusalReason.NotAllowlisted, bySub.Refusal);

        // The same id as a real oid claim does match.
        Assert.Equal(SuperAdminMatchKind.ObjectId, Recognize(allowed, ("oid", "sub-1")).Match!.Kind);

        // Another kind of entry still matches, and the subject is the label the session carries.
        var byEmail = Recognize(allowed, ("sub", "sub-9"), ("email", "boss@x.test")).Match!;
        Assert.Equal(SuperAdminMatchKind.Email, byEmail.Kind);
        Assert.Equal("sub-9", byEmail.ObjectId);
    }

    [Fact]
    public void An_id_too_long_for_the_audit_columns_is_refused_not_truncated()
    {
        var allowed = new SuperAdminOptions { TenantIds = ["tenant-1"], ObjectIds = [new string('a', 201), new string('b', 200)] };

        var tooLong = Recognize(allowed, ("oid", new string('a', 201)));
        Assert.Null(tooLong.Match);
        Assert.Equal(SuperAdminRefusalReason.NoStableId, tooLong.Refusal);

        Assert.Equal(new string('b', 200), Recognize(allowed, ("oid", new string('b', 200))).Match!.ObjectId);
    }
}
