using Godot;

namespace Capone;

/// <summary>
/// Tunable rules that differ from (or aren't found in) the EXE, read from res://game.cfg. Missing keys keep the
/// defaults below.
/// </summary>
public static class GameConfig
{
    public const string Path = "res://game.cfg";

    /// <summary>
    /// Chance that a B (grade 1) or C (grade 2) block cell becomes a business; the rest are plain buildings. The EXE
    /// uses 3 in 4 (fnBuildingPlacementOnMapByNumber); raised on the user's request. M and H cells always are.
    /// </summary>
    public static float CompanyChance { get; private set; } = 0.9f;

    /// <summary>Middle-class and center blocks (district 1 and 2) get at least one business (user's rule).</summary>
    public static bool MinOneCompanyInRichBlocks { get; private set; } = true;

    /// <summary>Frames per second of the unit animations by kind (idle / move / attack / killed); Godot's default is 5.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, float> AnimationFpsByKey = new();

    /// <summary>
    /// How long (game minutes) a unit keeps its attack picture after an order's hit (extortion, attacks on buildings),
    /// so it doesn't drop to the standing picture between hits. One game hour = 1 real second.
    /// </summary>
    public static int AttackHoldMinutes { get; private set; } = 20;

    /// <summary>
    /// Frame rate for an animation layer of a unit type: "[animation] Thompson.attack_fps", else "attack_fps", else 5.
    /// <paramref name="layer"/> is a layer name (idle, moveSE…, attackNW…, killed, killed2); the kind is its prefix.
    /// </summary>
    public static float AnimationFps(string unitType, string layer)
    {
        var kind = layer.StartsWith("move") ? "move" : layer.StartsWith("attack") ? "attack" : layer.StartsWith("killed") ? "killed" : "idle";
        if (AnimationFpsByKey.TryGetValue($"{unitType}.{kind}_fps", out var fps) || AnimationFpsByKey.TryGetValue($"{kind}_fps", out fps))
            return fps;
        return 5f;
    }

    static GameConfig() => Load();

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok)
            return;
        CompanyChance = Mathf.Clamp((float)cfg.GetValue("map", "company_chance", CompanyChance).AsDouble(), 0f, 1f);
        MinOneCompanyInRichBlocks = cfg.GetValue("map", "min_one_company_in_rich_blocks", MinOneCompanyInRichBlocks).AsBool();
        AnimationFpsByKey.Clear();
        if (cfg.HasSection("animation"))
            foreach (var key in cfg.GetSectionKeys("animation"))
                if (key.EndsWith("_fps"))
                    AnimationFpsByKey[key] = Mathf.Max(0.1f, (float)cfg.GetValue("animation", key).AsDouble());
        AttackHoldMinutes = Mathf.Max(0, (int)cfg.GetValue("animation", "attack_hold_minutes", AttackHoldMinutes));
    }
}
