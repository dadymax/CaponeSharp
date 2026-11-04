using System.Linq;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Builds the playable TileMapLayer from a .map file, tile blocks and Chicago.tile.</summary>
public static class TileMapGenerator
{
    public static readonly StringName CompanyLayerName = "company";
    public static readonly StringName TileNonWalkable = "nonWalkable";
    /// <summary>Map meta: cells that block sight (building letters B C M H D G P; the ground classes are see-through).</summary>
    public static readonly StringName MetaOpaque = "opaqueCells";
    /// <summary>Road cells (letter R, tile off_47D0BC: everyone may walk, class 2).</summary>
    public static readonly StringName MetaRoad = "roadCells";
    /// <summary>Pavement cells (letter S, tile off_47D230 "sand": no Cadillac, class 2).</summary>
    public static readonly StringName MetaSand = "sandCells";
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
        var opaque = new Godot.Collections.Array<Vector2I>();
        var roads = new Godot.Collections.Array<Vector2I>();
        var sand = new Godot.Collections.Array<Vector2I>();
        // Medium standalone buildings (letter B): original map cell -> map cell.
        var mediumBuildings = new Dictionary<Vector2I, Vector2I>();
        // Block letter of every original cell (the EXE's letter grid, filled by sub_420806).
        var letters = new Dictionary<Vector2I, char>();
        // Building cells without a company (selectable, PlainBuilding): Godot cell → kind code.
        var plainCells = new Godot.Collections.Dictionary<Vector2I, int>();
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
            var flat = new bool[block.X, block.Y];
            counter = block.Pos;
            for (var x = 0; x < block.X; x++)
                for (var y = 0; y < block.Y; y++)
                {
                    var tileDesc = ct.Tiles[counter];
                    flat[x, y] = tileDesc.Size.Y <= standartTile.Y;
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
            var skipped = new List<(int Grade, TileData Data, Vector2I Cell)>();

            void PlaceBusiness(int grade, TileData data, Vector2I at)
            {
                var entry = businesses.RandomLegal(grade, block.Category);
                if (entry == null)
                    return;
                var c = companyScene.Instantiate<Company>();
                c.InitBusiness(entry, cityBlock, at);
                cityBlock.AddCompany(at, c);
                data.SetCustomData(CompanyLayerName, c);
            }
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
                    // Flat ground (a picture one floor tile high) is drawn under everything; only the tall pictures
                    // (buildings, trees, lamps) are sorted by y with the units, so a tile in front can't cover a unit's feet.
                    if (flat[x, y])
                        td.ZIndex = GroundZ;
                    else
                        td.YSortOrigin = -1; // a company (its sign) on the same cell sorts after its building
                    td.SetCustomData(TileNonWalkable, true);

                    // fnBuildingPlacementOnMapByNumber: letter row j, column i → original cell (x + i, y + j); the
                    // picture is record base + i·H + (H − 1 − j) (sub_420790), i.e. atlas (i, H − 1 − j) here.
                    var blockCell = new Vector2I(x, yInverted);
                    var actualBlockPosition = MapTileMath.Add(tileMap, Vector2I.Zero, blockPosition);
                    var actual = MapTileMath.Add(tileMap, actualBlockPosition, blockCell);
                    cellOf[blockCell] = actual;
                    if ("BCMHDGP".IndexOf(bldngChar) >= 0)
                        opaque.Add(actual);
                    letters[blockPosition + blockCell] = bldngChar;
                    if (PlainBuilding.KindOf(bldngChar, block.Category) is { } plainKind)
                        plainCells[actual] = plainKind;
                    if (bldngChar == 'B')
                        mediumBuildings[blockPosition + blockCell] = actual;

                    switch (bldngChar)
                    {
                        // fnBuildingPlacementOnMapByNumber: R road and S "sand" (pavement) are class 2 ground, I is
                        // class 3 ground (sub_420BD0, off_47D514) — all walkable; everything else is not.
                        case 'R':
                            td.SetCustomData(TileNonWalkable, false);
                            roads.Add(actual);
                            break;
                        case 'S':
                            td.SetCustomData(TileNonWalkable, false);
                            sand.Add(actual);
                            break;
                        case 'I':
                            td.SetCustomData(TileNonWalkable, false);
                            break;
                        case 'B':
                        case 'C':
                        case 'M':
                        case 'H':
                        {
                            // B: grade 1 and C: grade 2 businesses with GameConfig.CompanyChance (the EXE: 3 in 4;
                            // otherwise a plain building); M, H: always grade 3. The kind is a random legal catalogue
                            // row of that grade (fnSelectBuildingRandomly; every legal row allows any district).
                            var grade = bldngChar switch { 'B' => 1, 'C' => 2, _ => 3 };
                            if (grade < 3 && GD.Randf() >= GameConfig.CompanyChance)
                            {
                                skipped.Add((grade, td, actual));
                                break;
                            }
                            PlaceBusiness(grade, td, actual);
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

            // User's rule: middle-class and center blocks always have a business (a C cell first, then a B).
            if (GameConfig.MinOneCompanyInRichBlocks && block.Category is 1 or 2 && cityBlock.Companies.Count(c => c.IsEconomic) == 0
                && skipped.Count > 0)
            {
                var pick = skipped.OrderByDescending(x => x.Grade).ThenBy(_ => GD.Randi()).First();
                PlaceBusiness(pick.Grade, pick.Data, pick.Cell);
            }

            // The collector visits the block row by row (sub_429146: index -> x = i % width, y = i / width).
            for (var yo = 0; yo < block.Y; yo++)
                for (var x = 0; x < block.X; x++)
                    cityBlock.Cells.Add(cellOf[new Vector2I(x, yo)]);

            bldngCounter++;
        }

        var bases = PlaceBases(tileMap, UsableStartPoints(map, letters), baseCount, randomBases, companyScene, mediumBuildings);

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
            // Sorted by depth with the units like the original's object list (was ZIndex 999: signs over every unit).
            company.Position = tileMap.MapToLocal(coords);
        }

        tileMap.SetMeta(TileNonWalkable, nonWalkableMap);
        tileMap.SetMeta(MetaOpaque, opaque);
        tileMap.SetMeta(MetaRoad, roads);
        tileMap.SetMeta(MetaSand, sand);
        tileMap.SetMeta(MetaNameBases, bases);
        tileMap.SetMeta(PlainBuilding.MetaCells, plainCells);
        return tileMap;
    }

