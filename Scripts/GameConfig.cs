using Godot;

namespace Capone;

/// <summary>
/// Tunable rules that differ from (or aren't found in) the EXE, read from game.cfg (<see cref="GameFiles"/>). Missing keys keep the
/// defaults below.
/// </summary>
public static class GameConfig
{
    public static string Path => GameFiles.PathOf("game.cfg");

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

    /// <summary>"[ai] brownouts": computer players order brownouts on their main enemy (remake rule; off = the original).</summary>
    public static bool AiBrownouts { get; set; } = true;

    /// <summary>
    /// "[ai] politician_share": percent of a computer player's profit moved from savings to politician bribes, so it can
    /// afford brownouts (not in the original, whose computer players never bribe politicians nor use services).
    /// </summary>
    public static int AiPoliticianShare { get; private set; } = 20;

    /// <summary>"[ai] brownout_days": a computer player orders at most one brownout per this many game days; 0 = never.</summary>
    public static int AiBrownoutDays { get; private set; } = 7;

    /// <summary>Result of loading game.cfg (the smoke test checks it is Ok).</summary>
    public static Error LoadError { get; private set; } = Error.Ok;

    public enum PathfindingMode { Original, AStar }

    /// <summary>
    /// "[movement] pathfinding": original (default) — the EXE's greedy walk with detours (<see cref="MoveMap"/>);
    /// astar — <see cref="PathSearch"/> picks each step from the cheapest whole path (not in the original).
    /// </summary>
    public static PathfindingMode Pathfinding { get; set; } = PathfindingMode.Original;

    /// <summary>
    /// "[movement] cars_on_forecourts": may Cadillacs and police cars drive on the I-ground forecourts next to some
    /// tall buildings? The EXE allows it (sub_420C80); default false — remake rule, they look like pavement.
    /// </summary>
    public static bool CarsOnForecourts { get; set; }

    static GameConfig() => Load();

    public static void Load()
    {
        var cfg = new ConfigFile();
        LoadError = cfg.Load(Path);
        if (LoadError == Error.Ok)
            Read(cfg);
        else if (LoadError != Error.FileNotFound)
            GD.PrintErr($"GameConfig: {Path} can't be read ({LoadError}) — every setting uses its default. "
                        + "Check the line in the error above; text values need quotes, e.g. pathfinding=\"astar\".");
        // Command-line override for test runs: -- --pathfinding=astar|original
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--pathfinding="))
                Pathfinding = ParsePathfinding(arg["--pathfinding=".Length..]);
        if (Pathfinding == PathfindingMode.AStar)
            GD.Print("GameConfig: path search A*");
    }

    private static PathfindingMode ParsePathfinding(string value) =>
        value.Trim().ToLowerInvariant() is "astar" or "a*" or "a-star" ? PathfindingMode.AStar : PathfindingMode.Original;

    private static void Read(ConfigFile cfg)
    {
        CompanyChance = Mathf.Clamp((float)cfg.GetValue("map", "company_chance", CompanyChance).AsDouble(), 0f, 1f);
        MinOneCompanyInRichBlocks = cfg.GetValue("map", "min_one_company_in_rich_blocks", MinOneCompanyInRichBlocks).AsBool();
        AnimationFpsByKey.Clear();
        if (cfg.HasSection("animation"))
            foreach (var key in cfg.GetSectionKeys("animation"))
                if (key.EndsWith("_fps"))
                    AnimationFpsByKey[key] = Mathf.Max(0.1f, (float)cfg.GetValue("animation", key).AsDouble());
        AttackHoldMinutes = Mathf.Max(0, (int)cfg.GetValue("animation", "attack_hold_minutes", AttackHoldMinutes));
        Pathfinding = ParsePathfinding(cfg.GetValue("movement", "pathfinding", "original").AsString());
        CarsOnForecourts = cfg.GetValue("movement", "cars_on_forecourts", CarsOnForecourts).AsBool();
        AiBrownouts = cfg.GetValue("ai", "brownouts", AiBrownouts).AsBool();
        AiPoliticianShare = Mathf.Clamp((int)cfg.GetValue("ai", "politician_share", AiPoliticianShare), 0, 100);
        AiBrownoutDays = Mathf.Max(0, (int)cfg.GetValue("ai", "brownout_days", AiBrownoutDays));
    }
}
