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

    /// <summary>Controls who can reach QueryBuilder's admin routes (under <c>/api/admin</c>) —
    /// catalog policy management today, more admin features later. Applied IN ADDITION to
    /// <see cref="Authorization"/>: a request must satisfy both. Defaults to
    /// <see cref="QueryBuilderAuthorizationMode.Anonymous"/>, like <see cref="Authorization"/> —
    /// set this before going to production, since the admin area lets a caller see a data
    /// source's full unfiltered schema and change what business users can query.</summary>
    public QueryBuilderAuthorizationOptions AdminAuthorization { get; } = new();

    /// <summary>Row-level data scoping (e.g. a Program-admin only sees their ProgramId's rows). Off
    /// unless a resolver is set — see <see cref="DataScopeOptions"/>.</summary>
    public DataScopeOptions DataScope { get; } = new();

    /// <summary>Package-wide default for the most records a query may return (1 to 100,000). Each
    /// data source can override it in the admin UI. Past the limit the results grid shows only the
    /// first this-many rows with a "narrow it down" warning, and exports are refused. Left null (and
    /// with no per-data-source value) there is no limit: the grid is capped at 1,000 rows and exports
    /// at 100,000, both silently, as before this option existed.</summary>
    public int? DefaultMaxRecords { get; set; }

    /// <summary>Lets admins add and edit data sources in the UI, with the connection details stored encrypted.
    /// Off unless enabled — see <see cref="DataSourceManagementOptions"/>.</summary>
    public DataSourceManagementOptions DataSourceManagement { get; } = new();
}

/// <summary>
/// Managed data sources: admins type host, port, database, user and password into the admin UI, and QueryBuilder
/// stores them encrypted (AES-256-GCM) with <see cref="EncryptionKey"/>. Data sources added by raw SQL with a
/// <c>ConnectionStringName</c> keep working either way.
/// </summary>
public sealed class DataSourceManagementOptions
{
    /// <summary>Turns on the add, edit, test and turn-off endpoints and screens. Needs <see cref="EncryptionKey"/>,
    /// and an <c>AdminAuthorization</c> that isn't Anonymous unless <see cref="AllowAnonymousAdmins"/> is set.
    /// An admin can make this server connect to any host they type, so keep admin access narrow.</summary>
    public bool Enabled { get; set; }

    /// <summary>Base64 of exactly 32 bytes. Generate one with <c>openssl rand -base64 32</c>. Keep it out of source
    /// control; losing it means re-entering each managed source's details. Setting it without <see cref="Enabled"/>
    /// is enough to keep querying managed sources that already exist.</summary>
    public string? EncryptionKey { get; set; }

    /// <summary>Old keys that can still be read, newest first or in any order: each is base64 of 32 bytes. Saved
    /// details are always written with <see cref="EncryptionKey"/>. To change the key, put the new one in
    /// <see cref="EncryptionKey"/>, move the old one here, restart, then use "Re-encrypt with the current key" on the
    /// admin page, and only afterwards remove the old key. Needs <see cref="EncryptionKey"/>.</summary>
    public IList<string> PreviousEncryptionKeys { get; set; } = [];

    /// <summary>Allows <see cref="Enabled"/> while <c>AdminAuthorization</c> is Anonymous, meaning anyone who can
    /// reach the site could point it at any server. Meant for local development only.</summary>
    public bool AllowAnonymousAdmins { get; set; }
}
