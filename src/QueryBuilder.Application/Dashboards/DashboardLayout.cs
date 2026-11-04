namespace QueryBuilder.Application.Dashboards;

public readonly record struct LayoutBox(int X, int Y, int W, int H)
{
    public bool Overlaps(LayoutBox other) =>
        X < other.X + other.W && other.X < X + W && Y < other.Y + other.H && other.Y < Y + H;
}

/// <summary>Pure grid arithmetic for dashboards, so the same rules repair a layout from any client and
/// convert pre-grid dashboards without touching storage.</summary>
public static class DashboardLayout
{
    /// <summary>Vertical compaction: tiles are visited by (y, x) and each drops to the lowest free row
    /// within its column span that doesn't overlap an already-placed tile. The result is returned in the
    /// final reading order (y, then x), which is also the order the caller should store.</summary>
    public static List<(T Item, LayoutBox Box)> Compact<T>(IEnumerable<(T Item, LayoutBox Box)> tiles)
    {
        var sorted = tiles
            .Select((t, index) => (t.Item, t.Box, Index: index))
            .OrderBy(t => t.Box.Y).ThenBy(t => t.Box.X).ThenBy(t => t.Index)
            .ToList();

        var placed = new List<LayoutBox>(sorted.Count);
        var result = new List<(T Item, LayoutBox Box)>(sorted.Count);
        foreach (var (item, box, _) in sorted)
        {
            var y = 0;
            while (true)
            {
                var candidate = box with { Y = y };
                var hit = placed.FirstOrDefault(p => p.Overlaps(candidate));
                if (hit.W == 0)
                {
                    placed.Add(candidate);
                    result.Add((item, candidate));
                    break;
                }

                y = hit.Y + hit.H;
            }
        }

        return result
            .OrderBy(r => r.Box.Y).ThenBy(r => r.Box.X)
            .ToList();
    }

    /// <summary>Pre-grid dashboards stored a width preset and a height of 1-3 rows. Tiles flow left to
    /// right in <c>Order</c>; one that doesn't fit starts a row below the tallest tile of the previous row.</summary>
    public static List<(T Item, LayoutBox Box)> FromLegacy<T>(IEnumerable<(T Item, int Width, int Height)> tilesInOrder)
    {
        var result = new List<(T Item, LayoutBox Box)>();
        int x = 0, rowY = 0, rowHeight = 0;
        foreach (var (item, width, height) in tilesInOrder)
        {
            var w = Math.Clamp(width, DashboardAccess.MinWidth, DashboardAccess.GridColumns);
            var h = Math.Clamp(height * 3, DashboardAccess.MinHeight, DashboardAccess.MaxHeight);
            if (x + w > DashboardAccess.GridColumns)
            {
                rowY += rowHeight;
                x = 0;
                rowHeight = 0;
            }

            result.Add((item, new LayoutBox(x, rowY, w, h)));
            x += w;
            rowHeight = Math.Max(rowHeight, h);
        }

        return result;
    }

    public static int BottomY(IEnumerable<LayoutBox> boxes)
    {
        var bottom = 0;
        foreach (var b in boxes)
        {
            bottom = Math.Max(bottom, b.Y + b.H);
        }

        return bottom;
    }
}
