using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace Capone;

/// <summary>
/// User-editable unit and business parameters (TODO item 18): res://Data/unit_stats.json and res://Data/businesses.json
/// hold the EXE's unit table (0x484030) and business catalogue (0x47DDC0). The game reads the EXE first and then lays
/// these files over it, so a missing file, unit, row or field keeps the original value. Write fresh files with the
/// original values with <c>-- --export-params</c>.
/// </summary>
public static class GameParameters
{
    public static string UnitsPath => GameFiles.PathOf("Data/unit_stats.json");
    public static string BusinessesPath => GameFiles.PathOf("Data/businesses.json");

    /// <summary>Problems found while reading the files (the smoke test reports them).</summary>
    public static List<string> Errors { get; } = new();

    // ---- units ------------------------------------------------------------------------------------------------------

    private static readonly (string Key, string Help)[] UnitFields =
    {
        ("Cost", "price and weekly salary"),
        ("MaxHP", "hit points"),
        ("DamageMin", "damage to units, lowest"),
        ("DamageMax", "damage to units, highest"),
        ("BuildingDamageMin", "damage to buildings and Cadillacs, lowest"),
        ("BuildingDamageMax", "damage to buildings and Cadillacs, highest"),
        ("HitChance", "chance to hit, percent"),
        ("AttackRange", "shooting range in cells"),
        ("FireRate", "shots per game hour; below 1 the unit aims round(1 / rate) hours per shot"),
        ("ReloadMinutes", "game minutes between shots (Shotgun man 300)"),
        ("AttackKind", "1 single target, 2 area attack (Terrorist, Shotgun man)"),
        ("FiresFromCar", "1: may shoot from a Cadillac"),
        ("TicksPerTile", "game minutes per cell walked (speed; Cadillac 30)"),
        ("TrainingMinutes", "training time in game minutes (EXE +24h)"),
        ("Vision", "sight radius while walking"),
        ("VisionStanding", "sight radius while standing (EXE +2Ah)"),
        ("Value1C", "EXE +1Ch, second money value (not used)"),
        ("Value28", "EXE +28h (Thompson man 50, not used)"),
    };

    private static JsonObject UnitToJson(OriginalUnitTable.Stats s) => new()
    {
        ["Cost"] = s.Cost, ["MaxHP"] = s.MaxHP, ["DamageMin"] = s.DamageMin, ["DamageMax"] = s.DamageMax,
        ["BuildingDamageMin"] = s.BuildingDamageMin, ["BuildingDamageMax"] = s.BuildingDamageMax,
        ["HitChance"] = s.HitChance, ["AttackRange"] = s.AttackRange, ["FireRate"] = s.FireRate,
        ["ReloadMinutes"] = s.ReloadMinutes, ["AttackKind"] = s.AttackKind, ["FiresFromCar"] = s.FiresFromCar,
        ["TicksPerTile"] = s.TicksPerTile, ["TrainingMinutes"] = s.Value24, ["Vision"] = s.Vision,
        ["VisionStanding"] = s.VisionMax, ["Value1C"] = s.Value1C, ["Value28"] = s.Value28,
    };

