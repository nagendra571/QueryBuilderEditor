using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Catalog;

public static class PostgresTypeMapper
{
    /// <param name="baseTypeName">pg_type.typname after resolving domains to their base type.</param>
    /// <param name="typeType">pg_type.typtype of that base type; 'e' is an enum.</param>
    public static ColumnDataType Map(string baseTypeName, string typeType)
    {
        if (typeType == "e")
        {
            return ColumnDataType.Text;
        }

        return baseTypeName switch
        {
            "int2" or "int4" or "int8" or "numeric" or "float4" or "float8" => ColumnDataType.Number,
            "date" => ColumnDataType.Date,
            "timestamp" or "timestamptz" => ColumnDataType.DateTime,
            "bool" => ColumnDataType.Boolean,
            "uuid" => ColumnDataType.Guid,
            "text" or "varchar" or "bpchar" or "name" or "citext" => ColumnDataType.Text,
            _ => ColumnDataType.Unknown
        };
    }
}