    /// <summary>Draw layer of flat ground tiles: below units and tall tiles (z 0).</summary>
    public const int GroundZ = -1;

    /// <summary>How far (cells) from a start point the HQ's medium building may be.</summary>
    public const int BaseSearchRadius = 12;

    /// <summary>
    /// The first <paramref name="baseCount"/> start points (in file order, or shuffled for RANDOM 1) get an HQ. An HQ
    /// stands on a medium standalone building — block letter B (user, from the original) — the one nearest to the start
    /// point that no other HQ took; the start points of most maps are one cell off it. Returns [cell, company] pairs in
    /// that order; the caller hands them to the players that have a base.
    /// </summary>
    private static Godot.Collections.Array PlaceBases(TileMapLayer tileMap, List<Vector2I> startPoints, int baseCount,
        bool randomBases, PackedScene companyScene, Dictionary<Vector2I, Vector2I> mediumBuildings)
    {
        // Searched in original coordinates: the isometric map cells are skewed against them.
        if (randomBases)
            Shuffle(startPoints);
        if (baseCount > startPoints.Count)
            GD.PrintErr($"TileMapGenerator: {startPoints.Count} usable start points, {baseCount} bases needed");

        var bases = new Godot.Collections.Array();
        foreach (var startPoint in startPoints)
        {
            if (bases.Count >= baseCount)
                break;
            Vector2I coords;
            if (NearestFree(mediumBuildings, startPoint) is { } original)
            {
                coords = mediumBuildings[original];
                mediumBuildings.Remove(original);
            }
            else
            {
                coords = MapTileMath.FromOriginal(tileMap, startPoint);
                GD.PrintErr($"TileMapGenerator: no medium building (B) near start point {startPoint}");
            }
            var td = tileMap.GetCellTileData(coords);
            if (td == null)
            {
                GD.PrintErr($"TileMapGenerator: start point {coords} is outside the map");
                continue;
            }

            // The HQ building (class 9, sub_415090, made by sub_404098) replaces a business that stood there.
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

    /// <summary>
    /// fnProcessMapLoad (0x403BBF–0x403BE9) drops a STARTPOINTS line whose cell is a road, pavement, F or G cell, so
    /// e.g. SmallNeighborhood's first two lines never get a base (episode 4: Big Jim's base is the third, "to the right").
    /// </summary>
    public static List<Vector2I> UsableStartPoints(OriginalMaps.Map map, IReadOnlyDictionary<Vector2I, char> letters) =>
        map.StartPoints.Where(p => !letters.TryGetValue(p, out var ch) || "RFSG".IndexOf(ch) < 0).ToList();

    private static Vector2I? NearestFree(Dictionary<Vector2I, Vector2I> cells, Vector2I from)
    {
        Vector2I? best = null;
        var bestDistance = BaseSearchRadius * BaseSearchRadius + 1;
        for (var dy = -BaseSearchRadius; dy <= BaseSearchRadius; dy++)
            for (var dx = -BaseSearchRadius; dx <= BaseSearchRadius; dx++)
            {
                var d = dx * dx + dy * dy;
                var cell = from + new Vector2I(dx, dy);
                if (d < bestDistance && cells.ContainsKey(cell))
                {
                    bestDistance = d;
                    best = cell;
                }
            }
        return best;
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

    public static Company GetCompany(TileData td) =>
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
