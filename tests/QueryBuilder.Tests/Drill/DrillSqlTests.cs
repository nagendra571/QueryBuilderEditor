using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Drill;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Tests.Drill;

public class DrillSqlTests
{
    private static readonly ScopePredicate[] Scope = [new("t0", "ProgramId", ColumnDataType.Number, ["10"])];

    public static IEnumerable<object[]> Builders() =>
    [
        [new SqlServerQuerySqlBuilder(), "[t0].[ProgramId] IN (@__scope0)"],
        [new PostgresQuerySqlBuilder(), "\"t0\".\"ProgramId\" IN (@__scope0)"]
    ];

    [Theory]
    [MemberData(nameof(Builders))]
    public void Drill_steps_sit_inside_the_scope_and(IQuerySqlBuilder builder, string scopeSql)
    {
        var drilled = DrillDefinitions.ForRows(VizFixture.Definition(), [new DrillStep("Region", "EU")], []);

        var generated = builder.Build(drilled, Scope);

        Assert.Contains($"WHERE ({scopeSql}) AND (", generated.Sql);
        Assert.DoesNotContain("EU", generated.Sql);
        Assert.Contains(generated.Parameters, p => p.LiteralValue == "EU");
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void An_OR_saved_filter_plus_a_step_cannot_escape_scope(IQuerySqlBuilder builder, string scopeSql)
    {
        var saved = VizFixture.Definition();
        saved.Filters.Operator = FilterLogicalOperator.Or;
        saved.Filters.Conditions.Add(TestData.Condition("Channel", FilterOperator.Equals, "EU"));
        saved.Filters.Conditions.Add(TestData.Condition("Channel", FilterOperator.Equals, "US"));

        var generated = builder.Build(DrillDefinitions.ForRows(saved, [new DrillStep("Region", "EU")], []), Scope);

        var where = generated.Sql[generated.Sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.StartsWith($"WHERE ({scopeSql}) AND ((", where);
        Assert.Matches(@"AND \(\(.* OR .*\) AND \(.*\)\)", where);
    }
}
