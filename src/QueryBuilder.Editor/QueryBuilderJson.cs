using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryBuilder.Editor;

/// <summary>
/// QueryBuilder's own request/response JSON contract (camelCase properties, string enums) — used
/// explicitly by every endpoint instead of relying on the host app's <c>ConfigureHttpJsonOptions</c>.
/// The embedded frontend is built against this exact contract, so correctness can't depend on
/// whether (or how) the host configured its own global JSON options.
/// </summary>
internal static class QueryBuilderJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
