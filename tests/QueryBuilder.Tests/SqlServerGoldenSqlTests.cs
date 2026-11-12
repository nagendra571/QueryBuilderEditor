using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Tests;

// Pins the exact T-SQL so extracting the dialect cannot change what SQL Server receives.
public class SqlServerGoldenSqlTests
{
    private readonly SqlServerQuerySqlBuilder _builder = new();

    private const string Head = "SELECT [t0].[OrderId] AS [OrderId]\nFROM [sales].[vw_Orders] AS [t0]";

    [Fact]
    public void Plain_select()
    {
        Assert.Equal(Head, _builder.Build(TestData.Definition(), []).Sql);
    }

    [Theory]
    [InlineData(FilterOperator.Equals, "x", "[t0].[RegionCode] = @p0")]
    [InlineData(FilterOperator.NotEquals, "x", "[t0].[RegionCode] <> @p0")]
    [InlineData(FilterOperator.GreaterThan, "x", "[t0].[RegionCode] > @p0")]
    [InlineData(FilterOperator.GreaterOrEqual, "x", "[t0].[RegionCode] >= @p0")]
    [InlineData(FilterOperator.LessThan, "x", "[t0].[RegionCode] < @p0")]
    [InlineData(FilterOperator.LessOrEqual, "x", "[t0].[RegionCode] <= @p0")]
    [InlineData(FilterOperator.Contains, "x", "[t0].[RegionCode] LIKE @p0")]
    [InlineData(FilterOperator.NotContains, "x", "[t0].[RegionCode] NOT LIKE @p0")]
    [InlineData(FilterOperator.StartsWith, "x", "[t0].[RegionCode] LIKE @p0")]
    [InlineData(FilterOperator.EndsWith, "x", "[t0].[RegionCode] LIKE @p0")]
    [InlineData(FilterOperator.In, "[\"a\",\"b\"]", "[t0].[RegionCode] IN (@p0, @p1)")]
    [InlineData(FilterOperator.NotIn, "[\"a\",\"b\"]", "[t0].[RegionCode] NOT IN (@p0, @p1)")]
    [InlineData(FilterOperator.Between, "[\"a\",\"b\"]", "[t0].[RegionCode] BETWEEN @p0 AND @p1")]
    [InlineData(FilterOperator.IsNull, null, "[t0].[RegionCode] IS NULL")]
    [InlineData(FilterOperator.IsNotNull, null, "[t0].[RegionCode] IS NOT NULL")]
    [InlineData(FilterOperator.IsTrue, null, "[t0].[RegionCode] = 1")]
    [InlineData(FilterOperator.IsFalse, null, "[t0].[RegionCode] = 0")]
    public void Every_filter_operator(FilterOperator op, string? value, string expectedWhere)
    {
        var definition = TestData.Definition();
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", op, value));

        Assert.Equal($"{Head}\nWHERE {expectedWhere}", _builder.Build(definition, []).Sql);
    }

    [Fact]
    public void Empty_in_list_is_constant_false()
    {
        var definition = TestData.Definition();
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", FilterOperator.In, "[]"));

        Assert.Equal($"{Head}\nWHERE 1 = 0", _builder.Build(definition, []).Sql);
    }

