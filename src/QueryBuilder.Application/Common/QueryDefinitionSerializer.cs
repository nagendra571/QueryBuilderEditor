using System.Text.Json;
using System.Text.Json.Serialization;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Common;

public static class QueryDefinitionSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(QueryDefinition definition) => JsonSerializer.Serialize(definition, Options);

    public static QueryDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<QueryDefinition>(json, Options)
        ?? throw new InvalidOperationException("Stored query definition could not be parsed.");
}
