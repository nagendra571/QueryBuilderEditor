using System.Text;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Sql;

public sealed class PostgresDialect : ISqlDialect
{
    // Postgres truncates longer identifiers silently, which would merge distinct column aliases.
    private const int MaxAliasBytes = 63;

    public DataSourceProvider Provider => DataSourceProvider.PostgreSql;

    public string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    public string BooleanTest(string expression, bool value) => $"{expression} IS {(value ? "TRUE" : "FALSE")}";

    // Npgsql sends string parameters as untyped text; casting the column keeps varchar/char/enum/citext
    // columns comparable with it instead of failing on an operator mismatch.
    public string Comparable(string expression, ColumnDataType dataType) =>
        dataType is ColumnDataType.Text or ColumnDataType.Unknown ? $"{expression}::text" : expression;

    public string Like(string expression, ColumnDataType dataType, string parameterName, bool negate)
    {
        // Comparable has already cast Text and Unknown; casting again would emit "::text::text".
        var text = dataType is ColumnDataType.Text or ColumnDataType.Unknown ? expression : $"{expression}::text";
        // ESCAPE '' because Postgres otherwise treats backslash as an escape, which SQL Server's LIKE does not.
        return $"{text} COLLATE \"default\" {(negate ? "NOT ILIKE" : "ILIKE")} {parameterName} ESCAPE ''";
    }

    public void ValidateAlias(string alias)
    {
        if (Encoding.UTF8.GetByteCount(alias) > MaxAliasBytes)
        {
            throw new CatalogValidationException(
                $"The column name '{alias}' is longer than the {MaxAliasBytes} bytes PostgreSQL allows. Give the column a shorter alias.");
        }
    }

    public string ApplyRowLimit(string sql, int rowLimit) => $"{sql}\nLIMIT {rowLimit}";
}
