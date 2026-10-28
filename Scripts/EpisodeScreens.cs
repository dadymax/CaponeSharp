using System;
using Godot;

namespace Capone;

/// <summary>
/// Campaign progress, saved like the original's config (fnWriteConfig after a won episode, sub_43C930): the episode to
/// play next (World +3B8h) and the furthest episode reached (+3BCh).
/// </summary>
public static class CampaignProgress
{
    private const string Path = "user://campaign.cfg";

    /// <summary>1-based episode to play next.</summary>
    public static int Current { get; private set; } = 1;
    public static int MaxReached { get; private set; } = 1;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok)
            return;
        Current = Math.Max(1, (int)cfg.GetValue("campaign", "current", 1));
        MaxReached = Math.Max(Current, (int)cfg.GetValue("campaign", "max_reached", Current));
    }

    /// <summary>Episode <paramref name="episode"/> won: the next one becomes current.</summary>
    public static void Won(int episode, int episodeCount)
    {
        Current = Math.Min(episode + 1, episodeCount);
        MaxReached = Math.Max(MaxReached, Current);
        var cfg = new ConfigFile();
        cfg.SetValue("campaign", "current", Current);
        cfg.SetValue("campaign", "max_reached", MaxReached);
        cfg.Save(Path);
    }
}

/// <summary>Helpers for the full-screen 800×600 pictures.</summary>
internal static class ScreenPictures
{
    public static Texture2D Load(string name) =>
        OriginalData.Instance.Bmps.Bmps.TryGetValue(name, out var bmp) && bmp.Body != null
            ? ImageTexture.CreateFromImage(bmp.Body)
            : null;

    public static AtlasTexture Part(Texture2D sheet, int x, int y, int w, int h) =>
        new() { Atlas = sheet, Region = new Rect2(x, y, w, h) };

    public static TextureButton Button(Vector2 pos, Texture2D normal, Texture2D hover, Texture2D pressed, Action click)
    {
        var button = new TextureButton
        {
            Position = pos, TextureNormal = normal, TextureHover = hover, TexturePressed = pressed ?? hover,
            ProcessMode = Node.ProcessModeEnum.Always,
        };
        button.Pressed += click;
        return button;
    }

    public static string[] Lines(string text) =>
        (text ?? "").Replace("\r", "").Split('\n');
}

/// <summary>
/// Main menu (sub_408F86 loads, sub_4090D0 draws): MainMenu.bmp with five 283×42 buttons at x 257 and y from the table
/// at 0x47698C (152, 237, 321, 407, 491): Campaign, Network game, Settings, Developers, Exit. MainMeBu.bmp holds the
/// lit picture of each in rows 0–4 and the pressed one in rows 5–9.
/// Only Campaign and Exit work; Network game, Settings and Developers are left for later (user: only on request).
/// </summary>
public partial class MainMenuScreen : Control
{
    public static readonly int[] ButtonY = { 152, 237, 321, 407, 491 };
    public const int ButtonX = 257;
    public const int ButtonWidth = 283;
    public const int ButtonHeight = 42;
    public const int Campaign = 0, Exit = 4;

    private Texture2D _background;

    public event Action CampaignChosen;
    public event Action ExitChosen;

    public MainMenuScreen() => Name = "MainMenu";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _background = ScreenPictures.Load("MAINMENU.BMP");
        var buttons = ScreenPictures.Load("MAINMEBU.BMP");
        if (buttons == null)
            return;
        foreach (var (index, action) in new[] { (Campaign, (Action)(() => CampaignChosen?.Invoke())), (Exit, () => ExitChosen?.Invoke()) })
        {
            var y = ButtonY[index];
            var button = ScreenPictures.Button(new Vector2(ButtonX, y),
                _background != null ? ScreenPictures.Part(_background, ButtonX, y, ButtonWidth, ButtonHeight) : null,
                ScreenPictures.Part(buttons, 0, ButtonHeight * index, ButtonWidth, ButtonHeight),
                ScreenPictures.Part(buttons, 0, ButtonHeight * (index + 5), ButtonWidth, ButtonHeight), action);
            button.Name = index == Campaign ? "Campaign" : "Exit";
            AddChild(button);
        }
    }

    public override void _Draw()
    {
        if (_background != null)
            DrawTexture(_background, Vector2.Zero);
    }
}