    [Fact]
    public void Aggregates_group_by_the_plain_columns_automatically()
    {
        var definition = TestData.Definition();
        definition.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Aggregate = AggregateFunction.Sum, OrderIndex = 1 });
        definition.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Alias = "Orders", Aggregate = AggregateFunction.CountDistinct, OrderIndex = 2 });

        Assert.Equal(
            "SELECT [t0].[OrderId] AS [OrderId],\n       SUM([t0].[Amount]) AS [Amount],\n       COUNT(DISTINCT [t0].[Amount]) AS [Orders]" +
            "\nFROM [sales].[vw_Orders] AS [t0]\nGROUP BY [t0].[OrderId]",
            _builder.Build(definition, []).Sql);
    }

    [Fact]
    public void Having_filters_on_the_aggregate()
    {
        var definition = TestData.Definition();
        definition.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Aggregate = AggregateFunction.Avg, OrderIndex = 1 });
        definition.Having = new FilterGroup();
        definition.Having.Conditions.Add(new FilterCondition
        {
            TableAlias = "t0", ColumnName = "Amount", Aggregate = AggregateFunction.Avg,
            Operator = FilterOperator.GreaterThan, Value = "5", DataType = ColumnDataType.Number
        });

        Assert.Equal(
            "SELECT [t0].[OrderId] AS [OrderId],\n       AVG([t0].[Amount]) AS [Amount]" +
            "\nFROM [sales].[vw_Orders] AS [t0]\nGROUP BY [t0].[OrderId]\nHAVING AVG([t0].[Amount]) > @p0",
            _builder.Build(definition, []).Sql);
    }

    [Fact]
    public void Sort_on_aggregated_and_plain_columns()
    {
        var definition = TestData.Definition();
        definition.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Aggregate = AggregateFunction.Max, OrderIndex = 1 });
        definition.Sorts.Add(new QuerySort { TableAlias = "t0", ColumnName = "Amount", Direction = SortDirection.Desc, OrderIndex = 0 });
        definition.Sorts.Add(new QuerySort { TableAlias = "t0", ColumnName = "OrderId", Direction = SortDirection.Asc, OrderIndex = 1 });

        Assert.Equal(
            "SELECT [t0].[OrderId] AS [OrderId],\n       MAX([t0].[Amount]) AS [Amount]" +
            "\nFROM [sales].[vw_Orders] AS [t0]\nGROUP BY [t0].[OrderId]" +
            "\nORDER BY MAX([t0].[Amount]) DESC, [t0].[OrderId] ASC",
            _builder.Build(definition, []).Sql);
    }

    [Fact]
    public void Scope_and_an_or_group_of_user_filters()
    {
        var definition = TestData.Definition();
        definition.Filters.Operator = FilterLogicalOperator.Or;
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", FilterOperator.Equals, "EU"));
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", FilterOperator.Equals, "US"));
        var nested = new FilterGroup();
        nested.Conditions.Add(TestData.Condition("Amount", FilterOperator.IsNull, null));
        definition.Filters.Groups.Add(nested);

        var scope = new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, ["10", "12"]);

        Assert.Equal(
            $"{Head}\nWHERE ([t0].[ProgramId] IN (@__scope0, @__scope1)) AND " +
            "([t0].[RegionCode] = @p0 OR [t0].[RegionCode] = @p1 OR ([t0].[Amount] IS NULL))",
            _builder.Build(definition, [scope]).Sql);
    }

    [Fact]
    public void Joins_of_each_type()
    {
        var definition = TestData.Definition();
        definition.Joins.Add(new QueryJoin
        {
            SchemaName = "sales", ObjectName = "vw_Programs", Alias = "t1", JoinType = JoinType.Left,
            Conditions = [new JoinCondition { LeftAlias = "t0", LeftColumn = "ProgramId", RightAlias = "t1", RightColumn = "ProgramId" }]
        });
        definition.Joins.Add(new QueryJoin
        {
            SchemaName = "sales", ObjectName = "vw_Secret", Alias = "t2", JoinType = JoinType.Full,
            Conditions =
            [
                new JoinCondition { LeftAlias = "t0", LeftColumn = "OrderId", RightAlias = "t2", RightColumn = "Id" },
                new JoinCondition { LeftAlias = "t1", LeftColumn = "ProgramId", RightAlias = "t2", RightColumn = "Id" }
            ]
        });
        definition.Joins.Add(new QueryJoin
        {
            SchemaName = "sales", ObjectName = "vw_Programs", Alias = "t3", JoinType = JoinType.Right,
            Conditions = [new JoinCondition { LeftAlias = "t0", LeftColumn = "ProgramId", RightAlias = "t3", RightColumn = "ProgramId" }]
        });
        definition.Joins.Add(new QueryJoin
        {
            SchemaName = "sales", ObjectName = "vw_Programs", Alias = "t4", JoinType = JoinType.Inner,
            Conditions = [new JoinCondition { LeftAlias = "t0", LeftColumn = "ProgramId", RightAlias = "t4", RightColumn = "ProgramId" }]
        });

        Assert.Equal(
            $"{Head}" +
            "\nLEFT JOIN [sales].[vw_Programs] AS [t1] ON [t0].[ProgramId] = [t1].[ProgramId]" +
            "\nFULL OUTER JOIN [sales].[vw_Secret] AS [t2] ON [t0].[OrderId] = [t2].[Id] AND [t1].[ProgramId] = [t2].[Id]" +
            "\nRIGHT JOIN [sales].[vw_Programs] AS [t3] ON [t0].[ProgramId] = [t3].[ProgramId]" +
            "\nINNER JOIN [sales].[vw_Programs] AS [t4] ON [t0].[ProgramId] = [t4].[ProgramId]",
            _builder.Build(definition, []).Sql);
    }

    [Fact]
    public void Closing_bracket_in_a_name_is_doubled()
    {
        var definition = new QueryDefinition
        {
            Source = new QuerySource { SchemaName = "s]x", ObjectName = "o]y", Alias = "a]b" },
            Columns = [new QueryColumn { TableAlias = "a]b", ColumnName = "c]d", Alias = "e]f" }]
        };

        Assert.Equal(
            "SELECT [a]]b].[c]]d] AS [e]]f]\nFROM [s]]x].[o]]y] AS [a]]b]",
            _builder.Build(definition, []).Sql);
    }

    [Fact]
    public void Row_limit_goes_after_select()
    {
        Assert.Equal("SELECT TOP (11) a\nFROM b", new SqlServerDialect().ApplyRowLimit("SELECT a\nFROM b", 11));
    }

    [Fact]
    public void Aliases_of_visible_columns_only_are_validated_by_the_dialect()
    {
        var dialect = new RecordingDialect();
        var definition = TestData.Definition();
        definition.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Alias = "Total", OrderIndex = 1 });
        definition.Columns.Add(new QueryColumn { TableAlias = "t0", ColumnName = "RegionCode", IsVisible = false, OrderIndex = 2 });

        new TestBuilder(dialect).Build(definition, []);

        Assert.Equal(["OrderId", "Total"], dialect.Validated);
    }

    [Fact]
    public void A_dialect_rejecting_an_alias_stops_generation()
    {
        var dialect = new RecordingDialect { Reject = "OrderId" };

        Assert.Throws<InvalidOperationException>(() => new TestBuilder(dialect).Build(TestData.Definition(), []));
    }

    [Fact]
    public void Empty_not_in_list_is_constant_true()
    {
        var definition = TestData.Definition();
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", FilterOperator.NotIn, "[]"));

        Assert.Equal($"{Head}\nWHERE 1 = 1", _builder.Build(definition, []).Sql);
    }

    [Fact]
    public void Empty_scope_values_match_no_rows()
    {
        Assert.Equal($"{Head}\nWHERE (1 = 0)", _builder.Build(TestData.Definition(), [new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, [])]).Sql);
    }

    [Theory]
    [InlineData(FilterOperator.Contains, "%x%")]
    [InlineData(FilterOperator.StartsWith, "x%")]
    [InlineData(FilterOperator.EndsWith, "%x")]
    public void Wildcard_operators_bind_the_wrapped_value(FilterOperator op, string literal)
    {
        var definition = TestData.Definition();
        definition.Filters.Conditions.Add(TestData.Condition("RegionCode", op, "x"));

        var generated = _builder.Build(definition, []);

        var parameter = Assert.Single(generated.Parameters);
        Assert.Equal("@p0", parameter.Name);
        Assert.Equal(literal, parameter.LiteralValue);
        Assert.DoesNotContain("x", generated.Sql.Replace("RegionCode", ""));
    }

    [Fact]
    public void Runtime_parameter_is_emitted_by_name()
    {
        var definition = TestData.Definition();
        var condition = TestData.Condition("RegionCode", FilterOperator.Equals, "EU");
        condition.IsParameterized = true;
        condition.ParameterName = "region";
        definition.Filters.Conditions.Add(condition);

        var generated = _builder.Build(definition, []);

        Assert.Equal($"{Head}\nWHERE [t0].[RegionCode] = @region", generated.Sql);
        var parameter = Assert.Single(generated.Parameters);
        Assert.True(parameter.IsRuntimeParameter);
        Assert.Equal("region", parameter.RuntimeParameterName);
    }

    private sealed class TestBuilder(ISqlDialect dialect) : SqlQueryBuilder(dialect);

    private sealed class RecordingDialect : ISqlDialect
    {
        private readonly SqlServerDialect _inner = new();
        public List<string> Validated { get; } = [];
        public string? Reject { get; init; }

        public DataSourceProvider Provider => _inner.Provider;
        public string Quote(string identifier) => _inner.Quote(identifier);
        public string BooleanTest(string expression, bool value) => _inner.BooleanTest(expression, value);
        public string Comparable(string expression, ColumnDataType dataType) => _inner.Comparable(expression, dataType);
        public string Like(string expression, ColumnDataType dataType, string parameterName, bool negate) =>
            _inner.Like(expression, dataType, parameterName, negate);
        public string ApplyRowLimit(string sql, int rowLimit) => _inner.ApplyRowLimit(sql, rowLimit);

        public void ValidateAlias(string alias)
        {
            Validated.Add(alias);
            if (alias == Reject)
            {
                throw new InvalidOperationException(alias);
            }
        }
    }
}
