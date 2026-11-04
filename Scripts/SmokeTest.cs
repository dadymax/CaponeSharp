using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Checks that everything is read correctly from the original game files.
/// Run: Godot --headless --path . -- --smoke-test
/// </summary>
public static class SmokeTest
{
    /// <summary>Prices from disassembled/orig_unit_handheld_data.txt, to check the unit table is read at the right place.</summary>
    private static readonly Dictionary<UnitCommon.UnitType, int> KnownCosts = new()
    {
        [UnitCommon.UnitType.Thug] = 20000,
        [UnitCommon.UnitType.Pistol] = 60000,
        [UnitCommon.UnitType.Sniper] = 110000,
        [UnitCommon.UnitType.Shotgun] = 90000,
        [UnitCommon.UnitType.Hitman] = 100000,
        [UnitCommon.UnitType.Thief] = 150000,
        [UnitCommon.UnitType.Terrorist] = 90000,
        [UnitCommon.UnitType.Thompson] = 130000,
        [UnitCommon.UnitType.Collector] = 5000,
    };

    private static int _failures;
    private static int _warnings;

    /// <summary>res://game.cfg must parse; a syntax error makes Godot drop the whole file (TODO playtest item 15).</summary>
    private static void CheckGameConfig()
    {
        var cfg = new ConfigFile();
        var error = cfg.Load(GameConfig.Path);
        if (error != Error.Ok)
            Fail($"game.cfg doesn't load: {error}");
        else
            GD.Print($"  game.cfg ok: pathfinding {GameConfig.Pathfinding}, company_chance {GameConfig.CompanyChance}");
    }

    /// <summary>Returns the number of failures.</summary>
    public static int Run()
    {
        _failures = 0;
        _warnings = 0;
        GD.Print("=== Smoke test: original data ===");
        CheckGameConfig();
        CheckExe();
        CheckUnits();
        CheckBusinessesAndTexts();
        CheckMaps();
        CheckEpisodes();
        GD.Print($"=== Smoke test done: {_failures} failure(s), {_warnings} warning(s) ===");
        return _failures;
    }

    private static void CheckExe()
    {
        var data = OriginalData.Instance;
        Check(data.Exe.IsLoaded, "Capone.EXE loaded");
        var blocks = data.BuildingBlocks.Blocks;
        Check(blocks.Count == OriginalBlocks.BlockCount, $"{blocks.Count} tile blocks read from the EXE");
        var tileCount = data.Tiles.Tiles.Count;
        foreach (var b in blocks)
        {
            if (b.BlockLines.Count != b.Y || b.BlockLines.Any(row => row.Length != b.X))
                Fail($"block {b.Number}: rows don't match {b.X}x{b.Y}");
            if (b.Pos < 0 || b.Pos + b.X * b.Y > tileCount)
                Fail($"block {b.Number}: tiles {b.Pos}..{b.Pos + b.X * b.Y} outside Chicago.tile ({tileCount})");
        }
    }

    private static void CheckUnits()
    {
        var table = OriginalUnitTable.Instance;
        foreach (UnitCommon.UnitType type in System.Enum.GetValues<UnitCommon.UnitType>())
        {
            var stats = table.Get((int)type);
            if (stats == null)
            {
                Fail($"no EXE stats for {type}");
                continue;
            }
            var name = OriginalUnitTable.GetName((int)type, OriginalData.TextLanguage);
            GD.Print($"  {(int)type,2} {type,-10} {name,-10} cost {stats.Cost,6} hp {stats.MaxHP,3} dmg {stats.DamageMin}-{stats.DamageMax}/{stats.BuildingDamageMin}-{stats.BuildingDamageMax} hit {stats.HitChance}% range {stats.AttackRange} ticks {stats.TicksPerTile} vision {stats.Vision}/{stats.VisionMax}");
            if (KnownCosts.TryGetValue(type, out var cost) && stats.Cost != cost)
                Fail($"{type}: EXE cost {stats.Cost}, expected {cost}");
            if (string.IsNullOrEmpty(name) && type != UnitCommon.UnitType.PoliceCar)
                Warn($"{type}: no name in the EXE");
        }
        foreach (var missing in UnitCommon.ValidateSprites())
            Fail("units.json sprite not in Chicago.tile: " + missing);
    }

