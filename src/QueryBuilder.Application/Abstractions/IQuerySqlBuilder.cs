using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Abstractions;

/// <summary>
/// Turns a validated <see cref="QueryDefinition"/> into dialect-correct, injection-safe SQL text.
/// Identifiers (schemas/tables/columns/aliases) come only from the already-validated definition —
/// never from raw user text — and every literal filter value is emitted as a bound parameter.
/// </summary>
public interface IQuerySqlBuilder
{
    DataSourceProvider Provider { get; }

    /// <summary><paramref name="scope"/> is required (not optional) on purpose: every caller must
    /// get it from <see cref="IDataScopeGuard.AuthorizeAsync"/>, so a new SQL path can't silently
    /// skip row-level data scoping.</summary>
    GeneratedQuery Build(QueryDefinition definition, IReadOnlyList<ScopePredicate> scope);
}

public sealed record GeneratedQuery(string Sql, IReadOnlyList<GeneratedQueryParameter> Parameters);

public sealed record GeneratedQueryParameter(
    string Name,
    ColumnDataType DataType,
    string? LiteralValue,
    bool IsRuntimeParameter,
    string? RuntimeParameterName);
