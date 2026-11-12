namespace QueryBuilder.Infrastructure.Sql;

public sealed class PostgresQuerySqlBuilder() : SqlQueryBuilder(new PostgresDialect());
