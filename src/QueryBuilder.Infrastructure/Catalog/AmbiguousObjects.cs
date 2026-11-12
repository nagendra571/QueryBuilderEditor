using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Infrastructure.Catalog;

/// <summary>Finds objects whose "schema.name" keys are equal ignoring case, which covers both
/// <c>public.Orders</c> with <c>public.orders</c> and schema <c>a.b</c> table <c>c</c> with schema <c>a</c> table <c>b.c</c>.
/// Rules are keyed by that string under a case-insensitive unique index, so such objects can't be told apart.</summary>
public static class AmbiguousObjects
{
    public static (List<SchemaObjectMetadata> Usable, List<AdminUnavailableObject> Unavailable) Partition(
        IReadOnlyList<SchemaObjectMetadata> objects)
    {
        var groups = objects
            .GroupBy(o => $"{o.SchemaName}.{o.Name}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var usable = new List<SchemaObjectMetadata>();
        var unavailable = new List<AdminUnavailableObject>();
        foreach (var obj in objects)
        {
            var group = groups[$"{obj.SchemaName}.{obj.Name}"];
            if (group.Count == 1)
            {
                usable.Add(obj);
                continue;
            }

            unavailable.Add(new AdminUnavailableObject
            {
                SchemaName = obj.SchemaName,
                Name = obj.Name,
                Kind = obj.Kind,
                Reason = UnavailableReason.NameClash,
                ClashesWith = group.Where(o => !ReferenceEquals(o, obj)).Select(o => Display(o)).ToList()
            });
        }

        return (usable, unavailable);
    }

    /// <summary>The plain "schema.name" when no dot makes it ambiguous, otherwise <c>"a.b"."c"</c>.</summary>
    public static string Display(SchemaObjectMetadata o) => Display(o.SchemaName, o.Name);

    public static string Display(string schema, string name) =>
        schema.Contains('.') || name.Contains('.') ? $"\"{schema}\".\"{name}\"" : $"{schema}.{name}";
}
