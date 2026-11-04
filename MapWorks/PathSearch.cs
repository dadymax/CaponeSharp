using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// Optional A* path search (game.cfg <c>[movement] pathfinding=astar</c>; not in the original, which walks greedily
/// with <see cref="MoveMap.NextDirection"/>). Same grid, same four steps and the same rules as the original walk:
/// <see cref="MoveMap.WalkableNow"/> decides where a unit (or a road-only car) may step, occupied cells cost more but
/// don't block (units may share cells), and a destination that isn't walkable (a building) is reached by standing
/// next to it. Step costs follow the original's cell scores (sub_427CC0): free road or pavement 1, other free ground
/// 1.2, a cell with a walking unit 1.5, a standing one 3, a crowded one 4.
/// </summary>
public static class PathSearch
{
    /// <summary>Upper bound of expanded cells per search (a 96 × 96 map has 9 216 cells).</summary>
    public const int MaxExpanded = 20_000;

    /// <summary>
    /// The direction (0–3, <see cref="MoveMap.Step"/>) of the first step of the cheapest path, <see cref="MoveMap.Arrived"/>
    /// when the unit is at the destination (or next to a destination it can't enter), or null when there is no path.
    /// </summary>
    public static int? FirstStep(Vector2I from, Vector2I to, bool car, object self)
    {
        if (from == to)
            return MoveMap.Arrived;
        var enterable = MoveMap.WalkableNow(to, car, self);
        if (!enterable && IsNeighbour(from, to))
            return MoveMap.Arrived; // the destination is a building: next to it, like the original walk

        var open = new PriorityQueue<Vector2I, float>();
        var cost = new Dictionary<Vector2I, float> { [from] = 0f };
        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        open.Enqueue(from, Heuristic(from, to));
        var expanded = 0;
        while (open.TryDequeue(out var cell, out _))
        {
            if (cell == to || (!enterable && IsNeighbour(cell, to)))
                return FirstDirection(from, cell, cameFrom);
            if (++expanded > MaxExpanded)
                break;
            for (var d = 0; d < 4; d++)
            {
                var next = MoveMap.Step(cell, d);
                if (next != to && !MoveMap.WalkableNow(next, car, self))
                    continue;
                if (next == to && !enterable)
                    continue;
                var g = cost[cell] + StepCost(next, self);
                if (cost.TryGetValue(next, out var known) && known <= g)
                    continue;
                cost[next] = g;
                cameFrom[next] = cell;
                open.Enqueue(next, g + Heuristic(next, to));
            }
        }
        return null;
    }

    /// <summary>Cost of stepping into <paramref name="cell"/>, from the original's cell scores.</summary>
    public static float StepCost(Vector2I cell, object self)
    {
        var (count, moving, _) = MoveMap.Occupancy(cell, self);
        if (count == 0)
            return MoveMap.IsRoadOrPavement(cell) ? 1f : 1.2f;
        if (count > 1)
            return 4f;
        return moving ? 1.5f : 3f;
    }

    private static float Heuristic(Vector2I a, Vector2I b) => Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Y - b.Y);

    private static bool IsNeighbour(Vector2I a, Vector2I b) => Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Y - b.Y) == 1;

    private static int FirstDirection(Vector2I from, Vector2I end, Dictionary<Vector2I, Vector2I> cameFrom)
    {
        if (end == from)
            return MoveMap.Arrived;
        var cell = end;
        while (cameFrom.TryGetValue(cell, out var previous) && previous != from)
            cell = previous;
        for (var d = 0; d < 4; d++)
            if (MoveMap.Step(from, d) == cell)
                return d;
        return MoveMap.Stuck;
    }
}
