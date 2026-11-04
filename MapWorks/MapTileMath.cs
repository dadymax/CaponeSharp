using Godot;

namespace Capone;

/// <summary>Isometric tile math helpers.</summary>
public static class MapTileMath
{
    public enum Directions { SE, NE, NW, SW }

    public static readonly Vector2I StandartTile = new(40, 19);
    /// <summary>Screen step of original x + 1: right-down (EXE sub_414A30: sx ∝ x − y, sy ∝ x + y).</summary>
    public static readonly Vector2 Right = new(StandartTile.X / 2.0f, StandartTile.Y / 2.0f);
    /// <summary>Screen step of original y + 1: left-down (was right-up until 2026-11-04, which mirrored the map).</summary>
    public static readonly Vector2 Down = new(-StandartTile.X / 2.0f, StandartTile.Y / 2.0f);

    // Sprite facings are screen directions, independent of the map axes.
    private static readonly Vector2 SeNormalised = new Vector2(StandartTile.X, StandartTile.Y).Normalized();
    private static readonly Vector2 NeNormalised = new Vector2(StandartTile.X, -StandartTile.Y).Normalized();
    private static readonly Vector2 NwNormalised = -SeNormalised;
    private static readonly Vector2 SwNormalised = -NeNormalised;

    /// <summary>Pixel distance between the centres of two neighbouring cells.</summary>
    public static readonly float TileStepLength = Right.Length();

    /// <summary>The current game map; set by Main after the map is built.</summary>
    public static TileMapLayer Tilemap { get; set; }

    /// <summary>Offsets a map cell by (add.X, add.Y) steps along the original game's block axes.</summary>
    public static Vector2I Add(TileMapLayer map, Vector2I from, Vector2I add = default)
    {
        var pixels = map.MapToLocal(from);
        var offset = Right * add.X + Down * add.Y;
        return map.LocalToMap(pixels + offset);
    }

    /// <summary>Map cell of an original-game coordinate (as used in .map STARTPOINTS and .epi UNIT/VCOND lines).</summary>
    public static Vector2I FromOriginal(TileMapLayer map, Vector2I original) => Add(map, Vector2I.Zero, original);

    /// <summary>Original-game coordinate of a map cell (the inverse of <see cref="FromOriginal"/>, a linear map).</summary>
    public static Vector2I ToOriginal(TileMapLayer map, Vector2I cell)
    {
        var o = FromOriginal(map, Vector2I.Zero);
        var ex = FromOriginal(map, new Vector2I(1, 0)) - o;
        var ey = FromOriginal(map, new Vector2I(0, 1)) - o;
        var d = cell - o;
        var det = ex.X * ey.Y - ex.Y * ey.X;
        if (det == 0)
            return d;
        return new Vector2I((d.X * ey.Y - d.Y * ey.X) / det, (ex.X * d.Y - ex.Y * d.X) / det);
    }

    /// <summary>Exact distance in map cells between two nodes.</summary>
    public static float GetDistanceExact(Node2D node1, Node2D node2)
    {
        var pos1 = Tilemap.LocalToMap(node1.Position);
        var pos2 = Tilemap.LocalToMap(node2.Position);
        return ((Vector2)(pos2 - pos1)).Length();
    }

    public static Directions GetDirection(Vector2 velocity)
    {
        var dir = velocity.Normalized();
        var se = dir.Dot(SeNormalised);
        var nw = dir.Dot(NwNormalised);
        var ne = dir.Dot(NeNormalised);
        var sw = dir.Dot(SwNormalised);
        var max = Mathf.Max(se, Mathf.Max(nw, Mathf.Max(ne, sw)));
        if (max == se) return Directions.SE;
        if (max == nw) return Directions.NW;
        if (max == ne) return Directions.NE;
        if (max == sw) return Directions.SW;
        return Directions.NW;
    }

    /// <summary>Distance in map cells between two nodes (uses their local positions).</summary>
    public static int GetDistance(Node2D node1, Node2D node2)
    {
        var pos1 = Tilemap.LocalToMap(node1.Position);
        var pos2 = Tilemap.LocalToMap(node2.Position);
        return Mathf.FloorToInt(Mathf.Abs(((Vector2)(pos2 - pos1)).Length()));
    }
}
