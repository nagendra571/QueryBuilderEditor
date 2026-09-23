using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Tests;

public class SqlBuilderScopeTests
{
    private readonly SqlServerQuerySqlBuilder _builder = new();

    private static ScopePredicate Program(params string[] values) =>
        new("t0", "ProgramId", ColumnDataType.Number, values);

    [Fact]
    public void No_scope_and_no_filters_emits_no_where()
    {
        var generated = _builder.Build(TestData.Definition(), []);

        Assert.DoesNotContain("WHERE", generated.Sql);
    }

    [Fact]
    public void No_scope_leaves_user_filters_unwrapped()
    {
        var definition = TestData.Definition();
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", FilterOperator.Equals, "EU"));

        var generated = _builder.Build(definition, []);

        Assert.Contains("\nWHERE [t0].[RegionCode] = @p0", generated.Sql);
    }

    [Fact]
    public void Scope_alone_emits_parameterized_in_list()
    {
        var generated = _builder.Build(TestData.Definition(), [Program("10", "12")]);

        Assert.Contains("\nWHERE ([t0].[ProgramId] IN (@__scope0, @__scope1))", generated.Sql);
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

        var generated = _builder.Build(definition, [Program("10")]);

        Assert.Contains("\nWHERE ([t0].[ProgramId] IN (@__scope0)) AND ([t0].[RegionCode] = @p0 OR [t0].[RegionCode] = @p1)", generated.Sql);
    }

    [Fact]
    public void Multiple_predicates_are_ANDed()
    {
        var generated = _builder.Build(TestData.Definition(),
            [Program("10"), new ScopePredicate("t0", "RegionCode", ColumnDataType.Text, ["EU"])]);

        Assert.Contains("WHERE ([t0].[ProgramId] IN (@__scope0)) AND ([t0].[RegionCode] IN (@__scope1))", generated.Sql);
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

        var where = sql.IndexOf("WHERE ([t0].[ProgramId] IN (@__scope0))", StringComparison.Ordinal);
        var groupBy = sql.IndexOf("GROUP BY [t0].[RegionCode]", StringComparison.Ordinal);
        var having = sql.IndexOf("HAVING SUM([t0].[Amount]) > @p0", StringComparison.Ordinal);
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

        Assert.Contains("([t0].[ProgramId] IN (@__scope0)) AND ([t1].[ProgramId] IN (@__scope1))", sql);
    }

    [Fact]
    public void Predicate_with_no_values_matches_no_rows()
    {
        var sql = _builder.Build(TestData.Definition(), [Program()]).Sql;

        Assert.Contains("WHERE (1 = 0)", sql);
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
}
