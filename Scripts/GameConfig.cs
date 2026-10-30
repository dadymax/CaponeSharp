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

    static GameConfig() => Load();

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok)
            return;
        CompanyChance = Mathf.Clamp((float)cfg.GetValue("map", "company_chance", CompanyChance).AsDouble(), 0f, 1f);
        MinOneCompanyInRichBlocks = cfg.GetValue("map", "min_one_company_in_rich_blocks", MinOneCompanyInRichBlocks).AsBool();
    }
}