    private static void CheckBusinessesAndTexts()
    {
        var table = OriginalBusinessTable.Instance;
        Check(table.Entries.Count == OriginalBusinessTable.RecordCount, $"{table.Entries.Count} business catalogue records");
        var beerBar = table.Get(43);
        Check(beerBar is { Price: 75000, Income: 92000, Type: OriginalBusinessTable.BusinessType.Bootlegging, Grade: 1 },
            $"catalogue row 43 = {beerBar}");
        foreach (var (grade, income) in new[] { (1, 25200), (2, 37800), (3, 50400) })
        {
            var legal = table.LegalOfGrade(grade).ToList();
            Check(legal.Count > 0 && legal.All(e => e.Income == income && e.Price == 0),
                $"{legal.Count} legal companies of grade {grade}, income {income}");
        }
        var byType = table.Entries.GroupBy(e => e.Type).Select(g => $"{g.Key} {g.Count()}");
        GD.Print("  catalogue: ", string.Join(", ", byType));

        var texts = OriginalTexts.Instance;
        Check(texts.Get(5, 1, OriginalExe.Language.English) == "Thug", "text (5, 1) EN = Thug");
        Check(texts.Get(5, 1, OriginalExe.Language.Russian) == "Вышибала", "text (5, 1) RU = Вышибала");
        var all = texts.All(OriginalExe.Language.English).ToList();
        var unreadable = all.Count(t => t.Text == OriginalTexts.MissingText);
        var untranslated = all.Count(t => !texts.HasTranslation(t.Group, t.Index, OriginalExe.Language.Russian));
        Check(unreadable == 0, $"{all.Count} texts in {texts.GroupCount} groups, {unreadable} unreadable, {untranslated} without Russian (English used)");
    }

    private static void CheckMaps()
    {
        var maps = OriginalData.Instance.Maps.Maps;
        Check(maps.Count > 0, $"{maps.Count} maps found");
        var blockCount = OriginalData.Instance.BuildingBlocks.Blocks.Count;
        foreach (var map in maps.Values.OrderBy(m => m.FileName))
        {
            foreach (var error in map.Errors)
                Fail(error);
            foreach (var p in map.BlockPlacement)
                if (p.Z < 0 || p.Z >= blockCount)
                    Fail($"{map.FileName}: unknown block {p.Z} at {p.X},{p.Y}");
            if (map.StartPoints.Count == 0)
                Warn($"{map.FileName}: no start points");

            var tileMap = TileMapGenerator.CreateTileMap(map.FileName, 0);
            if (tileMap == null)
            {
                Fail($"{map.FileName}: map could not be built");
                continue;
            }
            GD.Print($"  {map.FileName,-24} {map.Ver} {map.Dimensions.X}x{map.Dimensions.Y} blocks {map.BlockPlacement.Count,4} start points {map.StartPoints.Count} cells {tileMap.GetUsedCells().Count}");
            tileMap.Free();
        }
    }

    private static void CheckEpisodes()
    {
        var files = CampaignManager.FindEpisodeFiles();
        Check(files.Count > 0, $"{files.Count} episodes found");
        var maps = OriginalData.Instance.Maps.Maps;
        var parser = new EpiParser();
        foreach (var path in files)
        {
            var s = parser.Parse(path);
            foreach (var error in s.Errors)
                Fail(error);
            var file = System.IO.Path.GetFileName(path);
            GD.Print($"  {file,-9} '{s.ScenarioName}' map {s.MapFile} players {s.Players.Count} vcond {s.VictoryConditions.Count} units {s.StartingUnits.Count} bribes {s.BribeAdvances.Count}");

            if (!maps.TryGetValue(s.MapFile.ToUpperInvariant(), out var map))
            {
                Fail($"{file}: map {s.MapFile} not found");
                continue;
            }
            if (!s.Players.Any(p => p.IsHuman))
                Fail($"{file}: no human player (kind 0)");
            var bases = s.Players.Count(p => p.HasBase);
            if (bases > map.StartPoints.Count)
                Fail($"{file}: {bases} bases but {map.StartPoints.Count} start points on {s.MapFile}");
            var playerIds = s.Players.Select(p => p.Id).ToHashSet();
            foreach (var u in s.StartingUnits)
            {
                if (!playerIds.Contains(u.PlayerId))
                    Fail($"{file}: unit for unknown player {u.PlayerId}");
                if (u.Position.X < 0 || u.Position.Y < 0 || u.Position.X >= map.Dimensions.X || u.Position.Y >= map.Dimensions.Y)
                    Fail($"{file}: unit at {u.Position} outside the {map.Dimensions} map");
            }
            foreach (var vc in s.VictoryConditions)
            {
                var condition = new VictoryCondition(vc);
                if (condition.OriginalUnitType >= 0 && condition.UnitType == null)
                    Warn($"{file}: VCOND {vc.ConditionType} uses unit type {condition.OriginalUnitType} (not a table entry)");
            }
            if (string.IsNullOrEmpty(s.ScenarioName) || string.IsNullOrEmpty(s.ObjectiveText))
                Warn($"{file}: missing SCENARIONAME or OBJECTIVETEXT");
        }
    }

    private static void Check(bool ok, string what)
    {
        if (ok)
            GD.Print("  ok: ", what);
        else
            Fail(what);
    }

    private static void Fail(string message)
    {
        _failures++;
        GD.PrintErr("  FAIL: ", message);
    }

    private static void Warn(string message)
    {
        _warnings++;
        GD.Print("  warning: ", message);
    }
}
