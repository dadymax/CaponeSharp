using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Entry point to everything loaded from the original game folder (path comes from original.cfg).</summary>
public class OriginalData
{
    public static readonly Vector2I StandartTile = new(40, 19);

    /// <summary>Language of texts taken from Capone.EXE (the Russian edition's .epi files are Russian only).</summary>
    public static OriginalExe.Language TextLanguage { get; set; } = OriginalExe.Language.Russian;

    private static OriginalData _instance;
    public static OriginalData Instance => _instance ??= new OriginalData();

    public string OriginalGameDirectory { get; }

    public OriginalTiles Tiles => OriginalTiles.Instance;
    public OriginalMaps Maps => OriginalMaps.Instance;
    public OriginalBmps Bmps => OriginalBmps.Instance;
    public OriginalBlocks BuildingBlocks => OriginalBlocks.Instance;
    public OriginalExe Exe => OriginalExe.Instance;
    public OriginalUnitTable UnitTable => OriginalUnitTable.Instance;
    public OriginalBusinessTable Businesses => OriginalBusinessTable.Instance;
    public OriginalTexts Texts => OriginalTexts.Instance;

    private OriginalData()
    {
        OriginalGameDirectory = FindGameFolder();
    }

    /// <summary>
    /// Where original.cfg may be, in order: run from the project res://originalData/original.cfg, in an exported game
    /// original.cfg next to the executable; then user://original.cfg (where the folder dialog saves when the game's
    /// own folder can't be written).
    /// </summary>
    private static string[] ConfigPaths => new[]
    {
        GameFiles.Exported ? GameFiles.PathOf("original.cfg") : "res://originalData/original.cfg",
        "user://original.cfg",
    };

    /// <summary>True when the folder holds the original game (the files every start needs).</summary>
    public static bool IsGameFolder(string dir) => !string.IsNullOrEmpty(dir)
        && FileAccess.FileExists(dir.PathJoin("Capone.EXE")) && FileAccess.FileExists(dir.PathJoin("Chicago.tile"));

    /// <summary>
    /// The original game folder from the first original.cfg naming one that holds the game; an exported game put
    /// into the Capone folder itself needs no original.cfg. "" when none is found (<see cref="Boot"/> then asks).
    /// </summary>
    private static string FindGameFolder()
    {
        foreach (var path in ConfigPaths)
        {
            var cfg = new ConfigFile();
            if (cfg.Load(path) != Error.Ok)
                continue;
            var dir = cfg.GetValue("directory", "originalGameDirectory", "").AsString();
            if (IsGameFolder(dir))
                return dir;
            GD.PrintErr($"OriginalData: {path} names \"{dir}\", which has no Capone.EXE and Chicago.tile");
        }
        if (GameFiles.Exported && IsGameFolder(GameFiles.Folder))
            return GameFiles.Folder;
        GD.PrintErr("OriginalData: the original Capone folder isn't set (original.cfg: [directory] originalGameDirectory)");
        return "";
    }

    /// <summary>
    /// Uses the chosen original game folder from now on and saves it to the first original.cfg that can be written.
    /// Returns false when the folder doesn't hold the game.
    /// </summary>
    public static bool SetGameFolder(string dir)
    {
        dir = dir?.Replace('\\', '/');
        if (!IsGameFolder(dir))
            return false;
        var cfg = new ConfigFile();
        cfg.SetValue("directory", "originalGameDirectory", dir);
        foreach (var path in ConfigPaths)
            if (cfg.Save(path) == Error.Ok)
            {
                GD.Print($"OriginalData: original game folder {dir}, saved to {path}");
                break;
            }
        _instance = null;
        return true;
    }

    /// <summary>Cuts every frame of a Chicago.tile record out of its bitmap.</summary>
    public List<Image> GetImageForTile(OriginalTiles.SingleTileLine tile)
    {
        var result = new List<Image>();
        if (tile == null)
            return result;

        var bmpName = Tiles.Bmps[tile.BmpInd];
        var bmpImage = Bmps.Bmps[bmpName].Body;
        foreach (var pair in tile.Pairs)
        {
            var image = Image.CreateEmpty(tile.Size.X, tile.Size.Y, false, Image.Format.Rgba8);
            image.BlitRect(bmpImage, new Rect2I(pair, tile.Size), Vector2I.Zero);
            result.Add(image);
        }
        return result;
    }
}
