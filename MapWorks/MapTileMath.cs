using Godot;

namespace Capone;

/// <summary>Isometric tile math helpers.</summary>
public static class MapTileMath
{
    public enum Directions { SE, NE, NW, SW }

    public static readonly Vector2I StandartTile = new(40, 19);
    public static readonly Vector2 Right = new(StandartTile.X / 2.0f, StandartTile.Y / 2.0f);
    public static readonly Vector2 Down = new(StandartTile.X / 2.0f, -StandartTile.Y / 2.0f);

    private static readonly Vector2 RightNormalised = Right.Normalized();
    private static readonly Vector2 LeftNormalised = -RightNormalised;
    private static readonly Vector2 DownNormalised = Down.Normalized();
    private static readonly Vector2 UpNormalised = -DownNormalised;

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
        var right = dir.Dot(RightNormalised);
        var left = dir.Dot(LeftNormalised);
        var down = dir.Dot(DownNormalised);
        var up = dir.Dot(UpNormalised);
        var max = Mathf.Max(right, Mathf.Max(left, Mathf.Max(down, up)));
        if (max == right) return Directions.SE;
        if (max == left) return Directions.NW;
        if (max == down) return Directions.NE;
        if (max == up) return Directions.SW;
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
