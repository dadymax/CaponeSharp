using System;
using Godot;

namespace Capone;

/// <summary>
/// The in-game info window (class built by sub_447A41 / sub_447A97, loaded by sub_447C10, drawn by sub_447CB0):
/// InfoWin.bmp's top 400 × 300 at (200, 150) and, for one button (mode 1), its strip (0, 300, 400, 40) at (200, 410).
/// The title (+8) at (205, 152), width 290, font 2 yellow; up to 18 lines (+108h, 81 characters each, or a TIPTEXT
/// table) from (210, 180), 15 px apart, width 380, font 3 green; the button label (7,0) at (380, 418), font 2 yellow.
/// The button (350, 418)–(450, 440) shows InfoWin's pressed picture (0, 380, 100, 25) while held and closes the
/// window. Mode 2 (exit confirmation) has OK at (250, 418) and "Вернуться" at (450, 418), labels 7,1 / 7,2 at
/// (280, 422) / (465, 422), font 1.
/// <list type="bullet">
/// <item>Episode end (sub_41D3E0, message kinds 2 / 3): title 9,37 and SUCCESSTEXT / FAILURETEXT; after OK the end
/// screen.</item>
/// <item>Episode start (sub_40A160 → sub_414760(1), single player): the episode's TIPTEXT under title 5,85.</item>
/// </list>
/// </summary>
public partial class InfoWindow : Control
{
    public const int MaxLines = 18;
    /// <summary>Mode 1: one OK button; mode 2: OK and "Вернуться" (Back).</summary>
    private static readonly Rect2 OkRect = new(350, 418, 100, 22);
    private static readonly Rect2 YesRect = new(250, 418, 100, 22), NoRect = new(450, 418, 100, 22);

    private readonly string _title;
    private readonly string[] _lines;
    private readonly int _mode = 1;
    private Texture2D _picture;
    private int _pressed; // +6BCh: 0 none, 1 OK (mode 1), 2 OK, 3 Back (mode 2)

    /// <summary>Closed with the OK button (or Enter): true; with Back (or Escape) in mode 2: false.</summary>
    public event Action<bool> Answered;
    public event Action Closed;

    public InfoWindow() { }

    public InfoWindow(string title, string text) : this(title, ScreenPictures.Lines(text ?? ""), 1) { }

    /// <summary><paramref name="lines"/>: the window's lines (+108h, empty ones leave a gap).</summary>
    public InfoWindow(string title, string[] lines, int mode)
    {
        _title = title ?? "";
        _lines = lines ?? Array.Empty<string>();
        _mode = mode;
        Name = "InfoWindow";
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // clicks only inside the window and its buttons (_HasPoint)
        _picture = ScreenPictures.Load("INFOWIN.BMP");
    }

    public override bool _HasPoint(Vector2 point) => new Rect2(200, 150, 400, 300).HasPoint(point) || ButtonAt(point) != 0;

    private int ButtonAt(Vector2 p) => _mode == 2
        ? YesRect.HasPoint(p) ? 2 : NoRect.HasPoint(p) ? 3 : 0
        : OkRect.HasPoint(p) ? 1 : 0;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
            return;
        AcceptEvent();
        var button = ButtonAt(click.Position);
        if (click.Pressed)
        {
            _pressed = button;
            QueueRedraw();
            return;
        }
        var released = _pressed == button ? button : 0;
        _pressed = 0;
        QueueRedraw();
        if (released != 0)
            Close(released != 3);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;
        if (key.Keycode is Key.Enter or Key.KpEnter or Key.Space || Hotkeys.IsPauseKey(key))
            Close(true);
        else if (key.Keycode == Key.Escape)
            Close(_mode == 1);
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    public void Close() => Close(true);

    private void Close(bool ok)
    {
        if (IsQueuedForDeletion())
            return;
        Answered?.Invoke(ok);
        Closed?.Invoke();
        QueueFree();
    }

    public override void _Draw()
    {
        if (_picture != null)
        {
            DrawTextureRectRegion(_picture, new Rect2(200, 150, 400, 300), new Rect2(0, 0, 400, 300));
            DrawTextureRectRegion(_picture, new Rect2(200, 410, 400, 40), new Rect2(0, _mode == 2 ? 340 : 300, 400, 40));
            var x = _pressed switch { 1 => 350, 2 => 250, 3 => 450, _ => -1 };
            if (x >= 0)
                DrawTextureRectRegion(_picture, new Rect2(x, 418, 100, 25), new Rect2(0, 380, 100, 25));
        }
        OriginalFonts.DrawScreen(this, _title, 205, 152, 290, 2, 2);
        for (var i = 0; i < _lines.Length && i < MaxLines; i++)
            OriginalFonts.DrawScreen(this, _lines[i], 210, 180 + 15 * i, 380, 3, 0);
        if (_mode == 2)
        {
            OriginalFonts.DrawScreen(this, OriginalTexts.Instance.Get(7, 1), 280, 422, 40, 1, 2);
            OriginalFonts.DrawScreen(this, OriginalTexts.Instance.Get(7, 2), 465, 422, 70, 1, 2);
        }
        else
            OriginalFonts.DrawScreen(this, OriginalTexts.Instance.Get(7, 0), 380, 418, 40, 2, 2);
    }
}
