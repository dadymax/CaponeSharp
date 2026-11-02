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
    /// <summary>The up (+1) / down (−1) arrow of the episode bar.</summary>
    public event Action<int> Switch;

    /// <summary>
    /// The arrows painted in ScenDisp.bmp above and below the episode bar (the scroll control at briefing +28h,
    /// sub_4644A1 / sub_406C00; one 7-pixel step per episode): up the next episode, down the previous one, within
    /// 1 .. the furthest reached (user).
    /// </summary>
    public static readonly Rect2 UpArrow = new(728, 98, 48, 25), DownArrow = new(728, 426, 48, 25);

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
        foreach (var (rect, step) in new[] { (UpArrow, 1), (DownArrow, -1) })
        {
            var arrow = new Button { Flat = true, Position = rect.Position, Size = rect.Size, FocusMode = FocusModeEnum.None };
            arrow.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            arrow.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            arrow.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            arrow.Pressed += () => Switch?.Invoke(step);
            AddChild(arrow);
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
/// End of an episode (sub_43C930 / sub_43CAB5, drawn by sub_43CCD0): GameWin.bmp or GAMEOVER.bmp with the "Back to
/// menu" button (GameWinB / GameOvBu rows 0 hover, 1 normal; 283×42 at 259, 527) and the statistics chart of
/// <see cref="Statistics"/>:
/// <list type="bullet">
/// <item>Tabs Income / Money / People (end screen +78h): the button sheet's strip (0, 84 + 26·tab, 267×26) at (40, 145);
/// clicks at y 145–169 switch them (sub_43DB50; the x ranges depend on the current tab).</item>
/// <item>Value axis: starts at −90 000..0 (income), 0..starting funds or 200 000 (money), 0..5 (people), widened to
/// every sample, at least 5 apart; min / max printed right-aligned at x 238, y 450 / 180; axis pen RGB(123,140,132):
/// x 241 from y 185 to 465, and the zero line to x 490 (with a "0" label) when it falls inside.</item>
/// <item>y(v) = 465 − (v − min)·280 / (max − min), clamped to 185..465. Each player's line starts at x 242 from
/// −90 000 / the starting funds / 2 and goes through x 250 + 8k for every complete slot.</item>
/// <item>Legend, player i (local first): name at (54, 180 + 15i), font 0 yellow, width 90, and a line sample from x 145
/// to 175 at y 187 + 15i; pens by i mod 11 (green, red, blue, yellow, magenta, cyan, maroon, navy, olive, teal,
/// maroon), solid below 11, dotted below 22, dashed above.</item>
/// <item>The message log at the top left (x 5, y 5 + 16n, font 3, width 700; messages go after 15 s, sub_43C6E0)
/// shows the result text (message kinds 2 / 3 are drawn red).</item>
/// </list>
/// Animation (sub_43CCD0, bitmap +8 loaded by sub_43CAB5): won — GameWinA.bmp, 100 × 100 frames; when none plays, at
/// random (GetTickCount % 10 > 3) one of three places (table 0x48D4D4: (599,420), (559,340), (548,410)) starts and
/// shows row place, frames 0–5 every 250 ms (columns 0, 1, 2, 4, 4, 5), then hides; lost — GameOvAn.bmp, 30 × 40
/// frames at (750, 470), cycling 0–5 every 250 ms. Sound WinEffect / LoseEffect; the CD music is not available.
/// </summary>
public partial class EndScreen : Control
{
    private static readonly Color AxisColor = Color.Color8(123, 140, 132);
    private static readonly Color[] PenColors =
    {
        Color.Color8(0, 255, 0), Color.Color8(255, 0, 0), Color.Color8(0, 0, 255), Color.Color8(255, 255, 0),
        Color.Color8(255, 0, 255), Color.Color8(0, 255, 255), Color.Color8(128, 0, 0), Color.Color8(0, 0, 128),
        Color.Color8(128, 128, 0), Color.Color8(0, 128, 128), Color.Color8(128, 0, 0),
    };
    private const double Top = 185, Bottom = 465, Height = 280;
    private const double MessageSeconds = 15;

    private readonly bool _won;
    private readonly string _text;
    private readonly Statistics _stats;
    private Texture2D _background;
    private Texture2D _buttons;
    private ulong _shownAt;

    /// <summary>End screen +78h: 0 income, 1 money, 2 people.</summary>
    public int Tab { get; set; }

    public event Action Closed;

    public EndScreen() { }

    public EndScreen(bool won, string text, Statistics stats = null)
    {
        _won = won;
        _text = text;
        _stats = stats;
        Name = "EndScreen";
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // a size, so the tab strip gets clicks
        MouseFilter = MouseFilterEnum.Stop;
        _shownAt = Time.GetTicksMsec();
        _background = ScreenPictures.Load(_won ? "GAMEWIN.BMP" : "GAMEOVER.BMP");
        _buttons = ScreenPictures.Load(_won ? "GAMEWINB.BMP" : "GAMEOVBU.BMP");
        _animation = ScreenPictures.Load(_won ? "GAMEWINA.BMP" : "GAMEOVAN.BMP");
        // sub_43C930: sound 18h / 19h of the screens' sound set (table 0x477A1C) — WinEffect / LoseEffect. The music
        // is a CD audio track (sub_408BE2 → MCI "cdaudio": 0 won, 4 lost), not in the game files.
        Sound.Play(_won ? "WinEffect.wav" : "LoseEffect.wav");
        _animPlace = _won ? -1 : 0;
        _animTick = Time.GetTicksMsec();
        if (_buttons != null)
            AddChild(ScreenPictures.Button(new Vector2(259, 527), ScreenPictures.Part(_buttons, 0, 42, 283, 42),
                ScreenPictures.Part(_buttons, 0, 0, 283, 42), null, () => Closed?.Invoke()));
    }

    private bool MessageShown => Time.GetTicksMsec() - _shownAt < MessageSeconds * 1000;

    private static readonly Vector2[] WinPlaces = { new(599, 420), new(559, 340), new(548, 410) };
    private Texture2D _animation;
    private int _animPlace = -1; // +18h: won — the place (-1 none); lost — 0
    private int _animFrame;      // +14h
    private ulong _animTick;     // +1Ch

    /// <summary>sub_43CCD0's animation step, on real time.</summary>
    private void StepAnimation()
    {
        if (_animation == null)
            return;
        var now = Time.GetTicksMsec();
        if (_won && _animPlace < 0)
        {
            if (now % 10 <= 3)
                return;
            _animPlace = (int)(now % 3);
            _animFrame = 0;
            _animTick = now;
            QueueRedraw();
            return;
        }
        if (now <= _animTick + 250)
            return;
        _animTick = now;
        if (++_animFrame > 5)
        {
            if (_won)
                _animPlace = -1;
            else
                _animFrame = 0;
        }
        QueueRedraw();
    }

    private void DrawAnimation()
    {
        if (_animation == null)
            return;
        if (_won)
        {
            if (_animPlace < 0 || _animFrame > 5)
                return;
            var column = _animFrame == 3 ? 4 : _animFrame;
            DrawTextureRectRegion(_animation, new Rect2(WinPlaces[_animPlace], new Vector2(100, 100)),
                new Rect2(column * 100, _animPlace * 100, 100, 100));
        }
        else
            DrawTextureRectRegion(_animation, new Rect2(750, 470, 30, 40), new Rect2(_animFrame * 30, 0, 30, 40));
    }

    private bool _messageDrawn;

    /// <summary>Real time (the game may still run fast): the message log drops the text after 15 s.</summary>
    public override void _Process(double delta)
    {
        StepAnimation();
        if (_messageDrawn && !MessageShown)
        {
            _messageDrawn = false;
            QueueRedraw();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            return;
        var tab = TabAt(Tab, (int)click.Position.X, (int)click.Position.Y);
        if (tab == Tab)
            return;
        Tab = tab;
        QueueRedraw();
    }

    /// <summary>sub_43DB50: the tab strip's hit test; the x ranges depend on the tab being shown.</summary>
    public static int TabAt(int tab, int x, int y)
    {
        if (y <= 0x90 || y >= 0xAA)
            return tab;
        var result = tab;
        switch (tab)
        {
            case 0:
                if (x > 0x88 && x < 0xCE)
                    result = 1;
                if (x > 0xCE && x < 0x12C)
                    result = 2;
                break;
            case 1:
                if (x > 0x28 && x < 0x7A)
                    result = 0;
                if (x > 0xCE && x < 0x12C)
                    result = 2;
                break;
            case 2:
                if (x > 0x32 && x < 0x7A)
                    result = 0;
                if (x > 0x7A && x < 0xCC)
                    result = 1;
                break;
        }
        return result;
    }

    /// <summary>The value axis of a tab: the start range widened to every complete sample, at least 5 apart.</summary>
    public static (long Min, long Max) Range(Statistics stats, int tab)
    {
        long min, max;
        switch (tab)
        {
            case 0: min = -90_000; max = 0; break;
            case 1: min = 0; max = stats is { StartFunds: > 0 } ? stats.StartFunds : 200_000; break;
            default: min = 0; max = 5; break;
        }
        if (stats != null)
            for (var p = 0; p < stats.Players.Count; p++)
                for (var k = 0; k < stats.Count; k++)
                {
                    var v = stats.Value(SeriesOf(tab), p, k);
                    if (v < min)
                        min = v;
                    if (v > max)
                        max = v;
                }
        if (min + 5 > max)
            max = min + 5;
        return (min, max);
    }

    private static int SeriesOf(int tab) => tab switch
    {
        0 => Statistics.NetIncome,
        1 => Statistics.Cash,
        _ => Statistics.Gangsters,
    };

    /// <summary>Screen y of a value, clamped to the chart.</summary>
    public static double Y(double v, long min, long max) => Math.Clamp(Bottom - (v - min) * Height / (max - min), Top, Bottom);

    public override void _Draw()
    {
        if (_background != null)
            DrawTexture(_background, Vector2.Zero);
        if (_buttons != null)
            DrawTextureRectRegion(_buttons, new Rect2(40, 145, 267, 26), new Rect2(0, 84 + 26 * Tab, 267, 26));
        // The result text is shown before, in the info window (user; sub_41D3E0) — not on this screen.
        DrawChart();
        DrawAnimation();
    }

    private void DrawChart()
    {
        var (min, max) = Range(_stats, Tab);
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        OriginalFonts.DrawScreen(this, min.ToString(culture), 238, 450, 80, 200, 2);
        OriginalFonts.DrawScreen(this, max.ToString(culture), 238, 180, 80, 200, 2);
        DrawLine(new Vector2(241, 185), new Vector2(241, 465), AxisColor);
        if (min < 0 && max > 0)
        {
            OriginalFonts.DrawScreen(this, "0", 238, (int)(457 - -min * Height / (max - min)), 80, 200, 2);
            var zero = (float)(Bottom - -min * Height / (max - min));
            DrawLine(new Vector2(241, zero), new Vector2(490, zero), AxisColor);
        }
        else
        {
            if (min == 0)
                DrawLine(new Vector2(241, 465), new Vector2(490, 465), AxisColor);
            if (max == 0)
                DrawLine(new Vector2(241, 185), new Vector2(490, 185), AxisColor);
        }
        if (_stats == null)
            return;
        double start = Tab switch { 0 => -90_000, 1 => _stats.StartFunds, _ => 2 };
        for (var p = _stats.Players.Count - 1; p >= 0; p--)
        {
            var color = PenColors[p % PenColors.Length];
            var y = 187 + 15 * p;
            OriginalFonts.DrawScreen(this, _stats.Players[p].PlayerName, 54, y - 7, 90, 0, 2);
            Line(new Vector2(145, y), new Vector2(175, y), color, p);
            var from = new Vector2(242, (float)Y(start, min, max));
            for (var k = 0; k < _stats.Count; k++)
            {
                var to = new Vector2(250 + 8 * k, (float)Y(_stats.Value(SeriesOf(Tab), p, k), min, max));
                Line(from, to, color, p);
                from = to;
            }
        }
    }

    /// <summary>GDI pen styles: solid for players 0–10, dotted for 11–21, dashed after.</summary>
    private void Line(Vector2 a, Vector2 b, Color color, int player)
    {
        if (player < 11)
            DrawLine(a, b, color);
        else
            DrawDashedLine(a, b, color, 1, player < 22 ? 1 : 6);
    }
}
