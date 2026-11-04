using Godot;

namespace Capone;

/// <summary>
/// First scene (boot.tscn). The main scene's panels read the original pictures as soon as they enter the tree, so the
/// original game folder must be known before it loads. With a valid folder (<see cref="OriginalData"/>) the game goes
/// straight on to node_2d.tscn. Otherwise a folder dialog asks for it and saves it to original.cfg; cancelling quits.
/// A headless run without the folder fails with exit code 2.
/// </summary>
public partial class Boot : Node
{
    public const string MainScene = "res://node_2d.tscn";

    /// <summary>The release version, project setting application/config/version (also the executable's file version).</summary>
    public static string Version => ProjectSettings.GetSetting("application/config/version", "dev").AsString();

    private const string Explanation =
        "Для игры нужны файлы оригинальной игры Capone (1997).\nУкажите её папку — ту, где лежат Capone.EXE и Chicago.tile.\n\n"
        + "Capone Reborn needs the files of the original Capone (1997).\nChoose its folder, the one with Capone.EXE and Chicago.tile.";

    public override void _Ready()
    {
        GD.Print($"Capone Reborn {Version}");
        if (DisplayServer.GetName() != "headless")
            GetWindow().Title = $"Capone Reborn {Version}";
        if (!string.IsNullOrEmpty(OriginalData.Instance.OriginalGameDirectory))
        {
            GoOn();
            return;
        }
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("Capone Reborn: the original game folder isn't set. Put it into original.cfg: "
                        + "[directory] originalGameDirectory=\"C:/Games/Capone\"");
            GetTree().Quit(2);
            return;
        }
        var screen = new ColorRect { Color = Colors.Black };
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(screen);
        var text = new Label
        {
            Text = Explanation,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        text.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(text);
        Ask();
    }

    private void GoOn() => GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, MainScene);

    private void Ask()
    {
        var dialog = new FileDialog
        {
            Title = "Папка игры Capone / Capone game folder",
            ModeOverridesTitle = false,
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
        };
        dialog.DirSelected += dir =>
        {
            dialog.QueueFree();
            if (OriginalData.SetGameFolder(dir))
                GoOn();
            else
                Retry(dir);
        };
        dialog.Canceled += () => GetTree().Quit();
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(700, 450));
    }

    /// <summary>The chosen folder isn't the game's: says so, then asks again.</summary>
    private void Retry(string dir)
    {
        var alert = new AcceptDialog
        {
            Title = "Capone",
            DialogText = $"В папке {dir} нет Capone.EXE и Chicago.tile.\nThere is no Capone.EXE and Chicago.tile in {dir}.",
        };
        alert.Confirmed += Ask;
        alert.Canceled += Ask;
        AddChild(alert);
        alert.PopupCentered();
    }
}
