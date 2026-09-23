using System.Globalization;

namespace QueryBuilder.Editor;

/// <summary>
/// What rows the current user may see, returned by <see cref="DataScopeOptions.Resolver"/>. Either
/// <see cref="Unrestricted"/> (sees every row of every visible object) or a set of scope-key values,
/// e.g. <c>DataScope.For("ProgramId", 10, 12).And("RegionId", "EU")</c>. Immutable — <see cref="And"/>
/// returns a new instance.
/// </summary>
public sealed class DataScope
{
    private readonly Dictionary<string, IReadOnlyList<string>> _values;

    private DataScope(bool isUnrestricted, Dictionary<string, IReadOnlyList<string>> values)
    {
        IsUnrestricted = isUnrestricted;
        _values = values;
    }

    public static DataScope Unrestricted { get; } = new(true, new(StringComparer.OrdinalIgnoreCase));

    public bool IsUnrestricted { get; }

    /// <summary>Scope key → allowed values, formatted invariantly (so <c>10</c> and <c>"10"</c> are the same).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Values => _values;

    /// <summary>Values may be passed individually (<c>For("ProgramId", 10, 12)</c>) or as a
    /// collection (<c>For("ProgramId", programIds)</c>) — collections are flattened.</summary>
    public static DataScope For(string key, params object?[] values) =>
        new DataScope(false, new(StringComparer.OrdinalIgnoreCase)).And(key, values);

    public DataScope And(string key, params object?[] values)
    {
        if (IsUnrestricted)
        {
            throw new InvalidOperationException("An unrestricted DataScope can't be narrowed with And(...).");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(values);

        var copy = new Dictionary<string, IReadOnlyList<string>>(_values, StringComparer.OrdinalIgnoreCase);
        var existing = copy.TryGetValue(key, out var current) ? current : [];
        copy[key] = existing
            .Concat(Flatten(values).Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)!))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new DataScope(false, copy);
    }

    // A generic IEnumerable<T> overload would also bind a single string (IEnumerable<char>) and
    // split "14" into "1" and "4" — so collections are flattened here instead, strings excluded.
    private static IEnumerable<object> Flatten(IEnumerable<object?> values)
    {
        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }
            if (value is System.Collections.IEnumerable nested and not string)
            {
                foreach (var item in Flatten(nested.Cast<object?>()))
                {
                    yield return item;
                }
                continue;
            }
            yield return value;
        }
    }
}
