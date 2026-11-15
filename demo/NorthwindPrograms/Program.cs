using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using QueryBuilder.Launch;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAntiforgery();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    // Nothing here is decided from the client address, so trusting whatever fronts the app is acceptable for the demo.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();
var log = app.Logger;

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error");

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});
app.UseStaticFiles();

const string CookieName = "nw-person";
var cfg = app.Configuration;
var appKey = cfg["Demo:AppKey"] is { Length: > 0 } k ? k : "northwind";
var hubUrl = cfg["Demo:HubUrl"] ?? "";
var returnUrl = cfg["Demo:ReturnUrl"] is { Length: > 0 } r ? r : null;
var connString = cfg.GetConnectionString("Programs");

LaunchSigningKey? signingKey = null;
var pem = cfg["Demo:PrivateKeyPem"];
if (!string.IsNullOrWhiteSpace(pem))
{
    try
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        signingKey = LaunchSigningKey.Rsa(cfg["Demo:KeyId"] is { Length: > 0 } kid ? kid : "demo", rsa);
    }
    catch (Exception ex) when (ex is CryptographicException or ArgumentException)
    {
        log.LogError(ex, "Demo:PrivateKeyPem could not be read as an RSA key.");
    }
}

var people = new[]
{
    new Person("alex", "Alex Morgan", "alex.morgan@northwind.example", LaunchRole.Admin, null, "Admin", "all programs"),
    new Person("mary", "Mary Chen", "mary.chen@northwind.example", LaunchRole.Admin, 10, "Admin", "Program 10"),
    new Person("raj", "Raj Patel", "raj.patel@northwind.example", LaunchRole.Admin, 20, "Admin", "Program 20"),
    new Person("lee", "Lee Brooks", "lee.brooks@northwind.example", LaunchRole.Viewer, 10, "Viewer", "Program 10"),
};
var programNames = new Dictionary<int, string> { [10] = "Youth Coding Academy", [20] = "Adult Literacy" };

Person Current(HttpContext ctx) =>
    people.FirstOrDefault(p => p.Key == ctx.Request.Cookies[CookieName]) ?? people[0];

string H(object? v) => WebUtility.HtmlEncode(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "");

async Task<IResult> Page(HttpContext ctx, string active, string title, Func<Person, Task<string>> body)
{
    var me = Current(ctx);
    var tokens = ctx.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx);
    string content;
    try { content = await body(me); }
    catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
    {
        log.LogError(ex, "Demo database call failed on {Page}", title);
        content = "<p class=\"notice\">The demo database is waking up. Refresh in a minute.</p>";
    }

    var options = string.Concat(people.Select(p =>
        $"<option value=\"{p.Key}\"{(p.Key == me.Key ? " selected" : "")}>{H(p.Name)} · {p.RoleText} · {H(p.ScopeText)}</option>"));
    string Link(string href, string id, string text) =>
        $"<a href=\"{href}\"{(id == active ? " class=\"active\"" : "")}>{text}</a>";

    var html = $"""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>{H(title)} - Northwind Programs</title><link rel="stylesheet" href="/site.css"></head><body>
<header><span class="brand">Northwind Programs</span>
<form method="post" action="/switch"><input type="hidden" name="{H(tokens.FormFieldName)}" value="{H(tokens.RequestToken)}">
<label for="person">Signed in as</label><select id="person" name="person" onchange="this.form.submit()">{options}</select>
<noscript><button type="submit">Switch</button></noscript></form></header>
<div class="layout"><nav>{Link("/", "home", "Home")}{Link("/enrolments", "enrolments", "Enrolments")}{Link("/budgets", "budgets", "Budgets")}{Link("/open-querybuilder", "reports", "Reports ↗")}</nav>
<main><h1>{H(title)}</h1>{content}</main></div>
<footer>First visit can take up to a minute while the demo wakes up. This is a demo app with made-up data.</footer></body></html>
""";
    return Results.Content(html, "text/html; charset=utf-8");
}

