using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Abstractions;

public interface IQuerySqlBuilderFactory
{
    IQuerySqlBuilder GetBuilder(DataSourceProvider provider);
}
