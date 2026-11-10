using System.Text.RegularExpressions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.DataScoping;

/// <summary>The scope keys that apply to one data source: those the host declares in code plus the ones an admin added to it.</summary>
public static partial class DataScopeKeys
{
    public const int MaxPerDataSource = 20;

    /// <summary>Host keys first (their casing wins), then the data source's own, without repeats ignoring case.</summary>
    public static IReadOnlyList<string> Effective(IEnumerable<string> hostKeys, DataSource dataSource) =>
        Effective(hostKeys, dataSource.ScopeKeys);

    public static IReadOnlyList<string> Effective(IEnumerable<string> hostKeys, IEnumerable<string> dataSourceKeys)
    {
        var result = new List<string>();
        foreach (var key in hostKeys.Concat(dataSourceKeys))
        {
            if (!result.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(key);
            }
        }
        return result;
    }

    public static bool IsValidName(string? key) => key is not null && ValidKey().IsMatch(key);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex ValidKey();
}
