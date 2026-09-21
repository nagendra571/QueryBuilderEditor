using QueryBuilder.Editor.Authorization;

namespace QueryBuilder.Editor;

/// <summary>
/// Configures QueryBuilder for the host application. Set via:
/// <code>
/// builder.Services.AddQueryBuilderEditor(options =>
/// {
///     options.ConnectionString = builder.Configuration.GetConnectionString("QueryBuilderDb")!;
/// });
/// </code>
/// Mirrors TemplateBuilder.Editor's options shape (ConnectionString, ActorResolver, Authorization,
/// ApplyMigrations) so a team already using that package recognizes this immediately.
/// </summary>
public sealed class QueryBuilderEditorOptions
{
    /// <summary>
    /// Connection string for QueryBuilder's own metadata store (saved queries, registered data
    /// sources, audit log) — a plain string the host resolves however it likes, e.g. from its own
    /// <c>IConfiguration</c>. This is separate from the connection strings of the business data
    /// sources users build queries against, which are registered independently.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Resolves the acting user's identity string, used to stamp CreatedBy/OwnerId and the audit
    /// log's Actor column. Runs once per request. A null or blank result falls through to
    /// <c>HttpContext.User.Identity.Name</c>, then "anonymous". Exceptions propagate — they are not
    /// swallowed, so a misbehaving resolver fails loudly rather than silently logging as anonymous.
    /// </summary>
    public Func<HttpContext, string?>? ActorResolver { get; set; }

    /// <summary>
    /// When true (default), QueryBuilder applies its own EF Core migrations automatically on
    /// startup via a hosted service — no manual migration steps required.
    /// <para>
    /// Set to false when your database is DBA-managed and your app's login has no DDL rights
    /// (no CREATE TABLE/ALTER). In that case the migration hosted service is never registered —
    /// the app login only needs DML (SELECT/INSERT/UPDATE/DELETE) — and instead have your DBA run
    /// the idempotent script shipped at <c>scripts/QueryBuilder.schema.&lt;version&gt;.sql</c> in
    /// the package (source: <c>QueryBuilder.Editor/Scripts/</c>). It's safe to run against a fresh
    /// database or re-run against an already-migrated one: it checks <c>__EFMigrationsHistory</c>
    /// and applies only what's missing, so the same script covers both first provisioning and
    /// every later upgrade — just keep the latest version's file, it supersedes older ones.
    /// </para>
    /// </summary>
    public bool ApplyMigrations { get; set; } = true;

    /// <summary>Controls who can reach QueryBuilder's routes. Defaults to <see cref="QueryBuilderAuthorizationMode.Anonymous"/>.</summary>
    public QueryBuilderAuthorizationOptions Authorization { get; } = new();
}
