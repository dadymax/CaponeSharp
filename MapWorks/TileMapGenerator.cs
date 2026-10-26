using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Builds the playable TileMapLayer from a .map file, tile blocks and Chicago.tile.</summary>
public static class TileMapGenerator
{
    public static readonly StringName CompanyLayerName = "company";
    public static readonly StringName TileNonWalkable = "nonWalkable";
    public static readonly StringName MetaNameBases = "Bases";

    private const string CompanyScenePath = "res://Buildings/Company.tscn";

    public static TileMapLayer CreateTileMap(string mapName, int baseCount = 8, Node2D parent = null, bool randomBases = false)
    {
        GD.Randomize();
        CityBlock.ClearAll();
        var bmps = OriginalData.Instance.Bmps;
        var ct = OriginalData.Instance.Tiles;
        var cb = OriginalData.Instance.BuildingBlocks;
        var maps = OriginalData.Instance.Maps;

        if (!maps.Maps.TryGetValue(mapName.ToUpperInvariant(), out var map))
        {
            GD.PrintErr("TileMapGenerator: map not found: ", mapName);
            return null;
        }
        var companyScene = GD.Load<PackedScene>(CompanyScenePath);
        var standartTile = OriginalData.StandartTile;

        var tileSet = new TileSet
        {
            TileShape = TileSet.TileShapeEnum.Isometric,
            TileSize = standartTile,
            TileLayout = TileSet.TileLayoutEnum.DiamondRight,
            TileOffsetAxis = TileSet.TileOffsetAxisEnum.Vertical,
        };
        tileSet.AddNavigationLayer();
        tileSet.AddCustomDataLayer();
        tileSet.SetCustomDataLayerName(0, CompanyLayerName);
        tileSet.AddCustomDataLayer();
        tileSet.SetCustomDataLayerName(1, TileNonWalkable);

        var tileMap = new TileMapLayer
        {
            TileSet = tileSet,
            YSortEnabled = true,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };

        var businesses = OriginalData.Instance.Businesses;
        var bldngCounter = 0;
        foreach (var vec in map.BlockPlacement)
        {
            var blockPosition = new Vector2I(vec.X, vec.Y);
            if (vec.Z < 0 || vec.Z >= cb.Blocks.Count)
            {
                GD.PrintErr($"TileMapGenerator: {map.FileName} uses unknown block {vec.Z}");
                continue;
            }
            var block = cb.Blocks[vec.Z];

            // Tallest tile in the block decides the atlas row height.
            var counter = block.Pos;
            var maximumY = 0;
            for (var y = 0; y < block.Y; y++)
                for (var x = 0; x < block.X; x++)
                {
                    var tileDesc = ct.Tiles[counter];
                    if (tileDesc.Size.Y > maximumY)
                        maximumY = tileDesc.Size.Y;
                    counter++;
                }

            var tmpImage = Image.CreateEmpty(standartTile.X * block.X, maximumY * block.Y, false, Image.Format.Rgba8);
            counter = block.Pos;
            for (var x = 0; x < block.X; x++)
                for (var y = 0; y < block.Y; y++)
                {
                    var tileDesc = ct.Tiles[counter];
                    var bmpImage = bmps.Bmps[ct.Bmps[tileDesc.BmpInd]].Body;
                    counter++;
                    tmpImage.BlitRect(bmpImage, new Rect2I(tileDesc.Pairs[0], tileDesc.Size),
                        new Vector2I(x * standartTile.X, maximumY - tileDesc.Size.Y + y * maximumY));
                }

            var atlas = new TileSetAtlasSource
            {
                UseTexturePadding = false,
                TextureRegionSize = new Vector2I(standartTile.X, maximumY),
                Texture = ImageTexture.CreateFromImage(tmpImage),
            };

            var centerY = Mathf.FloorToInt(maximumY / 2.0f);
            var centerY2 = Mathf.FloorToInt(standartTile.Y / 2.0f);
            var margin = new Vector2I(0, centerY - centerY2);

            tileSet.AddSource(atlas, bldngCounter);

            // The original block object (fnBuildingPlacementOnMapByNumber): district = BlockTable category.
            var cityBlock = new CityBlock(bldngCounter, vec.Z, block.Category);
            var cellOf = new Dictionary<Vector2I, Vector2I>();

            for (var x = 0; x < block.X; x++)
                for (var y = 0; y < block.Y; y++)
                {
                    var yInverted = block.Y - y - 1;
                    var bldngTilePosition = new Vector2I(x, y);
                    var bldngChar = block.BlockLines[yInverted][x];
                    atlas.CreateTile(bldngTilePosition);
                    var td = atlas.GetTileData(bldngTilePosition, 0);
                    td.TextureOrigin = margin;
                    td.SetCustomData(TileNonWalkable, true);

                    var actualBlockPosition = MapTileMath.Add(tileMap, Vector2I.Zero, blockPosition);
                    var actual = MapTileMath.Add(tileMap, actualBlockPosition, bldngTilePosition);
                    cellOf[new Vector2I(x, yInverted)] = actual;

                    switch (bldngChar)
                    {
                        case 'R':
                        case 'S':
                            td.SetCustomData(TileNonWalkable, false);
                            break;
                        case 'B':
                        case 'C':
                        case 'M':
                        case 'H':
                        {
                            // B: grade 1 and C: grade 2 businesses 3 times in 4 (otherwise a plain building);
                            // M, H: always grade 3. The kind is a random legal catalogue row of that grade
                            // (fnSelectBuildingRandomly; every legal row allows any district).
                            var grade = bldngChar switch { 'B' => 1, 'C' => 2, _ => 3 };
                            if (grade < 3 && GD.Randi() % 4 == 0)
                                break;
                            var entry = businesses.RandomLegal(grade, block.Category);
                            if (entry == null)
                                break;
                            var c = companyScene.Instantiate<Company>();
                            c.InitBusiness(entry, cityBlock, actual);
                            cityBlock.AddCompany(actual, c);
                            td.SetCustomData(CompanyLayerName, c);
                            break;
                        }
                        case 'P':
                        {
                            var c = companyScene.Instantiate<Company>();
                            c.InitPoliceStation(actual);
                            cityBlock.AddCompany(actual, c);
                            td.SetCustomData(CompanyLayerName, c);
                            break;
                        }
                    }

                    tileMap.SetCell(actual, bldngCounter, bldngTilePosition);
                }

            // The collector visits the block row by row (sub_429146: index -> x = i % width, y = i / width).
            for (var yo = 0; yo < block.Y; yo++)
                for (var x = 0; x < block.X; x++)
                    cityBlock.Cells.Add(cellOf[new Vector2I(x, yo)]);

            bldngCounter++;
        }

        var bases = PlaceBases(tileMap, map, baseCount, randomBases, companyScene);

        var nonWalkableMap = new Godot.Collections.Array<Vector2I>();
        foreach (var coords in tileMap.GetUsedCells())
        {
            var td = tileMap.GetCellTileData(coords);
            if (td == null)
                continue;
            if (td.GetCustomData(TileNonWalkable).AsBool())
                nonWalkableMap.Add(coords);

            var company = GetCompany(td);
            if (company == null)
                continue;
            if (company.GetParent() == null)
                (parent ?? (Node)tileMap).AddChild(company);
            company.Position = tileMap.MapToLocal(coords);
            company.ZIndex = 999;
        }

        tileMap.SetMeta(TileNonWalkable, nonWalkableMap);
        tileMap.SetMeta(MetaNameBases, bases);
        return tileMap;
    }

