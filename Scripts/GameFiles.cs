using Godot;

namespace Capone;

/// <summary>
/// Where the files players may edit live (game.cfg, Data/*.json, original.cfg). Run from the project (editor, tests)
/// they are project files, res://&lt;name&gt;. In an exported game they sit next to its executable: the release build
/// (tools/build-release.ps1) copies them there and leaves them out of the game's pack, so players can edit them.
/// </summary>
public static class GameFiles
{
    /// <summary>True in an exported game (Godot's "template" feature), false when run from the project.</summary>
    public static bool Exported => OS.HasFeature("template");

    /// <summary>The folder of the game's executable (exported game), else the project folder.</summary>
    public static string Folder => Exported ? OS.GetExecutablePath().GetBaseDir() : "res://";

    /// <summary>Path of an editable file, e.g. "game.cfg" or "Data/rules.json".</summary>
    public static string PathOf(string relative) => Exported ? Folder.PathJoin(relative) : "res://" + relative;
}
