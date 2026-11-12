using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Tests.Postgres;

public class PostgresSqlBuilderTests
{
    private readonly PostgresQuerySqlBuilder _builder = new();

    private static ScopePredicate Program(params string[] values) =>
        new("t0", "ProgramId", ColumnDataType.Number, values);

    private static QueryDefinition WithCondition(FilterOperator op, string? value, string column = "RegionCode", ColumnDataType type = ColumnDataType.Text)
    {
        var definition = TestData.Definition();
        var condition = TestData.Condition(column, op, value);
        condition.DataType = type;
        definition.Filters.Conditions.Add(condition);
        return definition;
    }

    [Fact]
    public void No_scope_and_no_filters_emits_no_where()
    {
        Assert.DoesNotContain("WHERE", _builder.Build(TestData.Definition(), []).Sql);
    }

    [Fact]
    public void No_scope_leaves_user_filters_unwrapped()
    {
        var sql = _builder.Build(WithCondition(FilterOperator.Equals, "EU"), []).Sql;

        Assert.Contains("\nWHERE \"t0\".\"RegionCode\"::text = @p0", sql);
    }

    [Fact]
    public void Scope_alone_emits_parameterized_in_list()
    {
        var generated = _builder.Build(TestData.Definition(), [Program("10", "12")]);

        Assert.Contains("(\"t0\".\"ProgramId\" IN (@__scope0, @__scope1))", generated.Sql);
        Assert.Contains("\nWHERE (\"t0\".\"ProgramId\" IN (@__scope0, @__scope1))", generated.Sql);
        Assert.Collection(generated.Parameters,
            p => { Assert.Equal("@__scope0", p.Name); Assert.Equal("10", p.LiteralValue); Assert.Equal(ColumnDataType.Number, p.DataType); Assert.False(p.IsRuntimeParameter); },
            p => { Assert.Equal("@__scope1", p.Name); Assert.Equal("12", p.LiteralValue); });
        Assert.DoesNotContain("10", generated.Sql);
    }

    [Fact]
    public void Top_level_OR_user_filters_are_parenthesized_so_they_cannot_escape_scope()
    {
        var definition = TestData.Definition();
        definition.Filters.Operator = FilterLogicalOperator.Or;
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", FilterOperator.Equals, "EU"));
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", FilterOperator.Equals, "US"));

        var sql = _builder.Build(definition, [Program("10")]).Sql;

        Assert.Contains("\nWHERE (\"t0\".\"ProgramId\" IN (@__scope0)) AND (\"t0\".\"RegionCode\"::text = @p0 OR \"t0\".\"RegionCode\"::text = @p1)", sql);
    }

    [Fact]
    public void Multiple_predicates_are_ANDed()
    {
        var sql = _builder.Build(TestData.Definition(),
            [Program("10"), new ScopePredicate("t0", "Tenant", ColumnDataType.Text, ["EU"])]).Sql;

        Assert.Contains("WHERE (\"t0\".\"ProgramId\" IN (@__scope0)) AND (\"t0\".\"Tenant\"::text IN (@__scope1))", sql);
    }

    [Fact]
    public void Text_scope_casts_and_never_uses_ILIKE()
    {
        var sql = _builder.Build(TestData.Definition(), [new ScopePredicate("t0", "Tenant", ColumnDataType.Text, ["EU"])]).Sql;

        Assert.Contains("(\"t0\".\"Tenant\"::text IN (@__scope0))", sql);
        Assert.DoesNotContain("ILIKE", sql);
    }

    [Fact]
    public void Scope_stays_in_WHERE_before_auto_GROUP_BY_and_HAVING()
    {
        var definition = TestData.Definition();
        definition.Columns =
        [
            new QueryColumn { TableAlias = "t0", ColumnName = "RegionCode", OrderIndex = 0 },
            new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Aggregate = AggregateFunction.Sum, OrderIndex = 1 }
        ];
        definition.Having = new FilterGroup
        {
            Conditions = [new FilterCondition { TableAlias = "t0", ColumnName = "Amount", Aggregate = AggregateFunction.Sum, Operator = FilterOperator.GreaterThan, Value = "100", DataType = ColumnDataType.Number }]
        };

        var sql = _builder.Build(definition, [Program("10")]).Sql;

