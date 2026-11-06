using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Sql;

/// <summary>
/// The parts of generated SQL that differ between database engines. Everything else stays in
/// <see cref="SqlQueryBuilder"/> so each engine's output is shaped by the same code.
/// </summary>
public interface ISqlDialect
{
    DataSourceProvider Provider { get; }
    string Quote(string identifier);
    string BooleanTest(string expression, bool value);

    /// <summary>Wraps a column or aggregate so it compares correctly with a bound parameter of <paramref name="dataType"/>.</summary>
    string Comparable(string expression, ColumnDataType dataType);

    /// <remarks>
    /// <paramref name="expression"/> has already been through <see cref="Comparable"/>, so a dialect must not
    /// apply the same wrapping again.
    /// </remarks>
    string Like(string expression, ColumnDataType dataType, string parameterName, bool negate);

    /// <summary>Throws when the engine cannot represent <paramref name="alias"/> as a column alias.</summary>
    void ValidateAlias(string alias);

    string ApplyRowLimit(string sql, int rowLimit);
}
