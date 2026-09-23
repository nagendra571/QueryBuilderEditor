using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Abstractions;

/// <summary>The current request's row-level data scope, as resolved from the host's resolver.</summary>
public sealed record ResolvedDataScope(bool IsUnrestricted, IReadOnlyDictionary<string, IReadOnlyList<string>> Values)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoValues =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public static ResolvedDataScope Unrestricted { get; } = new(true, NoValues);

    /// <summary>Scoped with no values at all — sees only objects an admin marked "not scoped".</summary>
    public static ResolvedDataScope Denied { get; } = new(false, NoValues);

    public IReadOnlyList<string> ValuesFor(string key) =>
        Values.TryGetValue(key, out var values) ? values : [];

    /// <summary>Shape recorded in audit-log details.</summary>
    public object Describe() => IsUnrestricted ? "Unrestricted" : Values;
}

public interface ICurrentDataScope
{
    /// <summary>False when the host configured no scope resolver — everyone is unrestricted.</summary>
    bool IsEnabled { get; }

    /// <summary>The scope keys the host declared (what admins can map columns to).</summary>
    IReadOnlyList<string> DeclaredKeys { get; }

    /// <summary>Resolved once per request and cached.</summary>
    Task<ResolvedDataScope> GetAsync(CancellationToken cancellationToken);
}

/// <summary>A row filter the SQL builder must AND into WHERE: <c>[Alias].[ColumnName] IN (Values)</c>.</summary>
public sealed record ScopePredicate(string Alias, string ColumnName, ColumnDataType DataType, IReadOnlyList<string> Values);

public interface IDataScopeRuleRepository
{
    Task<IReadOnlyList<DataScopeRule>> GetForDataSourceAsync(Guid dataSourceId, CancellationToken cancellationToken);
    Task ReplaceForDataSourceAsync(Guid dataSourceId, IReadOnlyList<DataScopeRule> rules, CancellationToken cancellationToken);
}

/// <summary>
/// The single enforcement point for row-level data scoping. Every path that reads the business
/// catalog or builds business SQL goes through this — never through the client-supplied definition
/// alone, since run/export trust whatever definition the browser sends.
/// </summary>
public interface IDataScopeGuard
{
    Task<DataSourceCatalog> FilterCatalogAsync(DataSourceCatalog catalog, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="Exceptions.ForbiddenException"/> if any object the definition
    /// references is hidden from the current user; otherwise returns the predicates to inject.</summary>
    Task<IReadOnlyList<ScopePredicate>> AuthorizeAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken);
}
