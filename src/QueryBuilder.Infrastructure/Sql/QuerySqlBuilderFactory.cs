using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Sql;

public sealed class QuerySqlBuilderFactory(IEnumerable<IQuerySqlBuilder> builders) : IQuerySqlBuilderFactory
{
    public IQuerySqlBuilder GetBuilder(DataSourceProvider provider) =>
        builders.FirstOrDefault(b => b.Provider == provider)
        ?? throw new NotSupportedException($"No SQL builder is registered for provider '{provider}'.");
}
