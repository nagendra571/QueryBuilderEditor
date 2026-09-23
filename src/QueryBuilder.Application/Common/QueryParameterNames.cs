using System.Text.RegularExpressions;

namespace QueryBuilder.Application.Common;

/// <summary>
/// Runtime parameter names are emitted into SQL text as <c>@name</c> (they can't be bound — they
/// ARE the binding), and both the name and its declaration arrive from the browser. Without this
/// check a crafted name like <c>x) OR (1=1</c> would be SQL injection, including a way out of the
/// row-level data-scope predicate. The <c>__</c> prefix is reserved for QueryBuilder's own
/// generated parameters (<c>@__scope0</c>, ...).
/// </summary>
public static partial class QueryParameterNames
{
    public const string ReservedPrefix = "__";

    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name) &&
        !name.StartsWith(ReservedPrefix, StringComparison.Ordinal) &&
        ValidName().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9_]{1,128}$")]
    private static partial Regex ValidName();
}
