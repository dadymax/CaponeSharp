using System.Collections.Generic;
using Godot;

namespace Capone.Tests;

/// <summary>The original's step decisions (MoveMap: sub_427CC0, sub_427EF5, sub_428244) on hand-made maps.</summary>
public static class MovementTests
{
    private static List<Vector2I> Square(int r)
    {
        var cells = new List<Vector2I>();
        for (var y = -r; y <= r; y++)
            for (var x = -r; x <= r; x++)
                cells.Add(new Vector2I(x, y));
        return cells;
    }

    private static void Map(IEnumerable<Vector2I> blocked = null, IEnumerable<Vector2I> sand = null, IEnumerable<Vector2I> roads = null)
    {
        MoveMap.InitForTest(Square(10), blocked ?? new Vector2I[0], sand ?? new Vector2I[0], roads ?? new Vector2I[0]);
        MoveMap.Occupancy = (_, _) => (0, false, false);
        MoveMap.Rng = new System.Random(1);
    }

    /// <summary>Walks with NextDirection (and a detour when stuck) like the move job; returns the cells passed.</summary>
    private static List<Vector2I> Walk(Vector2I from, Vector2I to, bool car = false, int limit = 60)
    {
        var path = new List<Vector2I> { from };
        var here = from;
        Vector2I? detour = null;
        for (var i = 0; i < limit; i++)
        {
            var d = MoveMap.NextDirection(here, detour ?? to, from, to, car, null);
            if (d == MoveMap.Arrived && detour != null)
            {
                detour = null;
                d = MoveMap.NextDirection(here, to, from, to, car, null);
            }
            if (d == MoveMap.Stuck && detour == null)
            {
                detour = MoveMap.FindDetour(here, to, car, c => path.Contains(c) && c != here);
                if (detour == null)
                    break;
                d = MoveMap.NextDirection(here, detour.Value, from, to, car, null);
            }
            if (d is MoveMap.Arrived or MoveMap.Stuck)
                break;
            here = MoveMap.Step(here, d);
            path.Add(here);
        }
        return path;
    }

    [UnitTest]
    public static void ScoresFollowTheOriginal()
    {
        Map(blocked: new[] { new Vector2I(1, 0) }, roads: new[] { new Vector2I(0, 1) });
        var to = new Vector2I(5, 5);
        Assert.Equal(6, MoveMap.Score(to, to, false, null), "the destination");
        Assert.Equal(0, MoveMap.Score(new Vector2I(1, 0), to, false, null), "a building");
        Assert.Equal(0, MoveMap.Score(new Vector2I(20, 0), to, false, null), "off the map");
        Assert.Equal(5, MoveMap.Score(new Vector2I(0, 1), to, false, null), "free road");
        Assert.Equal(4, MoveMap.Score(new Vector2I(-1, 0), to, false, null), "free other ground");
        MoveMap.Occupancy = (c, _) => c == new Vector2I(-1, 0) ? (1, false, false) : c == new Vector2I(0, -1) ? (1, true, false) : c == new Vector2I(2, 2) ? (2, false, false) : c == new Vector2I(0, 1) ? (1, false, true) : (0, false, false);
        Assert.Equal(2, MoveMap.Score(new Vector2I(-1, 0), to, false, null), "a standing unit");
        Assert.Equal(4, MoveMap.Score(new Vector2I(0, -1), to, false, null), "a moving unit");
        Assert.Equal(1, MoveMap.Score(new Vector2I(2, 2), to, false, null), "two units");
        Assert.Equal(0, MoveMap.Score(new Vector2I(0, 1), to, false, null), "a road with a unit fighting on it is closed (sub_432E13)");
    }

    [UnitTest]
    public static void StraightWalkStaysNearTheLine()
    {
        Map();
        var path = Walk(Vector2I.Zero, new Vector2I(4, 4));
        Assert.Equal(new Vector2I(4, 4), path[^1], "arrives");
        Assert.Equal(9, path.Count, "8 steps, no detour");
        foreach (var c in path)
            Assert.True(MoveMap.LineDistance(Vector2I.Zero, new Vector2I(4, 4), c) < 1.0, $"{c} near the diagonal");
    }

    [UnitTest]
    public static void StopsNextToABuilding()
    {
        var building = new Vector2I(3, 0);
        Map(blocked: new[] { building });
        var path = Walk(Vector2I.Zero, building);
        Assert.Equal(new Vector2I(2, 0), path[^1], "stops beside the building");
    }

    [UnitTest]
    public static void WalksAroundAWall()
    {
        var wall = new List<Vector2I>();
        for (var y = -3; y <= 3; y++)
            wall.Add(new Vector2I(2, y));
        Map(blocked: wall);
        var path = Walk(Vector2I.Zero, new Vector2I(5, 0));
        Assert.Equal(new Vector2I(5, 0), path[^1], "gets round the wall");
        foreach (var c in path)
            Assert.False(wall.Contains(c), $"never inside the wall ({c})");
    }

    [UnitTest]
    public static void TheCarKeepsOffThePavement()
    {
        var sand = new List<Vector2I>();
        for (var y = -10; y <= 10; y++)
            sand.Add(new Vector2I(3, y));
        Map(sand: sand);
        Assert.True(MoveMap.Walkable(new Vector2I(3, 0), false), "pedestrians walk on the pavement");
        Assert.False(MoveMap.Walkable(new Vector2I(3, 0), true), "the Cadillac doesn't");
        var path = Walk(Vector2I.Zero, new Vector2I(6, 0), car: true);
        Assert.False(path.Contains(new Vector2I(3, 0)), "the car never crosses the pavement");
    }

    [UnitTest]
    public static void BridgesAreWalkable()
    {
        // Map letter I (sub_420BD0, ground class 3) is walkable for everyone: not in the blocked set.
        Map();
        Assert.True(MoveMap.Walkable(new Vector2I(0, 0), true), "car on open ground");
    }
}
