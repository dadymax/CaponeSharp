using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace Capone;

/// <summary>
/// Rule numbers from the EXE (and a few remake rules) that the user may tune in res://Data/rules.json (TODO item 18,
/// part 2). Defaults are the original values; a missing file or key keeps them. Written by <c>-- --export-params</c>.
/// </summary>
public static class GameRules
{
    public const string Path = "res://Data/rules.json";

    // Defaults: the original values (see the classes that use them for the EXE addresses).
    public static int HqMaxHp { get; private set; } = 1300;
    public static int CompanyHealPerHour { get; private set; } = 5;
    public static float SurrenderHpRatio { get; private set; } = 0.15f;
    public static int FortificationRange { get; private set; } = 4;
    public static int FortificationDamageMin { get; private set; } = 10;
    public static int FortificationDamageMax { get; private set; } = 15;
    public static int FortificationHitChance { get; private set; } = 90;
    public static int ExposedHitmanDamageMin { get; private set; } = 20;
    public static int ExposedHitmanDamageMax { get; private set; } = 30;

    public static (int Share, float Factor)[] MarketShareSteps { get; private set; } =
    {
        (0, 1.0f), (10, 1.11f), (20, 1.15f), (30, 1.33f), (40, 1.37f), (50, 1.56f),
        (60, 1.61f), (70, 1.64f), (80, 1.85f), (90, 1.91f), (100, 2.25f),
    };

    public static float[] LegalizationFactors { get; private set; } = { 1.0f, 0.75f, 0.5f, 0.25f };

    public static (long Income, int Chance)[] ExtortChance { get; private set; } =
        { (0, 100), (25_000, 90), (100_000, 15), (1_000_000, 0), (int.MaxValue, 0) };

    public static List<string> Errors { get; } = new();
    public static bool FileLoaded { get; private set; }

    static GameRules() => Load();

    public static void Load()
    {
        if (!FileAccess.FileExists(Path))
            return;
        JsonNode root;
        try
        {
            root = JsonNode.Parse(FileAccess.GetFileAsString(Path),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            Errors.Add($"{Path}: {e.Message}");
            GD.PrintErr($"GameRules: {Path} can't be read ({e.Message}) — the original values are used.");
            return;
        }
        FileLoaded = true;
        if (root is not JsonObject o)
            return;
        foreach (var (key, value) in o)
        {
            if (key.StartsWith('_') || value == null)
                continue;
            try
            {
                switch (key)
                {
                    case nameof(HqMaxHp): HqMaxHp = value.GetValue<int>(); break;
                    case nameof(CompanyHealPerHour): CompanyHealPerHour = value.GetValue<int>(); break;
                    case nameof(SurrenderHpRatio): SurrenderHpRatio = (float)value.GetValue<double>(); break;
                    case nameof(FortificationRange): FortificationRange = value.GetValue<int>(); break;
                    case nameof(FortificationDamageMin): FortificationDamageMin = value.GetValue<int>(); break;
                    case nameof(FortificationDamageMax): FortificationDamageMax = value.GetValue<int>(); break;
                    case nameof(FortificationHitChance): FortificationHitChance = value.GetValue<int>(); break;
                    case nameof(ExposedHitmanDamageMin): ExposedHitmanDamageMin = value.GetValue<int>(); break;
                    case nameof(ExposedHitmanDamageMax): ExposedHitmanDamageMax = value.GetValue<int>(); break;
                    case nameof(MarketShareSteps):
                        MarketShareSteps = value.AsArray().Select(n => (n![0]!.GetValue<int>(), (float)n[1]!.GetValue<double>()))
                            .OrderBy(p => p.Item1).ToArray();
                        break;
                    case nameof(LegalizationFactors):
                        LegalizationFactors = value.AsArray().Select(n => (float)n!.GetValue<double>()).ToArray();
                        break;
                    case nameof(ExtortChance):
                        ExtortChance = value.AsArray().Select(n => (n![0]!.GetValue<long>(), n[1]!.GetValue<int>()))
                            .OrderBy(p => p.Item1).ToArray();
                        break;
                    default: Errors.Add($"{Path}: unknown key {key}"); break;
                }
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or NullReferenceException
                                          or IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                Errors.Add($"{Path}: {key}: wrong value ({value.ToJsonString()})");
            }
        }
        if (LegalizationFactors.Length == 0)
            LegalizationFactors = new[] { 1f };
    }

    public static JsonObject ToJson() => new()
    {
        ["_comment"] = "Game rule numbers (original values from Capone.EXE). Edit and restart; a missing key keeps the original. Regenerate with -- --export-params.",
        ["_fields"] = new JsonObject
        {
            [nameof(HqMaxHp)] = "hit points of a headquarters building",
            [nameof(CompanyHealPerHour)] = "hit points an unowned or legal company heals per game hour",
            [nameof(SurrenderHpRatio)] = "a company gives in to extortion below this share of its hit points",
            [nameof(FortificationRange)] = "sight and shooting range of the fortification business",
            [nameof(FortificationDamageMin)] = "fortification damage, lowest",
            [nameof(FortificationDamageMax)] = "fortification damage, highest",
            [nameof(FortificationHitChance)] = "fortification chance to hit, percent",
            [nameof(ExposedHitmanDamageMin)] = "Hit man damage right after a shot (not hidden), lowest",
            [nameof(ExposedHitmanDamageMax)] = "Hit man damage right after a shot, highest",
            [nameof(MarketShareSteps)] = "[market share %, income multiplier]: the last step at or below the share applies",
            [nameof(LegalizationFactors)] = "income multiplier of a business type by its legalization level 0..3",
            [nameof(ExtortChance)] = "[weekly income, chance %]: extortion base chance, straight lines in between",
        },
        [nameof(HqMaxHp)] = HqMaxHp,
        [nameof(CompanyHealPerHour)] = CompanyHealPerHour,
        [nameof(SurrenderHpRatio)] = SurrenderHpRatio,
        [nameof(FortificationRange)] = FortificationRange,
        [nameof(FortificationDamageMin)] = FortificationDamageMin,
        [nameof(FortificationDamageMax)] = FortificationDamageMax,
        [nameof(FortificationHitChance)] = FortificationHitChance,
        [nameof(ExposedHitmanDamageMin)] = ExposedHitmanDamageMin,
        [nameof(ExposedHitmanDamageMax)] = ExposedHitmanDamageMax,
        [nameof(MarketShareSteps)] = new JsonArray(MarketShareSteps.Select(p => (JsonNode)new JsonArray(p.Share, p.Factor)).ToArray()),
        [nameof(LegalizationFactors)] = new JsonArray(LegalizationFactors.Select(f => (JsonNode)f).ToArray()),
        [nameof(ExtortChance)] = new JsonArray(ExtortChance.Select(p => (JsonNode)new JsonArray(p.Income, p.Chance)).ToArray()),
    };
}