        var where = sql.IndexOf("WHERE (\"t0\".\"ProgramId\" IN (@__scope0))", StringComparison.Ordinal);
        var groupBy = sql.IndexOf("GROUP BY \"t0\".\"RegionCode\"", StringComparison.Ordinal);
        var having = sql.IndexOf("HAVING SUM(\"t0\".\"Amount\") > @p0", StringComparison.Ordinal);
        Assert.True(where >= 0 && groupBy > where && having > groupBy, sql);
    }

    [Fact]
    public void Join_alias_gets_its_own_predicate()
    {
        var definition = TestData.Definition();
        definition.Joins.Add(new QueryJoin
        {
            SchemaName = "sales", ObjectName = "vw_Programs", Alias = "t1",
            Conditions = [new JoinCondition { LeftAlias = "t0", LeftColumn = "ProgramId", RightAlias = "t1", RightColumn = "ProgramId" }]
        });

        var sql = _builder.Build(definition, [Program("10"), new ScopePredicate("t1", "ProgramId", ColumnDataType.Number, ["10"])]).Sql;

        Assert.Contains("(\"t0\".\"ProgramId\" IN (@__scope0)) AND (\"t1\".\"ProgramId\" IN (@__scope1))", sql);
    }

    [Fact]
    public void Predicate_with_no_values_matches_no_rows()
    {
        Assert.Contains("WHERE (1 = 0)", _builder.Build(TestData.Definition(), [Program()]).Sql);
    }

    [Fact]
    public void Scope_parameter_names_never_collide_with_user_parameters()
    {
        var definition = TestData.Definition();
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", FilterOperator.In, "[\"EU\",\"US\"]"));

        var generated = _builder.Build(definition, [Program("10", "12")]);

        var names = generated.Parameters.Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("x) OR (1=1")]
    [InlineData("__scope0")]
    [InlineData("a b")]
    public void Builder_rejects_invalid_runtime_parameter_names(string parameterName)
    {
        var definition = TestData.Definition();
        var condition = TestData.Condition("RegionCode", FilterOperator.Equals, "EU");
        condition.IsParameterized = true;
        condition.ParameterName = parameterName;
        definition.Filters.Conditions.Add(condition);

        Assert.Throws<CatalogValidationException>(() => _builder.Build(definition, [Program("10")]));
    }

    [Fact]
    public void Text_contains_uses_ILIKE_with_default_collation()
    {
        var generated = _builder.Build(WithCondition(FilterOperator.Contains, "EU"), []);

        Assert.Contains("\"t0\".\"RegionCode\"::text COLLATE \"default\" ILIKE @p0", generated.Sql);
        Assert.Equal("%EU%", generated.Parameters[0].LiteralValue);
    }

    [Fact]
    public void Text_not_contains_uses_NOT_ILIKE()
    {
        var sql = _builder.Build(WithCondition(FilterOperator.NotContains, "EU"), []).Sql;

        Assert.Contains("\"t0\".\"RegionCode\"::text COLLATE \"default\" NOT ILIKE @p0", sql);
    }

    [Fact]
    public void ILIKE_disables_the_backslash_escape()
    {
        var dialect = new PostgresDialect();

        Assert.Equal("\"t0\".\"RegionCode\"::text COLLATE \"default\" ILIKE @p0 ESCAPE ''",
            dialect.Like("\"t0\".\"RegionCode\"::text", ColumnDataType.Text, "@p0", negate: false));
        Assert.Equal("\"t0\".\"RegionCode\"::text COLLATE \"default\" NOT ILIKE @p0 ESCAPE ''",
            dialect.Like("\"t0\".\"RegionCode\"::text", ColumnDataType.Text, "@p0", negate: true));
    }

    [Fact]
    public void Backslash_in_contains_value_stays_bound_and_literal()
    {
        var generated = _builder.Build(WithCondition(FilterOperator.Contains, "C:\\temp"), []);

        Assert.Equal("%C:\\temp%", generated.Parameters[0].LiteralValue);
        Assert.DoesNotContain("temp", generated.Sql);
        Assert.Contains("ILIKE @p0 ESCAPE ''", generated.Sql);
    }

    [Fact]
    public void Non_text_contains_casts_once()
    {
        var sql = _builder.Build(WithCondition(FilterOperator.Contains, "1", "Amount", ColumnDataType.Number), []).Sql;

        Assert.Contains("\"t0\".\"Amount\"::text COLLATE \"default\" ILIKE @p0", sql);
        Assert.DoesNotContain("::text::text", sql);
    }

    [Fact]
    public void Number_comparison_has_no_cast()
    {
        var sql = _builder.Build(WithCondition(FilterOperator.GreaterThan, "5", "Amount", ColumnDataType.Number), []).Sql;

        Assert.Contains("\"t0\".\"Amount\" > @p0", sql);
        Assert.DoesNotContain("::text", sql);
    }

    [Fact]
    public void Number_scope_has_no_cast()
    {
        var sql = _builder.Build(TestData.Definition(), [Program("10", "12")]).Sql;

        Assert.Contains("(\"t0\".\"ProgramId\" IN (@__scope0, @__scope1))", sql);
        Assert.DoesNotContain("::text", sql);
    }

    [Fact]
    public void Boolean_tests_use_IS_TRUE_and_IS_FALSE()
    {
        Assert.Contains("\"t0\".\"Active\" IS TRUE", _builder.Build(WithCondition(FilterOperator.IsTrue, null, "Active", ColumnDataType.Boolean), []).Sql);
        Assert.Contains("\"t0\".\"Active\" IS FALSE", _builder.Build(WithCondition(FilterOperator.IsFalse, null, "Active", ColumnDataType.Boolean), []).Sql);
    }

    [Fact]
    public void Is_null_has_no_cast()
    {
        var sql = _builder.Build(WithCondition(FilterOperator.IsNull, null), []).Sql;

        Assert.Contains("\"t0\".\"RegionCode\" IS NULL", sql);
    }

    [Fact]
    public void Having_on_text_aggregate_casts_the_aggregate()
    {
        var definition = TestData.Definition();
        definition.Columns =
        [
            new QueryColumn { TableAlias = "t0", ColumnName = "Category", OrderIndex = 0 },
            new QueryColumn { TableAlias = "t0", ColumnName = "RegionCode", Aggregate = AggregateFunction.Min, OrderIndex = 1 }
        ];
        definition.Having = new FilterGroup
        {
            Conditions = [new FilterCondition { TableAlias = "t0", ColumnName = "RegionCode", Aggregate = AggregateFunction.Min, Operator = FilterOperator.Equals, Value = "EU", DataType = ColumnDataType.Text }]
        };

        var sql = _builder.Build(definition, []).Sql;

        Assert.Contains("MIN(\"t0\".\"RegionCode\")::text = @p0", sql);
    }

    [Fact]
    public void Embedded_quote_in_identifier_is_doubled()
    {
        var definition = TestData.Definition();
        definition.Columns = [new QueryColumn { TableAlias = "t0", ColumnName = "a\"b" }];

        Assert.Contains("\"a\"\"b\"", _builder.Build(definition, []).Sql);
    }

    [Fact]
    public void ApplyRowLimit_appends_LIMIT()
    {
        Assert.Equal("SELECT a\nFROM b\nLIMIT 11", new PostgresDialect().ApplyRowLimit("SELECT a\nFROM b", 11));
    }

    private static QueryDefinition WithAlias(string alias)
    {
        var definition = TestData.Definition();
        definition.Columns = [new QueryColumn { TableAlias = "t0", ColumnName = "OrderId", Alias = alias }];
        return definition;
    }

    [Fact]
    public void Alias_of_64_bytes_is_refused()
    {
        var alias = new string('a', 64);

        var ex = Assert.Throws<CatalogValidationException>(() => _builder.Build(WithAlias(alias), []));
        Assert.Contains(alias, ex.Message);
    }

    [Fact]
    public void Alias_of_63_bytes_is_accepted()
    {
        var sql = _builder.Build(WithAlias(new string('a', 63)), []).Sql;

        Assert.Contains(new string('a', 63), sql);
    }

    [Fact]
    public void Multibyte_alias_counts_bytes()
    {
        Assert.Throws<CatalogValidationException>(() => _builder.Build(WithAlias(new string('é', 32)), []));
        _builder.Build(WithAlias(new string('é', 31)), []);
    }

    [Fact]
    public void Empty_not_in_list_is_constant_true()
    {
        Assert.Contains("\nWHERE 1 = 1", _builder.Build(WithCondition(FilterOperator.NotIn, "[]"), []).Sql);
    }

    [Fact]
    public void Empty_in_list_is_constant_false()
    {
        Assert.Contains("\nWHERE 1 = 0", _builder.Build(WithCondition(FilterOperator.In, "[]"), []).Sql);
    }

    [Fact]
    public void Empty_scope_values_match_no_rows()
    {
        Assert.Contains("\nWHERE (1 = 0)", _builder.Build(TestData.Definition(), [new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, [])]).Sql);
    }

    [Theory]
    [InlineData(FilterOperator.Contains, "%x%")]
    [InlineData(FilterOperator.StartsWith, "x%")]
    [InlineData(FilterOperator.EndsWith, "%x")]
    public void Wildcard_operators_bind_the_wrapped_value(FilterOperator op, string literal)
    {
        var generated = _builder.Build(WithCondition(op, "x"), []);

        var parameter = Assert.Single(generated.Parameters);
        Assert.Equal(literal, parameter.LiteralValue);
        Assert.Contains("ILIKE @p0 ESCAPE ''", generated.Sql);
    }

    [Fact]
    public void Runtime_parameter_is_emitted_by_name()
    {
        var definition = WithCondition(FilterOperator.Equals, "EU");
        definition.Filters.Conditions[0].IsParameterized = true;
        definition.Filters.Conditions[0].ParameterName = "region";

        var generated = _builder.Build(definition, []);

        Assert.Contains("\nWHERE \"t0\".\"RegionCode\"::text = @region", generated.Sql);
        Assert.True(Assert.Single(generated.Parameters).IsRuntimeParameter);
    }

    [Fact]
    public void Unknown_data_type_is_compared_as_text()
    {
        var sql = _builder.Build(WithCondition(FilterOperator.Equals, "EU", type: ColumnDataType.Unknown), []).Sql;

        Assert.Contains("\nWHERE \"t0\".\"RegionCode\"::text = @p0", sql);
    }

    [Fact]
    public void Factory_returns_postgres_builder_for_PostgreSql()
    {
        var factory = new QuerySqlBuilderFactory([new SqlServerQuerySqlBuilder(), new PostgresQuerySqlBuilder()]);

        Assert.IsType<PostgresQuerySqlBuilder>(factory.GetBuilder(DataSourceProvider.PostgreSql));
        Assert.IsType<SqlServerQuerySqlBuilder>(factory.GetBuilder(DataSourceProvider.SqlServer));
    }
}
