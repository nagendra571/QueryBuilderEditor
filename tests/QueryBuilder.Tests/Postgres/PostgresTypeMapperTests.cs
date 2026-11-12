using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Catalog;

namespace QueryBuilder.Tests.Postgres;

public class PostgresTypeMapperTests
{
    [Theory]
    [InlineData("int2", ColumnDataType.Number)]
    [InlineData("int4", ColumnDataType.Number)]
    [InlineData("int8", ColumnDataType.Number)]
    [InlineData("numeric", ColumnDataType.Number)]
    [InlineData("float4", ColumnDataType.Number)]
    [InlineData("float8", ColumnDataType.Number)]
    [InlineData("date", ColumnDataType.Date)]
    [InlineData("timestamp", ColumnDataType.DateTime)]
    [InlineData("timestamptz", ColumnDataType.DateTime)]
    [InlineData("bool", ColumnDataType.Boolean)]
    [InlineData("uuid", ColumnDataType.Guid)]
    [InlineData("text", ColumnDataType.Text)]
    [InlineData("varchar", ColumnDataType.Text)]
    [InlineData("bpchar", ColumnDataType.Text)]
    [InlineData("name", ColumnDataType.Text)]
    [InlineData("citext", ColumnDataType.Text)]
    [InlineData("money", ColumnDataType.Unknown)]
    [InlineData("time", ColumnDataType.Unknown)]
    [InlineData("interval", ColumnDataType.Unknown)]
    [InlineData("json", ColumnDataType.Unknown)]
    [InlineData("_int4", ColumnDataType.Unknown)]
    public void Maps_base_types(string typeName, ColumnDataType expected) =>
        Assert.Equal(expected, PostgresTypeMapper.Map(typeName, "b"));

    [Theory]
    [InlineData("mood")]
    [InlineData("anything")]
    public void Enums_are_text_whatever_their_name(string typeName) =>
        Assert.Equal(ColumnDataType.Text, PostgresTypeMapper.Map(typeName, "e"));
}
