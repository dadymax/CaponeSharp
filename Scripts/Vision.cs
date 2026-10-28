using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// Line of sight (sub_447870 → generic sub_447660; the EXE has unrolled copies for radius 3, 4, 8 and 9). A sight grid
/// covers (2r+1)² cells around the viewer. Every cell starts visible; then, ring by ring from 2 to r, a cell is hidden
/// unless a neighbour one step back toward the centre is visible, lies on a see-through tile and is within r by
/// straight-line distance (sub_4471D0 / sub_4470D0). When one axis dominates there are two such neighbours (back along
/// that axis, and diagonally back); either one is enough. The cell itself is not tested, so the wall facing the viewer
/// is seen and what lies behind it is not.
/// See-through tiles (map object vt+168h): ground — roads, sidewalks, parks, 'F' — yes; buildings ('B', 'C', 'M', 'H',
/// 'D', 'G', 'P', companies, HQs) no. The Godot map is the original grid with y flipped, so this works on Godot cells.
/// </summary>
public static class Vision
{
    private static HashSet<Vector2I> _used = new();
    private static HashSet<Vector2I> _opaque = new();

    /// <summary>Takes the map's cells and its building cells (<see cref="TileMapGenerator.MetaOpaque"/>).</summary>
    public static void Init(TileMapLayer map)
    {
        _used = new HashSet<Vector2I>(map.GetUsedCells());
        _opaque = map.HasMeta(TileMapGenerator.MetaOpaque)
            ? new HashSet<Vector2I>(map.GetMeta(TileMapGenerator.MetaOpaque).AsGodotArray<Vector2I>())
            : new HashSet<Vector2I>();
    }

    public static bool SeeThrough(Vector2I cell) => _used.Contains(cell) && !_opaque.Contains(cell);

    /// <summary>Sight grid of radius <paramref name="r"/> around <paramref name="center"/>; index (dy + r) * (2r + 1) + dx + r.</summary>
    public static bool[] Build(Vector2I center, int r)
    {
        r = Mathf.Max(0, r);
        var size = 2 * r + 1;
        var grid = new bool[size * size];
        for (var i = 0; i < grid.Length; i++)
            grid[i] = true;
        for (var ring = 2; ring <= r; ring++)
            for (var dy = -ring; dy <= ring; dy++)
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring)
                        continue;
                    if (!Passes(grid, center, r, dx, dy))
                        grid[(dy + r) * size + dx + r] = false;
                }
        return grid;
    }

    /// <summary>sub_4471D0: is one of the cells one step back toward the centre see-through?</summary>
    private static bool Passes(bool[] grid, Vector2I center, int r, int dx, int dy)
    {
        var sx = Mathf.Sign(dx);
        var sy = Mathf.Sign(dy);
        var ax = Mathf.Abs(dx);
        var ay = Mathf.Abs(dy);
        if (dx == 0)
            return SeeThroughStep(grid, center, r, 0, dy - sy);
        if (dy == 0)
            return SeeThroughStep(grid, center, r, dx - sx, 0);
        if (ax == ay)
            return SeeThroughStep(grid, center, r, dx - sx, dy - sy);
        if (ax > ay)
            return SeeThroughStep(grid, center, r, dx - sx, dy - sy) || SeeThroughStep(grid, center, r, dx - sx, dy);
        return SeeThroughStep(grid, center, r, dx - sx, dy - sy) || SeeThroughStep(grid, center, r, dx, dy - sy);
    }

    /// <summary>sub_4470D0: visible in the grid, a see-through tile, and within r by straight-line distance.</summary>
    private static bool SeeThroughStep(bool[] grid, Vector2I center, int r, int dx, int dy)
    {
        var size = 2 * r + 1;
        if (!grid[(dy + r) * size + dx + r])
            return false;
        if (!SeeThrough(center + new Vector2I(dx, dy)))
            return false;
        return Mathf.Sqrt(dx * dx + dy * dy) <= r;
    }

    /// <summary>Is <paramref name="cell"/> visible in a grid built around <paramref name="center"/> with radius <paramref name="r"/>?</summary>
    public static bool Visible(bool[] grid, Vector2I center, int r, Vector2I cell)
    {
        var d = cell - center;
        if (Mathf.Abs(d.X) > r || Mathf.Abs(d.Y) > r || grid == null)
            return false;
        return grid[(d.Y + r) * (2 * r + 1) + d.X + r];
    }
}
