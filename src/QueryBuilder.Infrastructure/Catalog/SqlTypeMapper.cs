using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Infrastructure.Catalog;

public static class SqlTypeMapper
{
    public static ColumnDataType Map(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "bit" => ColumnDataType.Boolean,
        "date" => ColumnDataType.Date,
        "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => ColumnDataType.DateTime,
        "time" => ColumnDataType.DateTime,
        "tinyint" or "smallint" or "int" or "bigint" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => ColumnDataType.Number,
        "uniqueidentifier" => ColumnDataType.Guid,
        "char" or "varchar" or "text" or "nchar" or "nvarchar" or "ntext" => ColumnDataType.Text,
        _ => ColumnDataType.Unknown
    };
}
