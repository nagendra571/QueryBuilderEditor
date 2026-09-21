namespace QueryBuilder.Editor.Authorization;

/// <summary>
/// Configures who can reach QueryBuilder's routes. Applied to every mapped endpoint automatically
/// by <c>MapQueryBuilderEditor()</c> — no per-endpoint changes needed. For anything other than
/// <see cref="QueryBuilderAuthorizationMode.Anonymous"/>, your pipeline must include, in order:
/// <c>app.UseAuthentication(); app.UseAuthorization();</c>
/// </summary>
public sealed class QueryBuilderAuthorizationOptions
{
    public QueryBuilderAuthorizationMode Mode { get; set; } = QueryBuilderAuthorizationMode.Anonymous;

    /// <summary>Required when <see cref="Mode"/> is <see cref="QueryBuilderAuthorizationMode.Role"/>. A user in any one of these roles is granted access.</summary>
    public string[] RoleNames { get; set; } = [];

    /// <summary>
    /// Escape hatch for advanced scenarios (claims-based, multi-tenant, composite rules): the name
    /// of an ASP.NET Core authorization policy you registered yourself via
    /// <c>services.AddAuthorization(o => o.AddPolicy(...))</c>. When set, this takes precedence over
    /// <see cref="Mode"/> entirely.
    /// </summary>
    public string? PolicyName { get; set; }
}
