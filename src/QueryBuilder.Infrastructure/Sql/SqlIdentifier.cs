namespace QueryBuilder.Infrastructure.Sql;

/// <summary>
/// Bracket-quotes SQL Server identifiers. Callers must only pass identifiers that have already
/// been validated against the live data source catalog (see SqlServerDataCatalogService.ValidateAsync) —
/// this helper only prevents bracket-escaping issues, it is not itself an injection defense.
/// </summary>
public static class SqlIdentifier
{
    public static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    public static string QualifiedColumn(string alias, string column) => $"{Quote(alias)}.{Quote(column)}";
}