    /// <summary>
    /// Turns the companies at the first <paramref name="baseCount"/> start points (in file order, or shuffled for RANDOM 1)
    /// into HQs. Returns [cell, company] pairs in that order; the caller hands them to the players that have a base.
    /// </summary>
    private static Godot.Collections.Array PlaceBases(TileMapLayer tileMap, OriginalMaps.Map map, int baseCount,
        bool randomBases, PackedScene companyScene)
    {
        var startPoints = new List<Vector2I>();
        foreach (var original in map.StartPoints)
            startPoints.Add(MapTileMath.FromOriginal(tileMap, original));
        if (randomBases)
            Shuffle(startPoints);
        if (baseCount > startPoints.Count)
            GD.PrintErr($"TileMapGenerator: {map.FileName} has {startPoints.Count} start points, {baseCount} bases needed");

        var bases = new Godot.Collections.Array();
        foreach (var startPoint in startPoints)
        {
            if (bases.Count >= baseCount)
                break;
            var coords = startPoint;
            var td = tileMap.GetCellTileData(coords);
            if (td == null)
            {
                GD.PrintErr($"TileMapGenerator: start point {coords} is outside the map");
                continue;
            }

            // The original puts the HQ building (class 9, sub_415090) right on the start point (sub_404098);
            // a business that stood there is replaced.
            var company = GetCompany(td);
            if (company == null)
            {
                company = companyScene.Instantiate<Company>();
                company.InitBuilding(CompanyCommon.CompanyType.HQ, coords);
                td.SetCustomData(CompanyLayerName, company);
            }
            bases.Add(new Godot.Collections.Array { coords, company });
        }
        return bases;
    }

    public static Vector2[] GetStandartTileVertices(Vector2 margin)
    {
        var tile = OriginalData.StandartTile;
        return new[]
        {
            new Vector2(0, tile.Y / 2) + margin,
            new Vector2(tile.X / 2, 0) + margin,
            new Vector2(tile.X, tile.Y / 2) + margin,
            new Vector2(tile.X / 2, tile.Y) + margin,
        };
    }

    private static Company GetCompany(TileData td) =>
        td.GetCustomData(CompanyLayerName).AsGodotObject() as Company;

    private static void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = (int)(GD.Randi() % (uint)(i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
