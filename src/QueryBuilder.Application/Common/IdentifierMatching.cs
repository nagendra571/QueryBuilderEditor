using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Common;

/// <summary>How a provider compares schema, object and column names. Postgres folds case only when
/// unquoted, and this app always quotes, so its names are case-sensitive.</summary>
public static class IdentifierMatching
{
    public static StringComparison Comparison(DataSourceProvider provider) =>
        provider == DataSourceProvider.PostgreSql ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static StringComparer Comparer(DataSourceProvider provider) =>
        provider == DataSourceProvider.PostgreSql ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
