using System.Text.RegularExpressions;

namespace QueryBuilder.Editor;

/// <summary>
/// Row-level data scoping: limits which rows a user sees, e.g. a Program-admin only sees rows whose
/// ProgramId is theirs. Off unless a resolver is set.
/// <code>
/// options.DataScope.Keys = ["ProgramId"];
/// options.DataScope.Resolver = ctx => ctx.User.IsInRole("Admin")
///     ? DataScope.Unrestricted
///     : DataScope.For("ProgramId", GetProgramIds(ctx));
/// </code>
/// Admins then decide, per view in the admin UI, which column each key filters on — or mark the
/// view "not scoped". Views without a decision are hidden from scoped users (fail closed).
/// </summary>
public sealed partial class DataScopeOptions
{
    /// <summary>The scope keys admins can map view columns to (e.g. "ProgramId", "ModuleId").</summary>
    public IList<string> Keys { get; set; } = [];

    /// <summary>Resolves the current user's scope, once per request. Return
    /// <see cref="DataScope.Unrestricted"/> for users who see everything. Returning null or throwing
    /// is treated as denied (only "not scoped" views are visible) — never as unrestricted, so a bug
    /// here can't leak data.</summary>
    public Func<HttpContext, DataScope?>? Resolver { get; set; }

    /// <summary>Async alternative to <see cref="Resolver"/> for scopes that need a DB/API lookup. Set one or the other.</summary>
    public Func<HttpContext, ValueTask<DataScope?>>? ResolverAsync { get; set; }

    internal bool IsEnabled => Resolver is not null || ResolverAsync is not null;

    internal void Validate()
    {
        if (Resolver is not null && ResolverAsync is not null)
        {
            throw new InvalidOperationException(
                $"Set only one of {nameof(QueryBuilderEditorOptions)}.DataScope.{nameof(Resolver)} and .{nameof(ResolverAsync)}.");
        }
        if (IsEnabled && Keys.Count == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(QueryBuilderEditorOptions)}.DataScope.{nameof(Keys)} must list at least one scope key when a resolver is set.");
        }
        if (!IsEnabled && Keys.Count > 0)
        {
            throw new InvalidOperationException(
                $"{nameof(QueryBuilderEditorOptions)}.DataScope.{nameof(Keys)} is set but no {nameof(Resolver)}/{nameof(ResolverAsync)} is — data scoping would silently be off.");
        }
        foreach (var key in Keys)
        {
            if (key is null || !ValidKey().IsMatch(key))
            {
                throw new InvalidOperationException(
                    $"Data-scope key '{key}' is invalid — use letters, digits and underscores only (max 128 chars).");
            }
        }
        var duplicate = Keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Data-scope key '{duplicate.Key}' is declared more than once.");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex ValidKey();
}
