using Godot;

namespace Capone.Tests;

/// <summary>Checks of the original maps.</summary>
public static class MapTests
{
    /// <summary>Block letter at an original map cell, '?' outside every block.</summary>
    private static char Letter(OriginalMaps.Map map, int x, int y)
    {
        var blocks = OriginalData.Instance.BuildingBlocks.Blocks;
        foreach (var p in map.BlockPlacement)
        {
            if (p.Z < 0 || p.Z >= blocks.Count)
                continue;
            var b = blocks[p.Z];
            var bx = x - p.X;
            var by = y - p.Y;
            if (bx >= 0 && by >= 0 && bx < b.X && by < b.Y)
                return b.BlockLines[b.Y - 1 - by][bx];
        }
        return '?';
    }

    [UnitTest]
    public static void EveryStartPointHasAMediumBuildingNearby()
    {
        const int r = TileMapGenerator.BaseSearchRadius;
        foreach (var (name, map) in OriginalData.Instance.Maps.Maps)
            foreach (var sp in map.StartPoints)
            {
                var found = false;
                for (var dy = -r; dy <= r && !found; dy++)
                    for (var dx = -r; dx <= r && !found; dx++)
                        found = dx * dx + dy * dy <= r * r && Letter(map, sp.X + dx, sp.Y + dy) == 'B';
                Assert.True(found, $"{name}: start point ({sp.X}, {sp.Y}) has no B within {r} cells");
            }
    }

    [UnitTest]
    public static void StandardStartPointsAreNextToTheirBuilding()
    {
        var map = OriginalData.Instance.Maps.Maps["HARLEM.MAP"];
        foreach (var sp in map.StartPoints)
        {
            Assert.Equal('G', Letter(map, sp.X, sp.Y), $"start point ({sp.X}, {sp.Y}) itself");
            Assert.Equal('B', Letter(map, sp.X, sp.Y - 1), $"one cell off ({sp.X}, {sp.Y - 1})");
        }
    }
}
