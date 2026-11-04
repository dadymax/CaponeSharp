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
                return b.BlockLines[by][bx]; // row j = original y + j (fnBuildingPlacementOnMapByNumber)
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

    /// <summary>With the EXE's block rows (row j = original y + j) a standard start point is the B building itself.
    /// (The old flipped rows put it one cell off; that was the mirrored map, TODO playtest item 3.)</summary>
    [UnitTest]
    public static void StandardStartPointsAreOnTheirBuilding()
    {
        var map = OriginalData.Instance.Maps.Maps["HARLEM.MAP"];
        foreach (var sp in map.StartPoints)
            Assert.Equal('B', Letter(map, sp.X, sp.Y), $"start point ({sp.X}, {sp.Y})");
    }

    /// <summary>
    /// fnProcessMapLoad (0x403BBF–0x403BE9) drops start points on R / F / S / G cells; the others keep their order.
    /// </summary>
    [UnitTest]
    public static void StartPointsOnGroundAreDropped()
    {
        var map = OriginalData.Instance.Maps.Maps["SMALLNEIGHBORHOOD.MAP"];
        var letters = new System.Collections.Generic.Dictionary<Vector2I, char>();
        foreach (var sp in map.StartPoints)
            letters[sp] = Letter(map, sp.X, sp.Y);
        Assert.Equal(map.StartPoints.Count, TileMapGenerator.UsableStartPoints(map, letters).Count, "all on buildings");
        letters[map.StartPoints[0]] = 'R';
        letters[map.StartPoints[1]] = 'G';
        var usable = TileMapGenerator.UsableStartPoints(map, letters);
        Assert.Equal(map.StartPoints.Count - 2, usable.Count, "road and G start points dropped");
        Assert.Equal(map.StartPoints[2], usable[0], "order kept");
    }
}

/// <summary>Original ↔ map cell coordinates (MapTileMath).</summary>
public static class CoordinateTests
{
    [UnitTest]
    public static void ToOriginalInvertsFromOriginal()
    {
        var map = new TileMapLayer
        {
            TileSet = new TileSet
            {
                TileShape = TileSet.TileShapeEnum.Isometric,
                TileSize = MapTileMath.StandartTile,
                TileLayout = TileSet.TileLayoutEnum.DiamondRight,
                TileOffsetAxis = TileSet.TileOffsetAxisEnum.Vertical,
            },
        };
        try
        {
            foreach (var o in new[] { new Vector2I(0, 0), new Vector2I(17, 14), new Vector2I(84, 84), new Vector2I(3, 90) })
                Assert.Equal(o, MapTileMath.ToOriginal(map, MapTileMath.FromOriginal(map, o)), $"round trip {o}");
        }
        finally
        {
            map.Free();
        }
    }
}