async Task<List<object?[]>> Query(string sql, Person me, int columns, string idColumn)
{
    if (string.IsNullOrWhiteSpace(connString)) throw new InvalidOperationException("ConnectionStrings:Programs is not configured.");
    await using var conn = new SqlConnection(connString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandTimeout = 120;
    cmd.CommandText = sql;
    if (me.ProgramId is int pid)
    {
        cmd.CommandText += $" WHERE {idColumn} = @programId";
        cmd.Parameters.AddWithValue("@programId", pid);
    }
    var rows = new List<object?[]>();
    await using var rd = await cmd.ExecuteReaderAsync();
    while (await rd.ReadAsync())
    {
        var row = new object?[columns];
        rd.GetValues(row);
        rows.Add(row);
    }
    return rows;
}

string Table(string[] heads, List<object?[]> rows, params int[] numeric) =>
    "<div class=\"table-wrap\"><table><thead><tr>" +
    string.Concat(heads.Select((h, i) => $"<th{(numeric.Contains(i) ? " class=\"num\"" : "")}>{H(h)}</th>")) +
    "</tr></thead><tbody>" +
    string.Concat(rows.Select(r => "<tr>" + string.Concat(r.Select((c, i) =>
        $"<td{(numeric.Contains(i) ? " class=\"num\"" : "")}>{(c is DateTime d ? d.ToString("yyyy-MM-dd") : c is decimal m ? m.ToString("N2", CultureInfo.InvariantCulture) : H(c))}</td>")) + "</tr>")) +
    "</tbody></table></div>";

app.MapGet("/healthz", () => Results.Text("ok"));
app.MapGet("/error", () => Results.Content("<!doctype html><title>Error</title><p>Something went wrong. <a href=\"/\">Back to Northwind Programs</a></p>", "text/html"));

app.MapPost("/switch", async (HttpContext ctx, IAntiforgery af) =>
{
    try { await af.ValidateRequestAsync(ctx); }
    catch (AntiforgeryValidationException) { return Results.StatusCode(400); }
    var key = (await ctx.Request.ReadFormAsync())["person"].ToString();
    var chosen = people.FirstOrDefault(p => p.Key == key) ?? people[0];
    ctx.Response.Cookies.Append(CookieName, chosen.Key, new CookieOptions
    {
        HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = ctx.Request.IsHttps, IsEssential = true, Path = "/",
    });
    return Results.Redirect("/");
});

app.MapGet("/", (Delegate)((HttpContext ctx) => Page(ctx, "home", "Home", async me =>
{
    var access = me.ProgramId is int pid
        ? $"{H(me.First)} is {(me.RoleText == "Admin" ? "an Admin" : "a Viewer")} for program {pid} ({H(programNames.GetValueOrDefault(pid, "?"))}). In QueryBuilder {(me.First == "Lee" ? "he" : "she")} sees only program {pid}'s rows" +
          (me.RoleText == "Viewer" ? ", and can open only the dashboards that have been published to the app." : ", and can build and share reports.")
        : $"{H(me.First)} is an Admin for all programs. In QueryBuilder {H(me.First)} sees every program's rows.";
    var sb = new StringBuilder();
    sb.Append($"<p>Welcome to Northwind Programs, where we run training and community programs. {access}</p>");
    sb.Append("<a class=\"btn\" href=\"/open-querybuilder\">Open reports in QueryBuilder →</a>");
    sb.Append("<h2>Your programs</h2>");
    var rows = await Query("SELECT ProgramId, ProgramName, Region FROM sales.Programs", me, 3, "ProgramId");
    sb.Append(Table(["ID", "Program", "Region"], rows));
    return sb.ToString();
})));

app.MapGet("/enrolments", (Delegate)((HttpContext ctx) => Page(ctx, "enrolments", "Enrolments", async me =>
{
    var rows = await Query(
        "SELECT TOP (100) EnrollmentId, ProgramName, ParticipantName, EnrolledOn, Status, FeeAmount FROM sales.vw_ProgramEnrollments",
        me, 6, "ProgramId");
    return $"<p class=\"muted\">{rows.Count} enrolment{(rows.Count == 1 ? "" : "s")} shown (up to 100).</p>" +
           Table(["ID", "Program", "Participant", "Enrolled", "Status", "Fee"], rows, 5);
})));

app.MapGet("/budgets", (Delegate)((HttpContext ctx) => Page(ctx, "budgets", "Budgets", async me =>
{
    var rows = await Query(
        "SELECT TOP (100) BudgetId, ProgramName, FiscalYear, Category, Amount FROM sales.vw_ProgramBudgets",
        me, 5, "OwningProgramId");
    return $"<p class=\"muted\">{rows.Count} budget line{(rows.Count == 1 ? "" : "s")} shown (up to 100).</p>" +
           Table(["ID", "Program", "Year", "Category", "Amount"], rows, 4);
})));

app.MapGet("/open-querybuilder", (Delegate)((HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (signingKey is null || string.IsNullOrWhiteSpace(hubUrl))
        return Page(ctx, "reports", "Reports", _ => Task.FromResult(
            "<p class=\"error\">Demo not configured: the launch key or the hub address is missing. Set <code>Demo__PrivateKeyPem</code> and <code>Demo__HubUrl</code>.</p>"));
    var me = Current(ctx);
    try
    {
        var scope = me.ProgramId is int pid
            ? LaunchScope.For(new Dictionary<string, IEnumerable<string>> { ["ProgramId"] = new[] { pid.ToString(CultureInfo.InvariantCulture) } })
            : LaunchScope.Unrestricted;
        var token = LaunchToken.Create(appKey, hubUrl, signingKey, new LaunchUser(me.Email, me.Name, me.Email), me.Role, scope, returnUrl);
        return Task.FromResult(Results.Content(LaunchForm.Html(hubUrl, token), "text/html; charset=utf-8"));
    }
    catch (ArgumentException ex)
    {
        log.LogError(ex, "Could not create a launch token.");
        return Page(ctx, "reports", "Reports", _ => Task.FromResult(
            $"<p class=\"error\">Demo configuration error: {H(ex.Message)}</p>"));
    }
}));

app.Run();

record Person(string Key, string Name, string Email, LaunchRole Role, int? ProgramId, string RoleText, string ScopeText)
{
    public string First => Name.Split(' ')[0];
}
