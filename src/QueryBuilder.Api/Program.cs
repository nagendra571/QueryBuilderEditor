using System.Text.Json;
using System.Text.Json.Serialization;
using QueryBuilder.Api.Seed;
using QueryBuilder.Editor;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// This one call replaces AddApplication + AddInfrastructure + exception-handler wiring — everything
// a consumer of the QueryBuilder.Editor package needs, matching TemplateBuilder.Editor's shape.
builder.Services.AddQueryBuilderEditor(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("AppDb")!;
    // Host apps embedding QueryBuilder supply their own actor identity here, e.g.:
    //   options.ActorResolver = ctx => ctx.User?.FindFirst("sub")?.Value;
    // Left unset, the default chain is: HttpContext.User.Identity.Name, then "anonymous".
    // options.ApplyMigrations = false; // set this if your DB is DBA-managed with no DDL rights.
    // options.Authorization.Mode = QueryBuilderAuthorizationMode.Role;
    // options.Authorization.RoleNames = ["Admin"];
    // Left unset, Authorization.Mode defaults to Anonymous — no change required.
});

// Demo-only: this sample app seeds a "Sales Sample" data source so there's something to browse
// immediately. A real integration would not register this. Registered *after*
// AddQueryBuilderEditor so it runs after QueryBuilder's own migration hosted service.
builder.Services.AddSingleton<DemoSourceSeeder>();
builder.Services.AddHostedService<DemoDataSeederHostedService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string ClientCorsPolicy = "ClientApp";
builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCorsPolicy, policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(ClientCorsPolicy);

// Serves the built React app (embedded in QueryBuilder.Editor) plus a SPA fallback route, so this
// single process is the whole app — no separate frontend server needed. The Vite dev server on
// :5173 (with the CORS policy above) is still there for fast local frontend iteration; this is
// what a real deployment, or `dotnet run` without `npm run dev`, actually serves.
app.UseQueryBuilderEditorUI();

app.MapQueryBuilderEditor();

app.Run();
