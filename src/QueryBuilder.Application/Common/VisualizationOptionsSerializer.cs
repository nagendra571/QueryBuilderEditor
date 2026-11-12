using System.Text.Json;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Common;

public static class VisualizationOptionsSerializer
{
    public static string Serialize(VisualizationOptions options) =>
        JsonSerializer.Serialize(options, QueryDefinitionSerializer.Options);

    /// <summary>A stored blob that no longer parses comes back as version 0, which every consumer
    /// treats as "needs updating" instead of failing the whole list.</summary>
    public static VisualizationOptions Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<VisualizationOptions>(json, QueryDefinitionSerializer.Options)
                ?? new VisualizationOptions { Version = 0 };
        }
        catch (JsonException)
        {
            return new VisualizationOptions { Version = 0 };
        }
    }
}
