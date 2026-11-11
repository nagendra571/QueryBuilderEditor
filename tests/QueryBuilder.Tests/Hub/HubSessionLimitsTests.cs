using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using QueryBuilder.Editor;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using Xunit;

namespace QueryBuilder.Tests.Hub;

/// <summary>Per-app session revocation and the super admin allowlist re-check, both on a TestServer with a fake clock.</summary>
[Collection(MetadataDatabaseCollection.Name)]
public class HubSessionLimitsTests
{
    private const string Apps = "/querybuilder/api/hub/apps";
    private const string Me = "/querybuilder/api/me";

    private static readonly TimeSpan PastCache = TimeSpan.FromSeconds(31);

    private static async Task<HubApiHost.Browser> SuperAsync(HubApiHost env)
    {
        var browser = env.NewBrowser();
        await browser.SignInSuperAsync();
        return browser;
    }

    private static Task<HttpResponseMessage> RenameAsync(HubApiHost.Browser super, Guid id, bool enabled) =>
        super.PutAsync($"{Apps}/{id}", new { name = "App A", isEnabled = enabled, scopeKeys = new[] { "ProgramId" } });

    [MetadataSqlFact]
    public Task Turning_an_app_off_ends_its_sessions_for_good_even_after_it_is_turned_back_on() => HubApiHost.RunAsync(async env =>
    {
        var user = env.NewBrowser();
        await user.SignInAppAsync(env.AppA, HubRole.Viewer);
        Assert.Equal(HttpStatusCode.OK, await user.StatusAsync(Me));

        env.Clock.Now += TimeSpan.FromSeconds(5);
        var super = await SuperAsync(env);
        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(super, env.AppA, enabled: false)).StatusCode);
        env.Clock.Now += PastCache;
        Assert.Equal(HttpStatusCode.Unauthorized, await user.StatusAsync(Me));

        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(super, env.AppA, enabled: true)).StatusCode);
        env.Clock.Now += PastCache;
        Assert.Equal(HttpStatusCode.Unauthorized, await user.StatusAsync(Me));

        var fresh = env.NewBrowser();
        await fresh.SignInAppAsync(env.AppA, HubRole.Viewer);
        Assert.Equal(HttpStatusCode.OK, await fresh.StatusAsync(Me));
    });

    [MetadataSqlFact]
    public Task Revoking_a_signing_key_ends_every_session_of_that_app_but_not_later_ones_or_other_apps() => HubApiHost.RunAsync(async env =>
    {
        var old = env.NewBrowser();
        await old.SignInAppAsync(env.AppA, HubRole.Author);
        var other = env.NewBrowser();
        await other.SignInAppAsync(env.AppB, HubRole.Author);

        env.Clock.Now += TimeSpan.FromSeconds(5);
        var super = await SuperAsync(env);
        Assert.Equal(HttpStatusCode.NoContent, (await super.SendAsync(HttpMethod.Delete, $"{Apps}/{env.AppA}/keys/rsa-a")).StatusCode);
        DateTimeOffset? stamp;
        await using (var db = env.HubWideDb())
        {
            stamp = (await db.Apps.SingleAsync(a => a.Id == env.AppA)).SessionsValidAfterUtc;
        }

        Assert.NotNull(stamp);

        // Within 30 seconds the cached answer may still say valid; after that the session is gone.
        env.Clock.Now += PastCache;
        Assert.Equal(HttpStatusCode.Unauthorized, await old.StatusAsync(Me));
        Assert.Equal(HttpStatusCode.OK, await other.StatusAsync(Me));

        var later = env.NewBrowser();
        await later.SignInAppAsync(env.AppA, HubRole.Author);
        Assert.Equal(HttpStatusCode.OK, await later.StatusAsync(Me));

        // Repeating the revoke changes nothing, the stamp included.
        env.Clock.Now += PastCache;
        Assert.Equal(HttpStatusCode.NoContent, (await super.SendAsync(HttpMethod.Delete, $"{Apps}/{env.AppA}/keys/rsa-a")).StatusCode);
        await using var check = env.HubWideDb();
        Assert.Equal(stamp, (await check.Apps.SingleAsync(a => a.Id == env.AppA)).SessionsValidAfterUtc);
        env.Clock.Now += PastCache;
        Assert.Equal(HttpStatusCode.OK, await later.StatusAsync(Me));
    });

    [MetadataSqlFact]
    public Task A_session_issued_after_the_stamp_is_never_ended_by_it() => HubApiHost.RunAsync(async env =>
    {
        var super = await SuperAsync(env);
        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(super, env.AppA, enabled: false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(super, env.AppA, enabled: true)).StatusCode);

        env.Clock.Now += TimeSpan.FromSeconds(2);
        var user = env.NewBrowser();
        await user.SignInAppAsync(env.AppA, HubRole.Viewer);
        env.Clock.Now += PastCache;
        Assert.Equal(HttpStatusCode.OK, await user.StatusAsync(Me));
    });

    // The options are fixed for a process, so "removed from the allowlist" means a restart with new options. The
    // second host shares the database, and with it the Data Protection key ring, so the old cookie still decrypts.
    private static async Task AssertSuperEndsAsync(string kind, string value, string tenant,
        Action<QueryBuilderEditorOptions> first, Action<QueryBuilderEditorOptions> second)
    {
        await HubApiHost.RunAsync(async env =>
        {
            var super = env.NewBrowser();
            await super.SignInSuperMatchedAsync(kind, value, tenant);
            Assert.Equal(HttpStatusCode.OK, await super.StatusAsync(Apps));

            await env.RestartAsync(o => first(o));
            var same = super.MovedTo(env.Host.GetTestClient());
            Assert.Equal(HttpStatusCode.OK, await same.StatusAsync(Apps));

            await env.RestartAsync(o => second(o));
            var after = super.MovedTo(env.Host.GetTestClient());
            Assert.Equal(HttpStatusCode.Unauthorized, await after.StatusAsync(Apps));
        }, configure: o => first(o));
    }

    [MetadataSqlFact]
    public Task A_super_session_ends_when_its_object_id_leaves_the_allowlist() => AssertSuperEndsAsync("ObjectId", "oid-1", "tenant-1",
        o => { o.Hub.SuperAdmins.ObjectIds = ["oid-1", "keep"]; },
        o => { o.Hub.SuperAdmins.ObjectIds = ["keep"]; });

    [MetadataSqlFact]
    public Task A_super_session_ends_when_its_email_leaves_the_allowlist() => AssertSuperEndsAsync("Email", "boss@x.com", "tenant-1",
        o => { o.Hub.SuperAdmins.Emails = ["BOSS@x.com"]; },
        o => { o.Hub.SuperAdmins.Emails = []; });

    [MetadataSqlFact]
    public Task A_super_session_ends_when_its_group_leaves_the_allowlist() => AssertSuperEndsAsync("Group", "11111111-1111-1111-1111-111111111111", "tenant-1",
        o => { o.Hub.SuperAdmins.GroupIds = ["11111111-1111-1111-1111-111111111111"]; },
        o => { o.Hub.SuperAdmins.GroupIds = ["22222222-2222-2222-2222-222222222222"]; });

    [MetadataSqlFact]
    public Task A_super_session_ends_when_its_tenant_leaves_the_allowlist() => AssertSuperEndsAsync("ObjectId", "admin-oid", "tenant-1",
        o => { o.Hub.SuperAdmins.TenantIds = ["tenant-1", "tenant-2"]; },
        o => { o.Hub.SuperAdmins.TenantIds = ["tenant-2"]; });

    [MetadataSqlFact]
    public Task A_super_session_whose_match_is_not_on_the_allowlist_is_refused() => HubApiHost.RunAsync(async env =>
    {
        var super = env.NewBrowser();
        // A group the host never listed: the re-check finds nothing to match, so the session is not trusted.
        Assert.Equal(HttpStatusCode.OK, (await super.GetAsync("/querybuilder/test/signin?kind=super&oid=admin-oid&mkind=Group&mvalue=g&tid=tenant-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await super.StatusAsync(Apps));
    });
}
