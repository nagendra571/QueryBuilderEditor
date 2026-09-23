using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests;

internal static class TestData
{
    public static readonly Guid DataSourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static DataSourceCatalog Catalog() => new()
    {
        DataSourceId = DataSourceId,
        Schemas =
        [
            new SchemaMetadata
            {
                Name = "sales",
                Objects =
                [
                    View("sales", "vw_Orders", ("OrderId", ColumnDataType.Number), ("ProgramId", ColumnDataType.Number),
                        ("RegionCode", ColumnDataType.Text), ("Amount", ColumnDataType.Number)),
                    View("sales", "vw_Programs", ("ProgramId", ColumnDataType.Number), ("ProgramName", ColumnDataType.Text)),
                    View("sales", "vw_Secret", ("Id", ColumnDataType.Number))
                ]
            }
        ]
    };

    private static SchemaObjectMetadata View(string schema, string name, params (string Name, ColumnDataType Type)[] columns) => new()
    {
        SchemaName = schema,
        Name = name,
        Kind = SchemaObjectKind.View,
        Columns = columns.Select((c, i) => new ColumnMetadata { Name = c.Name, DataType = c.Type, OrdinalPosition = i + 1 }).ToList()
    };

    public static DataScopeRule Rule(string objectName, string? key = null, string? column = null) => new()
    {
        DataSourceId = DataSourceId,
        ObjectName = objectName,
        ScopeKey = key,
        ColumnName = column
    };

    public static ResolvedDataScope Scoped(params (string Key, string[] Values)[] values) =>
        new(false, values.ToDictionary(v => v.Key, v => (IReadOnlyList<string>)v.Values, StringComparer.OrdinalIgnoreCase));

    public static QueryDefinition Definition(string objectName = "vw_Orders", string alias = "t0") => new()
    {
        Source = new QuerySource { SchemaName = "sales", ObjectName = objectName, Alias = alias },
        Columns = [new QueryColumn { TableAlias = alias, ColumnName = objectName == "vw_Orders" ? "OrderId" : "ProgramId" }]
    };

    public static FilterCondition Condition(string column, FilterOperator op, string? value, string alias = "t0") => new()
    {
        TableAlias = alias,
        ColumnName = column,
        Operator = op,
        Value = value,
        DataType = ColumnDataType.Text
    };
}
