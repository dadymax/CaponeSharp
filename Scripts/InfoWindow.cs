using System;
using Godot;

namespace Capone;

/// <summary>
/// The in-game info window (class built by sub_447A41 / sub_447A97, loaded by sub_447C10, drawn by sub_447CB0):
/// InfoWin.bmp's top 400 × 300 at (200, 150) and, for one button (mode 1), its strip (0, 300, 400, 40) at (200, 410).
/// The title (+8) at (205, 152), width 290, font 2 yellow; up to 18 lines (+108h, 81 characters each, or a TIPTEXT
/// table) from (210, 180), 15 px apart, width 380, font 3 green; the button label (7,0) at (380, 418), font 2 yellow.
/// The button (350, 418)–(450, 440) shows InfoWin's pressed picture (0, 380, 100, 25) while held and closes the
/// window.
/// <list type="bullet">
/// <item>Episode end (sub_41D3E0, message kinds 2 / 3): title 9,37 and SUCCESSTEXT / FAILURETEXT; after OK the end
/// screen.</item>
/// <item>Episode start (sub_40A160 → sub_414760(1), single player): the episode's TIPTEXT under title 5,85.</item>
/// </list>
/// </summary>
public partial class InfoWindow : Control
{
    public const int MaxLines = 18;
    private static readonly Rect2 ButtonRect = new(350, 418, 100, 22);

    private readonly string _title;
    private readonly string[] _lines;
    private Texture2D _picture;
    private bool _pressed;

    public event Action Closed;

    public InfoWindow() { }

    public InfoWindow(string title, string text)
    {
        _title = title ?? "";
        _lines = ScreenPictures.Lines(text ?? "");
        Name = "InfoWindow";
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // clicks only inside the window and its button (_HasPoint)
        _picture = ScreenPictures.Load("INFOWIN.BMP");
    }

    public override bool _HasPoint(Vector2 point) => new Rect2(200, 150, 400, 300).HasPoint(point) || ButtonRect.HasPoint(point);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
            return;
        AcceptEvent();
        var onButton = ButtonRect.HasPoint(click.Position);
        if (click.Pressed)
        {
            _pressed = onButton;
            QueueRedraw();
            return;
        }
        var close = _pressed && onButton;
        _pressed = false;
        QueueRedraw();
        if (close)
            Close();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Escape or Key.Space })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void Close()
    {
        if (IsQueuedForDeletion())
            return;
        Closed?.Invoke();
        QueueFree();
    }

    public override void _Draw()
    {
        if (_picture != null)
        {
            DrawTextureRectRegion(_picture, new Rect2(200, 150, 400, 300), new Rect2(0, 0, 400, 300));
            DrawTextureRectRegion(_picture, new Rect2(200, 410, 400, 40), new Rect2(0, 300, 400, 40));
            if (_pressed)
                DrawTextureRectRegion(_picture, new Rect2(350, 418, 100, 25), new Rect2(0, 380, 100, 25));
        }
        OriginalFonts.DrawScreen(this, _title, 205, 152, 290, 2, 2);
        for (var i = 0; i < _lines.Length && i < MaxLines; i++)
            OriginalFonts.DrawScreen(this, _lines[i], 210, 180 + 15 * i, 380, 3, 0);
        OriginalFonts.DrawScreen(this, OriginalTexts.Instance.Get(7, 0), 380, 418, 40, 2, 2);
    }
}
