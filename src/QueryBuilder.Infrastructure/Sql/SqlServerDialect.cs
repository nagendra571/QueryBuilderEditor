using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Sql;

public sealed class SqlServerDialect : ISqlDialect
{
    public DataSourceProvider Provider => DataSourceProvider.SqlServer;

    public string Quote(string identifier) => SqlIdentifier.Quote(identifier);

    public string BooleanTest(string expression, bool value) => $"{expression} = {(value ? 1 : 0)}";

    public string Comparable(string expression, ColumnDataType dataType) => expression;

    public string Like(string expression, ColumnDataType dataType, string parameterName, bool negate) =>
        $"{expression} {(negate ? "NOT LIKE" : "LIKE")} {parameterName}";

    public void ValidateAlias(string alias)
    {
    }

    public string ApplyRowLimit(string sql, int rowLimit)
    {
        var selectIndex = sql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
        return sql.Insert(selectIndex + "SELECT".Length, $" TOP ({rowLimit})");
    }
}
