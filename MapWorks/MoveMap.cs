using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// The original's tile-by-tile movement decisions (move job sub_4279E7), on map cells. Directions are the four grid
/// neighbours (sub_427C28): 0 x−1, 1 y+1, 2 x+1, 3 y−1. Results 4 (arrived) and 5 (stuck) as in the EXE.
/// <list type="bullet">
/// <item><see cref="Score"/> (sub_427CC0): 6 the destination, 0 off the map or not walkable for the unit, free 5 on
/// road / pavement (class 2 ground) or 4 elsewhere; one occupant: 4 if it is moving, 2 if it stands; more: 1.</item>
/// <item><see cref="NextDirection"/> (sub_427EF5): of the (up to two) directions toward the destination, the better
/// scored; a tie goes to the cell nearer the straight line from the move's start to its end (sub_428116), an exact
/// tie at random (the sidestep sub_427DF6 only runs outside a running game).</item>
/// <item><see cref="FindDetour"/> (sub_428244): when stuck, rays go out from the cell in the directions next to the
/// ones toward the destination; the first walkable cell beside a ray on the destination's side, not among the last
/// visited cells, becomes a waypoint.</item>
/// </list>
/// Walkable (tile vt+15Ch): R road and I for everyone, S pavement for everyone but the Cadillac.
/// </summary>
public static class MoveMap
{
    public const int Arrived = 4, Stuck = 5;
    private static readonly Vector2I[] Steps = { new(-1, 0), new(0, 1), new(1, 0), new(0, -1) };

    private static HashSet<Vector2I> _blocked = new();
    private static HashSet<Vector2I> _used = new();
    private static HashSet<Vector2I> _roads = new();
    private static HashSet<Vector2I> _sand = new();

    /// <summary>World +6 (sub_414CD0): how far the detour search looks.</summary>
    public static int SearchRange { get; private set; } = 100;

    /// <summary>Occupants of a cell other than the asking unit: (count, the first one is moving, one is fighting).</summary>
    public static Func<Vector2I, object, (int Count, bool Moving, bool Fighting)> Occupancy { get; set; } = (_, _) => (0, false, false);

    public static Random Rng { get; set; } = new();

    /// <summary>The sidestep of sub_427DF6 (see NextDirection); off in a running game.</summary>
    public static bool SidestepEnabled { get; set; }

    public static void Init(TileMapLayer map)
    {
        _used = new HashSet<Vector2I>(map.GetUsedCells());
        _blocked = Cells(map, TileMapGenerator.TileNonWalkable);
        _roads = Cells(map, TileMapGenerator.MetaRoad);
        _sand = Cells(map, TileMapGenerator.MetaSand);
        var rect = map.GetUsedRect();
        SearchRange = Math.Max(rect.Size.X, rect.Size.Y);
    }

    /// <summary>Unit-test hook.</summary>
    internal static void InitForTest(IEnumerable<Vector2I> used, IEnumerable<Vector2I> blocked, IEnumerable<Vector2I> sand,
        IEnumerable<Vector2I> roads, int range = 50)
    {
        _used = new HashSet<Vector2I>(used);
        _blocked = new HashSet<Vector2I>(blocked);
        _sand = new HashSet<Vector2I>(sand);
        _roads = new HashSet<Vector2I>(roads);
        SearchRange = range;
    }

    private static HashSet<Vector2I> Cells(TileMapLayer map, StringName meta) =>
        map.HasMeta(meta) ? new HashSet<Vector2I>(map.GetMeta(meta).AsGodotArray<Vector2I>()) : new HashSet<Vector2I>();

    public static Vector2I Step(Vector2I cell, int direction) => cell + Steps[((direction % 4) + 4) % 4];

    /// <summary>
    /// The road cell (cars may stand on it) closest to <paramref name="cell"/> by Manhattan distance, searching outward
    /// up to <paramref name="range"/>; null when none. Road-only cars drive there to "reach" a building, which is
    /// ringed by pavement.
    /// </summary>
    public static Vector2I? NearestRoad(Vector2I cell, int range = 12)
    {
        for (var r = 0; r <= range; r++)
            for (var dx = -r; dx <= r; dx++)
            {
                var dy = r - Mathf.Abs(dx);
                foreach (var c in dy == 0 ? new[] { cell + new Vector2I(dx, 0) } : new[] { cell + new Vector2I(dx, dy), cell + new Vector2I(dx, -dy) })
                    if (_roads.Contains(c) && Walkable(c, true))
                        return c;
            }
        return null;
    }

    /// <summary>S pavement (no cars).</summary>
    public static bool IsPavement(Vector2I cell) => _sand.Contains(cell);

    /// <summary>Tile vt+158h = 1: road or pavement (class 2 ground).</summary>
    public static bool IsRoadOrPavement(Vector2I cell) => _roads.Contains(cell) || _sand.Contains(cell);

    /// <summary>The ground alone: may a unit (a car or not) stand on the cell?</summary>
    public static bool Walkable(Vector2I cell, bool car) =>
        _used.Contains(cell) && !_blocked.Contains(cell) && !(car && _sand.Contains(cell));

