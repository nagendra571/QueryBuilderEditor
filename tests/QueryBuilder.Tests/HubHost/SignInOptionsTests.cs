using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using QueryBuilder.Editor.Hub;
using QueryBuilder.Tests.Hub;
using Xunit;
using HubHostApp = global::QueryBuilder.Hub.HubHost;
using SignInCodeCommand = global::QueryBuilder.Hub.SignInCodeCommand;

namespace QueryBuilder.Tests.HubHostSuite;

[Collection(MetadataDatabaseCollection.Name)]
public class SignInOptionsTests
{
    private static Dictionary<string, string?> Base(string connection) => new()
    {
        ["Hub:ConnectionString"] = connection,
        ["Hub:Audience"] = "https://hub.test",
        ["Hub:DataSourceKey"] = HubApiHost.Key,
    };

    private static Dictionary<string, string?> Entra(string connection, string tenant = "tenant-1")
    {
        var s = Base(connection);
        s["Hub:SignIn:Provider:Kind"] = "Entra";
        s["Hub:SignIn:Provider:TenantId"] = tenant;
        s["Hub:SignIn:Provider:ClientId"] = "client-1";
        s["Hub:SignIn:Provider:ClientSecret"] = "not-a-real-secret";
        s["Hub:SuperAdmins:TenantIds:0"] = "tenant-1";
        s["Hub:SuperAdmins:ObjectIds:0"] = "admin-oid";
        return s;
    }

    private static Dictionary<string, string?> Oidc(string connection, string authority = "https://idp.example.com/realm")
    {
        var s = Base(connection);
        s["Hub:SignIn:Provider:Kind"] = "OpenIdConnect";
        s["Hub:SignIn:Provider:Authority"] = authority;
        s["Hub:SignIn:Provider:ClientId"] = "client-1";
        s["Hub:SignIn:Provider:ClientSecret"] = "not-a-real-secret";
        s["Hub:SuperAdmins:UserIds:0"] = "sub-1";
        return s;
    }

    private static Dictionary<string, string?> CodesOnly(string connection)
    {
        var s = Base(connection);
        s["Hub:SignIn:Codes:Enabled"] = "true";
        s["Hub:SuperAdmins:CodeNames:0"] = "priya";
        return s;
    }

