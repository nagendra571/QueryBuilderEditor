namespace QueryBuilder.Infrastructure.Sql;

public sealed class SqlServerQuerySqlBuilder() : SqlQueryBuilder(new SqlServerDialect());