/// <summary>
/// Episode briefing (sub_464AE0 draws, sub_4644A1 reads the texts): ScenDisp.bmp; SCENARIONAME centred at (410, 70)
/// font 4 yellow; up to 14 STORYTEXT lines at (183, 109 + 15i) and 3 OBJECTIVETEXT lines at (183, 357 + 15i), font 3
/// green, width 525; the MAP file name at (179, 421) font 2 green; one 7-pixel mark per finished episode on the bar at
/// x 734 (copied from ScenDisp 734,412–771,419 upwards from y 405); buttons from ScenDiBu.bmp: "Start game" at
/// (152, 523), "Back" at (500, 523), 200×42.
/// </summary>
public partial class BriefingScreen : Control
{
    private readonly Scenario _scenario;
    private readonly int _episodeIndex;
    private Texture2D _background;

    public event Action Started;
    public event Action Back;

    public BriefingScreen() { }

    public BriefingScreen(Scenario scenario, int episodeIndex)
    {
        _scenario = scenario;
        _episodeIndex = episodeIndex;
        Name = "BriefingScreen";
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _background = ScreenPictures.Load("SCENDISP.BMP");
        var buttons = ScreenPictures.Load("SCENDIBU.BMP");
        if (buttons != null)
        {
            AddChild(ScreenPictures.Button(new Vector2(152, 523), ScreenPictures.Part(buttons, 0, 168, 200, 42),
                ScreenPictures.Part(buttons, 0, 0, 200, 42), ScreenPictures.Part(buttons, 0, 42, 200, 42), () => Started?.Invoke()));
            AddChild(ScreenPictures.Button(new Vector2(500, 523),
                _background != null ? ScreenPictures.Part(_background, 500, 523, 200, 42) : null,
                ScreenPictures.Part(buttons, 0, 84, 200, 42), ScreenPictures.Part(buttons, 0, 126, 200, 42), () => Back?.Invoke()));
        }
    }

    public override void _Draw()
    {
        if (_background != null)
        {
            DrawTexture(_background, Vector2.Zero);
            var mark = new Rect2(734, 412, 37, 7);
            for (var k = 0; k < _episodeIndex * 7; k += 7)
                DrawTextureRectRegion(_background, new Rect2(734, 405 - k, 37, 7), mark);
        }
        if (_scenario == null)
            return;
        OriginalFonts.DrawScreen(this, _scenario.ScenarioName, 410, 70, 700, 104, 2);
        var story = ScreenPictures.Lines(_scenario.StoryText);
        for (var i = 0; i < 14 && i < story.Length; i++)
            OriginalFonts.DrawScreen(this, story[i], 183, 109 + 15 * i, 525, 3, 0);
        var objective = ScreenPictures.Lines(_scenario.ObjectiveText);
        for (var i = 0; i < 3 && i < objective.Length; i++)
            OriginalFonts.DrawScreen(this, objective[i], 183, 357 + 15 * i, 525, 3, 0);
        OriginalFonts.DrawScreen(this, _scenario.MapFile, 179, 421, 525, 2, 0);
    }
}

/// <summary>
/// End of an episode (sub_43C930 / sub_43CAB5): GameWin.bmp or GAMEOVER.bmp with the "Back to menu" button
/// (GameWinB / GameOvBu rows 0 hover, 1 normal; 283×42 at 259, 527).
/// TODO: the green box shows the statistics chart (sub_43CCD0, tabs Income / Money / People from the button sheet rows
/// 2–4) and GameWinA / GameOvAn animate; the SUCCESSTEXT / FAILURETEXT goes to the message log. Until then the box
/// shows the episode's result text.
/// </summary>
public partial class EndScreen : Control
{
    private readonly bool _won;
    private readonly string _text;
    private Texture2D _background;

    public event Action Closed;

    public EndScreen() { }

    public EndScreen(bool won, string text)
    {
        _won = won;
        _text = text;
        Name = "EndScreen";
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _background = ScreenPictures.Load(_won ? "GAMEWIN.BMP" : "GAMEOVER.BMP");
        var buttons = ScreenPictures.Load(_won ? "GAMEWINB.BMP" : "GAMEOVBU.BMP");
        if (buttons != null)
            AddChild(ScreenPictures.Button(new Vector2(259, 527), ScreenPictures.Part(buttons, 0, 42, 283, 42),
                ScreenPictures.Part(buttons, 0, 0, 283, 42), null, () => Closed?.Invoke()));
    }

    public override void _Draw()
    {
        if (_background != null)
            DrawTexture(_background, Vector2.Zero);
        var lines = ScreenPictures.Lines(_text);
        for (var i = 0; i < lines.Length && i < 19; i++)
            OriginalFonts.DrawScreen(this, lines[i], 56, 186 + 15 * i, 430, 3, 2);
    }
}