    private static WebApplication Build(Dictionary<string, string?> settings) =>
        HubHostApp.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings);
        });

    [Fact]
    public void Entra_kind_configures_entra_authority()
    {
        var settings = Entra("Server=unused");
        settings["Hub:SignIn:Provider:DisplayName"] = "Contoso";
        settings["Hub:SignIn:Provider:CallbackPath"] = "/cb";

        var app = Build(settings);
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("provider");

        Assert.Equal("https://login.microsoftonline.com/tenant-1/v2.0", options.Authority);
        Assert.Equal("/cb", options.CallbackPath);
        Assert.Equal("QueryBuilder.HubExternal", options.SignInScheme);
        Assert.True(options.UsePkce);
        Assert.False(options.MapInboundClaims);
        Assert.False(options.SaveTokens);
        Assert.False(options.GetClaimsFromUserInfoEndpoint);
        Assert.Equal("Contoso", app.Services.GetRequiredService<QueryBuilder.Editor.QueryBuilderEditorOptions>().Hub.ProviderDisplayName);
        Assert.Equal(QueryBuilder.Editor.SuperAdminProviderKind.Entra, app.Services.GetRequiredService<QueryBuilder.Editor.QueryBuilderEditorOptions>().Hub.SuperAdmins.ProviderKind);
    }

    [Fact]
    public void OpenIdConnect_kind_reads_userinfo_and_uses_authority()
    {
        var settings = Oidc("Server=unused");
        settings["Hub:SignIn:Provider:GroupsClaim"] = "roles";

        var app = Build(settings);
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("provider");
        var editor = app.Services.GetRequiredService<QueryBuilder.Editor.QueryBuilderEditorOptions>();

        Assert.Equal("https://idp.example.com/realm", options.Authority);
        Assert.True(options.GetClaimsFromUserInfoEndpoint);
        Assert.True(options.UsePkce);
        Assert.False(options.MapInboundClaims);
        Assert.Equal("code", options.ResponseType);
        Assert.Equal("QueryBuilder.HubExternal", options.SignInScheme);
        Assert.Equal(QueryBuilder.Editor.SuperAdminProviderKind.OpenIdConnect, editor.Hub.SuperAdmins.ProviderKind);
        Assert.Equal("roles", editor.Hub.SuperAdmins.GroupsClaim);
        Assert.Equal(["sub-1"], editor.Hub.SuperAdmins.UserIds);
    }

    [Theory]
    [InlineData("common")]
    [InlineData("Organizations")]
    [InlineData("consumers")]
    public void Multi_tenant_entra_authority_is_refused(string tenant)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(Entra("Server=unused", tenant)));

        Assert.Contains("Hub:SignIn:Provider:TenantId", ex.Message);
    }

    [Theory]
    [InlineData("http://idp.example.com")]
    [InlineData("idp.example.com")]
    public void Openid_connect_authority_must_be_absolute_https(string authority)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(Oidc("Server=unused", authority)));

        Assert.Contains("Hub:SignIn:Provider:Authority", ex.Message);
        Assert.DoesNotContain(authority, ex.Message);
    }

    [Theory]
    [InlineData("Entra", "Hub:SignIn:Provider:ClientId")]
    [InlineData("Entra", "Hub:SignIn:Provider:ClientSecret")]
    [InlineData("Entra", "Hub:SignIn:Provider:TenantId")]
    [InlineData("OpenIdConnect", "Hub:SignIn:Provider:ClientId")]
    [InlineData("OpenIdConnect", "Hub:SignIn:Provider:ClientSecret")]
    [InlineData("OpenIdConnect", "Hub:SignIn:Provider:Authority")]
    public void Missing_provider_settings_are_named(string kind, string missing)
    {
        var settings = kind == "Entra" ? Entra("Server=unused") : Oidc("Server=unused");
        settings.Remove(missing);

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains(missing, ex.Message);
        Assert.DoesNotContain("not-a-real-secret", ex.Message);
    }

    [Fact]
    public void Unknown_kind_is_refused()
    {
        var settings = Entra("Server=unused");
        settings["Hub:SignIn:Provider:Kind"] = "Saml";

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Hub:SignIn:Provider:Kind", ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void A_numeric_kind_is_refused(string kind)
    {
        var settings = Entra("Server=unused");
        settings["Hub:SignIn:Provider:Kind"] = kind;

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Hub:SignIn:Provider:Kind", ex.Message);
    }

    [Fact]
    public void Old_Hub_Entra_settings_are_rejected_with_a_pointer()
    {
        var settings = Entra("Server=unused");
        settings["Hub:Entra:TenantId"] = "tenant-1";

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Hub:Entra", ex.Message);
        Assert.Contains("Hub:SignIn:Provider", ex.Message);
    }

    [Fact]
    public void Neither_option_refuses_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(Base("Server=unused")));

        Assert.Contains("SuperAdminSignInScheme", ex.Message);
        Assert.Contains("SignInCodes", ex.Message);
    }

    [Theory]
    [InlineData("http://login.example.com/")]
    [InlineData("login.example.com")]
    public void Entra_instance_must_be_absolute_https(string instance)
    {
        var settings = Entra("Server=unused");
        settings["Hub:SignIn:Provider:Instance"] = instance;

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Hub:SignIn:Provider:Instance", ex.Message);
    }

    [Fact]
    public void Scopes_must_include_openid_and_default_when_empty()
    {
        var settings = Oidc("Server=unused");
        settings["Hub:SignIn:Provider:Scopes:0"] = "profile";
        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));
        Assert.Contains("Hub:SignIn:Provider:Scopes", ex.Message);

        var defaults = Build(Oidc("Server=unused"));
        Assert.Equal(["openid", "profile", "email"], defaults.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("provider").Scope);
    }

    // The handler's claim actions are run on a userinfo document directly: a full round trip needs a live provider.
    [Fact]
    public void Userinfo_email_verified_and_groups_reach_the_principal_for_openid_connect()
    {
        var settings = Oidc("Server=unused");
        settings["Hub:SignIn:Provider:GroupsClaim"] = "roles";
        var options = Build(settings).Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("provider");
        using var userinfo = System.Text.Json.JsonDocument.Parse("""{"sub":"sub-1","email":"a@example.com","email_verified":true,"roles":"g1"}""");
        var identity = new System.Security.Claims.ClaimsIdentity("test");

        foreach (var action in options.ClaimActions)
        {
            action.Run(userinfo.RootElement, identity, "issuer");
        }

        Assert.Equal("true", identity.FindFirst("email_verified")?.Value, ignoreCase: true);
        Assert.Equal("g1", identity.FindFirst("roles")?.Value);
        Assert.Equal("a@example.com", identity.FindFirst("email")?.Value);
    }

    [Fact]
    public async Task Signin_code_command_refuses_when_codes_are_turned_off()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        string[] args =
        [
            "signin-code", "--name", "priya", "--Hub:ConnectionString=Server=unused", "--Hub:Audience=https://hub.test",
            $"--Hub:DataSourceKey={HubApiHost.Key}", "--Hub:SignIn:Provider:Kind=OpenIdConnect",
            "--Hub:SignIn:Provider:Authority=https://idp.example.com", "--Hub:SignIn:Provider:ClientId=c",
            "--Hub:SignIn:Provider:ClientSecret=s", "--Hub:SuperAdmins:UserIds:0=sub-1", "--Hub:SuperAdmins:CodeNames:0=priya",
        ];

        Assert.Equal(1, await SignInCodeCommand.RunAsync(args, stdout, stderr));

        Assert.Equal("", stdout.ToString());
        Assert.Contains("Hub:SignIn:Codes:Enabled", stderr.ToString());
    }

    [Theory]
    [InlineData("Hub:SignIn:Codes:PermitLimit")]
    [InlineData("Hub:SignIn:Codes:WindowSeconds")]
    public void Code_rate_limit_must_be_at_least_one(string setting)
    {
        var settings = CodesOnly("Server=unused");
        settings[setting] = "0";

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains(setting, ex.Message);
    }

    [MetadataSqlFact]
    public Task Codes_only_hub_starts_and_signs_in() => MetadataDb.WithDatabaseAsync(async (_, connection) =>
    {
        await using var app = Build(CodesOnly(connection));
        await app.StartAsync();
        try
        {
            Assert.Null(await app.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync("provider"));
            using var client = app.GetTestClient();

            var page = await client.GetAsync("/querybuilder/signin");
            var html = await page.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("/querybuilder/signin/code", html);
            Assert.DoesNotContain("/querybuilder/signin/provider", html);

            var issued = await app.Services.GetRequiredService<ISuperAdminCodeIssuer>().IssueAsync("priya", null, default);
            using var post = new HttpRequestMessage(HttpMethod.Post, "/querybuilder/signin/code")
            {
                Content = new FormUrlEncodedContent([new("code", issued.Code)]),
            };
            post.Headers.Add("Origin", "http://localhost");
            var response = await client.SendAsync(post);
            Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    });

    [MetadataSqlFact]
    public Task Code_post_is_rate_limited() => MetadataDb.WithDatabaseAsync(async (_, connection) =>
    {
        var settings = CodesOnly(connection);
        settings["Hub:SignIn:Codes:PermitLimit"] = "3";
        await using var app = Build(settings);
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();

            async Task<HttpResponseMessage> Attempt(string path = "/querybuilder/signin/code")
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent([new("code", "x")]) };
                request.Headers.Add("Origin", "http://localhost");
                return await client.SendAsync(request);
            }

            for (var i = 0; i < 3; i++)
            {
                Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Attempt()).StatusCode);
            }

            var limited = await Attempt();
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.True(limited.Headers.Contains("Retry-After"));
            // Other routes, and the launch bucket, are not drawn from this allowance.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync("/querybuilder/signin/code")).StatusCode);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Attempt("/querybuilder/launch")).StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    });

    private static string[] CommandArgs(string connection, params string[] extra) =>
    [
        "signin-code", "--name", "priya",
        $"--Hub:ConnectionString={connection}", "--Hub:Audience=https://hub.test", $"--Hub:DataSourceKey={HubApiHost.Key}",
        "--Hub:SignIn:Codes:Enabled=true", "--Hub:SuperAdmins:CodeNames:0=priya",
        .. extra,
    ];

    [MetadataSqlFact]
    public Task Signin_code_command_prints_a_working_code() => MetadataDb.WithDatabaseAsync(async (_, connection) =>
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // The process's own stdout is what a shell captures; an appsettings log level must not put log lines on it.
        var console = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(console);
        int exit;
        try
        {
            exit = await SignInCodeCommand.RunAsync(CommandArgs(connection, "--minutes", "5", "--Logging:LogLevel:Default=Information"), stdout, stderr);
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.Equal(0, exit);
        Assert.Equal("", console.ToString());
        Assert.Equal("", stderr.ToString());
        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, lines.Length);
        var code = Assert.Single(lines, l => l.StartsWith("Code: ", StringComparison.Ordinal))["Code: ".Length..];
        Assert.Single(lines, l => l.StartsWith("Expires: ", StringComparison.Ordinal));

        await using var app = Build(CodesOnly(connection));
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            using var post = new HttpRequestMessage(HttpMethod.Post, "/querybuilder/signin/code")
            {
                Content = new FormUrlEncodedContent([new("code", code)]),
            };
            post.Headers.Add("Origin", "http://localhost");
            Assert.Equal(HttpStatusCode.SeeOther, (await client.SendAsync(post)).StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    });

    [Fact]
    public async Task Signin_code_command_prints_nothing_when_audit_fails()
    {
        var bad = new SqlConnectionStringBuilder
        {
            DataSource = "127.0.0.1,1", InitialCatalog = "nope", ConnectTimeout = 2, IntegratedSecurity = true, TrustServerCertificate = true,
        }.ConnectionString;
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await SignInCodeCommand.RunAsync(CommandArgs(bad), stdout, stderr);

        Assert.Equal(1, exit);
        Assert.Equal("", stdout.ToString());
        Assert.NotEqual("", stderr.ToString());
    }

    [Fact]
    public async Task Signin_code_command_refuses_a_name_that_is_not_listed_and_missing_options()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        Assert.Equal(1, await SignInCodeCommand.RunAsync(["signin-code"], stdout, stderr));
        Assert.Equal(1, await SignInCodeCommand.RunAsync(CommandArgs("Server=unused", "--minutes", "abc"), stdout, stderr));

        Assert.Equal("", stdout.ToString());
        Assert.NotEqual("", stderr.ToString());
    }
}