    private static void ApplyUnit(OriginalUnitTable.Stats s, JsonObject o, string where)
    {
        foreach (var (key, value) in o)
        {
            if (key.StartsWith('_') || value == null)
                continue;
            try
            {
                switch (key)
                {
                    case "Cost": s.Cost = value.GetValue<int>(); break;
                    case "MaxHP": s.MaxHP = value.GetValue<int>(); break;
                    case "DamageMin": s.DamageMin = value.GetValue<int>(); break;
                    case "DamageMax": s.DamageMax = value.GetValue<int>(); break;
                    case "BuildingDamageMin": s.BuildingDamageMin = value.GetValue<int>(); break;
                    case "BuildingDamageMax": s.BuildingDamageMax = value.GetValue<int>(); break;
                    case "HitChance": s.HitChance = value.GetValue<int>(); break;
                    case "AttackRange": s.AttackRange = (float)value.GetValue<double>(); break;
                    case "FireRate": s.FireRate = (float)value.GetValue<double>(); break;
                    case "ReloadMinutes": s.ReloadMinutes = value.GetValue<int>(); break;
                    case "AttackKind": s.AttackKind = value.GetValue<int>(); break;
                    case "FiresFromCar": s.FiresFromCar = value.GetValue<int>(); break;
                    case "TicksPerTile": s.TicksPerTile = value.GetValue<int>(); break;
                    case "TrainingMinutes": s.Value24 = value.GetValue<int>(); break;
                    case "Vision": s.Vision = value.GetValue<int>(); break;
                    case "VisionStanding": s.VisionMax = value.GetValue<int>(); break;
                    case "Value1C": s.Value1C = value.GetValue<int>(); break;
                    case "Value28": s.Value28 = value.GetValue<int>(); break;
                    default: Errors.Add($"{where}: unknown field {key}"); break;
                }
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException)
            {
                Errors.Add($"{where}.{key}: not a number ({value.ToJsonString()})");
            }
        }
    }

    private static string UnitKey(int type) =>
        Enum.IsDefined(typeof(UnitCommon.UnitType), type) ? ((UnitCommon.UnitType)type).ToString() : $"Type{type}";

    /// <summary>Called by <see cref="OriginalUnitTable"/> after reading the EXE.</summary>
    public static void ApplyUnits(IReadOnlyDictionary<int, OriginalUnitTable.Stats> stats)
    {
        if (Read(UnitsPath) is not { } root)
            return;
        var units = root["Units"] as JsonObject;
        if (units == null)
        {
            Errors.Add($"{UnitsPath}: no \"Units\" object");
            return;
        }
        foreach (var (key, node) in units)
        {
            var match = stats.Values.FirstOrDefault(s => UnitKey(s.Type) == key);
            if (match == null)
                Errors.Add($"{UnitsPath}: unknown unit {key}");
            else if (node is JsonObject o)
                ApplyUnit(match, o, $"{UnitsPath}: {key}");
        }
    }

    // ---- businesses --------------------------------------------------------------------------------------------------

    private static JsonObject RowToJson(OriginalBusinessTable.Entry e) => new()
    {
        ["Index"] = e.Index, ["Name"] = e.Name, ["Type"] = e.Type.ToString(), ["Grade"] = e.Grade, ["Price"] = e.Price,
        ["Income"] = e.Income, ["MaxHP"] = e.MaxHP, ["BuildMinutes"] = e.Value1C, ["District"] = e.Value24,
        ["Kind"] = e.Value28, ["Value18"] = e.Value18, ["Value20"] = e.Value20, ["Flag2C"] = e.Flag2C,
    };

