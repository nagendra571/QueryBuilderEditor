using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.DataScoping;

public sealed record ScopeColumnFilter(string ColumnName, IReadOnlyList<string> Values);

public sealed record ObjectScopeAccess(bool IsVisible, IReadOnlyList<ScopeColumnFilter> Filters)
{
    public static ObjectScopeAccess Hidden { get; } = new(false, []);
    public static ObjectScopeAccess Unfiltered { get; } = new(true, []);
}

/// <summary>
/// Pure decision logic for row-level data scoping — no I/O, so every fail-closed branch is unit
/// testable. Anything ambiguous resolves to hidden, never to unfiltered.
/// </summary>
public static class DataScopeEvaluator
{
    public static ObjectScopeAccess Evaluate(
        string objectKey,
        IReadOnlyList<DataScopeRule> dataSourceRules,
        ResolvedDataScope scope,
        IReadOnlyCollection<string> declaredKeys)
    {
        if (scope.IsUnrestricted)
        {
            return ObjectScopeAccess.Unfiltered;
        }

        var objectRules = dataSourceRules
            .Where(r => string.Equals(r.ObjectName, objectKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (objectRules.Count == 0)
        {
            return ObjectScopeAccess.Hidden;
        }

        // Key rules win over a "not scoped" marker if both somehow exist (the admin command rejects
        // that mix, but a hand-edited table could still contain it) — filtering is the safe reading.
        var keyRules = objectRules.Where(r => r.ScopeKey is not null).ToList();
        if (keyRules.Count == 0)
        {
            return ObjectScopeAccess.Unfiltered;
        }

        var filters = new List<ScopeColumnFilter>();
        foreach (var rule in keyRules)
        {
            if (string.IsNullOrWhiteSpace(rule.ColumnName) ||
                !declaredKeys.Contains(rule.ScopeKey!, StringComparer.OrdinalIgnoreCase))
            {
                return ObjectScopeAccess.Hidden;
            }

            var values = scope.ValuesFor(rule.ScopeKey!);
            if (values.Count == 0)
            {
                return ObjectScopeAccess.Hidden;
            }

            filters.Add(new ScopeColumnFilter(rule.ColumnName, values));
        }

        return new ObjectScopeAccess(true, filters);
    }
}