    /// <summary>
    /// Tile vt+15Ch (sub_427BE3): the ground, and on road / pavement (sub_432E13) no unit fighting there — other
    /// units are passed (they only lower the score).
    /// </summary>
    public static bool WalkableNow(Vector2I cell, bool car, object self) =>
        Walkable(cell, car) && !((_roads.Contains(cell) || _sand.Contains(cell)) && Occupancy(cell, self).Fighting);

    /// <summary>sub_427CC0.</summary>
    public static int Score(Vector2I cell, Vector2I destination, bool car, object self)
    {
        if (!_used.Contains(cell))
            return 0;
        if (cell == destination)
            return 6;
        if (!WalkableNow(cell, car, self))
            return 0;
        var (count, moving, _) = Occupancy(cell, self);
        if (count == 0)
            return _roads.Contains(cell) || _sand.Contains(cell) ? 5 : 4;
        if (count > 1)
            return 1;
        return moving ? 4 : 2;
    }

    /// <summary>sub_427EF5. <paramref name="lineStart"/> / <paramref name="lineEnd"/>: the move's straight line.</summary>
    public static int NextDirection(Vector2I from, Vector2I to, Vector2I lineStart, Vector2I lineEnd, bool car, object self)
    {
        if (from == to)
            return Arrived;
        var dirs = new List<int>(2);
        if (from.X < to.X) dirs.Add(2);
        if (from.X > to.X) dirs.Add(0);
        if (from.Y < to.Y) dirs.Add(1);
        if (from.Y > to.Y) dirs.Add(3);
        int d0 = dirs[0], d1 = dirs.Count > 1 ? dirs[1] : Stuck;
        int s0 = Score(Step(from, d0), to, car, self), s1 = dirs.Count > 1 ? Score(Step(from, d1), to, car, self) : 0;
        if (s0 < s1)
        {
            (d0, d1) = (d1, d0);
            (s0, s1) = (s1, s0);
        }
        if (s0 == 6)
            return WalkableNow(Step(from, d0), car, self) ? d0 : Arrived; // the destination is a building: next to it
        if (s0 == 0)
            return Stuck;
        if (s0 == s1)
        {
            var e0 = LineDistance(lineStart, lineEnd, Step(from, d0));
            var e1 = LineDistance(lineStart, lineEnd, Step(from, d1));
            if (e0 == e1)
                return Rng.Next(2) == 0 ? d0 : d1;
            return e0 < e1 ? d0 : d1;
        }
        // sub_427DF6 sidestep: only when the world state (sub_414BA0) mod 3 isn't 0 — in a running game it is 3, so
        // units walk on into an occupied cell instead (they may share cells). Off: it made units shuttle back and forth.
        if (SidestepEnabled && s1 == 0 && s0 < 4)
        {
            var side = Sidestep(from, to, d0, car, self);
            if (side != Stuck)
                return side;
        }
        return d0;
    }

    /// <summary>sub_427DF6: try a perpendicular step (random side first) from which the original direction is open.</summary>
    private static int Sidestep(Vector2I from, Vector2I to, int direction, bool car, object self)
    {
        var turn = Rng.Next(2) * 2 + 1;
        foreach (var t in new[] { turn, turn + 2 })
        {
            var side = (direction + t) % 4;
            var cell = Step(from, side);
            if (Score(cell, to, car, self) > 3 && WalkableNow(Step(cell, direction), car, self))
                return side;
        }
        return Stuck;
    }

    /// <summary>sub_428116: distance of <paramref name="p"/> from the line through <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static double LineDistance(Vector2I a, Vector2I b, Vector2I p)
    {
        if (a.X == b.X)
            return Math.Abs(b.X - p.X);
        if (a.Y == b.Y)
            return Math.Abs(b.Y - p.Y);
        double k = (double)(a.Y - b.Y) / (a.X - b.X);
        double c = (b.Y * a.X - a.Y * (double)b.X) / (a.X - b.X);
        var x = (p.X + k * (p.Y - c)) / (k * k + 1);
        var y = k * x + c;
        return Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
    }

    /// <summary>sub_428244: a waypoint around an obstacle, or null.</summary>
    public static Vector2I? FindDetour(Vector2I from, Vector2I to, bool car, Func<Vector2I, bool> recentlyVisited, object self = null)
    {
        var toward = new bool[4];
        if (from.X > to.X) toward[0] = true;
        if (from.Y < to.Y) toward[1] = true;
        if (from.X < to.X) toward[2] = true;
        if (from.Y > to.Y) toward[3] = true;
        var dead = new bool[4];
        var rays = new[] { from, from, from, from };
        for (var range = 0; range < SearchRange; range++)
            for (var d = 0; d < 4; d++)
            {
                if (dead[d] || (!toward[(d + 1) % 4] && !toward[(d + 3) % 4]))
                    continue;
                rays[d] = Step(rays[d], d);
                if (!WalkableNow(rays[d], car, self))
                {
                    dead[d] = true;
                    continue;
                }
                foreach (var side in new[] { (d + 1) % 4, (d + 3) % 4 })
                {
                    if (!toward[side])
                        continue;
                    var beside = Step(rays[d], side);
                    if (!WalkableNow(beside, car, self))
                        continue;
                    if (recentlyVisited == null || !recentlyVisited(beside))
                        return beside;
                    dead[d] = true; // visited lately: this ray is done after its other side is tried
                }
            }
        return null;
    }
}