    private static void ApplyRow(OriginalBusinessTable.Entry e, JsonObject o, string where)
    {
        foreach (var (key, value) in o)
        {
            if (key.StartsWith('_') || key == "Index" || value == null)
                continue;
            try
            {
                switch (key)
                {
                    case "Name": e.Name = value.GetValue<string>(); break;
                    case "Type":
                        if (Enum.TryParse<OriginalBusinessTable.BusinessType>(value.GetValue<string>(), out var t))
                            e.Type = t;
                        else
                            Errors.Add($"{where}.Type: unknown type {value}");
                        break;
                    case "Grade": e.Grade = value.GetValue<int>(); break;
                    case "Price": e.Price = value.GetValue<int>(); break;
                    case "Income": e.Income = value.GetValue<int>(); break;
                    case "MaxHP": e.MaxHP = value.GetValue<int>(); break;
                    case "BuildMinutes": e.Value1C = value.GetValue<int>(); break;
                    case "District": e.Value24 = value.GetValue<int>(); break;
                    case "Kind": e.Value28 = value.GetValue<int>(); break;
                    case "Value18": e.Value18 = value.GetValue<int>(); break;
                    case "Value20": e.Value20 = value.GetValue<int>(); break;
                    case "Flag2C": e.Flag2C = value.GetValue<int>(); break;
                    default: Errors.Add($"{where}: unknown field {key}"); break;
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                Errors.Add($"{where}.{key}: wrong value ({value.ToJsonString()})");
            }
        }
    }

    /// <summary>Called by <see cref="OriginalBusinessTable"/> after reading the EXE.</summary>
    public static void ApplyBusinesses(IReadOnlyList<OriginalBusinessTable.Entry> entries)
    {
        if (Read(BusinessesPath) is not { } root)
            return;
        if (root["Rows"] is not JsonArray rows)
        {
            Errors.Add($"{BusinessesPath}: no \"Rows\" array");
            return;
        }
        foreach (var node in rows)
        {
            if (node is not JsonObject o || o["Index"] is not { } indexNode)
            {
                Errors.Add($"{BusinessesPath}: a row without \"Index\"");
                continue;
            }
            var index = indexNode.GetValue<int>();
            if (index < 0 || index >= entries.Count)
                Errors.Add($"{BusinessesPath}: row {index} out of range");
            else
                ApplyRow(entries[index], o, $"{BusinessesPath}: row {index}");
        }
    }

    // ---- files -------------------------------------------------------------------------------------------------------

    public static bool UnitsFileLoaded { get; private set; }
    public static bool BusinessesFileLoaded { get; private set; }

    private static JsonNode Read(string path)
    {
        if (!FileAccess.FileExists(path))
            return null;
        try
        {
            var node = JsonNode.Parse(FileAccess.GetFileAsString(path),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (path == UnitsPath)
                UnitsFileLoaded = true;
            else
                BusinessesFileLoaded = true;
            return node;
        }
        catch (JsonException e)
        {
            Errors.Add($"{path}: {e.Message}");
            GD.PrintErr($"GameParameters: {path} can't be read ({e.Message}) — the original values are used.");
            return null;
        }
    }

    /// <summary>Writes both files with the current values (the EXE's, plus whatever the files already changed).</summary>
    public static void Export()
    {
        var units = new JsonObject();
        for (var type = 0; type < OriginalUnitTable.RecordCount; type++)
            if (OriginalUnitTable.Instance.Get(type) is { } s)
                units[UnitKey(type)] = UnitToJson(s);
        var unitHelp = new JsonObject();
        foreach (var (key, help) in UnitFields)
            unitHelp[key] = help;
        Write(UnitsPath, new JsonObject
        {
            ["_comment"] = "Unit stats, from Capone.EXE's unit table (0x484030); edit and restart the game. A missing unit or field keeps the original value. Regenerate with -- --export-params.",
            ["_fields"] = unitHelp,
            ["Units"] = units,
        });
        var rows = new JsonArray();
        foreach (var e in OriginalBusinessTable.Instance.Entries)
            rows.Add(RowToJson(e));
        Write(BusinessesPath, new JsonObject
        {
            ["_comment"] = "Business catalogue, from Capone.EXE (0x47DDC0); rows 0-41 legal companies, 42-159 businesses. Edit and restart. A missing row or field keeps the original value. Regenerate with -- --export-params.",
            ["_fields"] = new JsonObject
            {
                ["Type"] = "Legal, Bootlegging, Entertainment, Gambling, Troubleshooting, Support",
                ["Grade"] = "1 slum, 2 middle class, 3 center, 4 top (0 any, legal rows)",
                ["Price"] = "price of setting the business up (legal companies 0)",
                ["Income"] = "weekly income; negative = upkeep (support businesses)",
                ["MaxHP"] = "hit points",
                ["BuildMinutes"] = "game minutes the conversion takes (EXE +1Ch)",
                ["District"] = "where a legal row may stand: 0 slum, 1 middle class, 2 center, 3 any (EXE +24h)",
                ["Kind"] = "support kind: 3 cash-out, 4 training center, 7 fortification (EXE +28h)",
                ["Value18"] = "EXE +18h", ["Value20"] = "EXE +20h", ["Flag2C"] = "1: also needs a politician service",
            },
            ["Rows"] = rows,
        });
        Write(GameRules.Path, GameRules.ToJson());
    }

    private static void Write(string path, JsonNode node)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path.GetBaseDir()));
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file?.StoreString(node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n");
        GD.Print($"GameParameters: wrote {path}");
    }
}
