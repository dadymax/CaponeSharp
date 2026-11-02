using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// The menu's "Деньги" window (main window +0AE4h): "Перевести деньги другим игрокам" — give money to other players.
/// Built by sub_4629A6, loaded by sub_462B86 (Pics\MoneyWin.bmp, colour key = the pixel at (367, 312)), drawn by
/// sub_462C13, clicks sub_4630A8, mouse moves sub_463450 / sub_463426.
/// <list type="bullet">
/// <item>MoneyWin's top 400 × 300 at (200, 150). The other players still in the game (vt+54h), up to 18 in three
/// columns of six rows 30 px apart: a box (312, 300, 25, 25) at (218 / 338 / 458, 186 + 30r), the tick (300, 300, 12,
/// 18) at (224 / 344 / 464, 189 + 30r) when chosen, the name at (248 / 368 / 488, 193 + 30r), width 90, font 3,
/// colour 2. (The original draws the ticks of columns 2 and 3 120 / 360 px too low, off the list — drawn in place
/// here.) A click in x 220–246 / 340–356 / 460–476, y 189 + 30r … 213 + 30r toggles the player.</item>
/// <item>The amount: a slider (sub_406996 class) over (304, 386)–(496, 406), positions 0..19, left arrow (200, 300,
/// 20, 20) / pressed (220, 300), knob (240, 300), right arrow (260, 300) / pressed (280, 300); a click on an arrow
/// steps by one (and repeats every 250 ms while held), on the track it jumps there and drags. Amount = 50 000 ×
/// (position + 1), printed at (501, 386), width 80, font 3, colour 2.</item>
/// <item>Buttons "Перевести" (250, 418)–(350, 443) and "Вернуться" (450, 418)–(550, 443): the picture's (0 / 100,
/// 300, 100, 25) while the mouse is over one (OverButton on entering), ButtonClick on a click. "Перевести" posts a
/// transfer event (sub_429470) to every chosen player; both close the window.</item>
/// </list>
/// </summary>
public partial class MoneyWindow : Control
{
    public const int Positions = 20, MaxRows = 18;
    public const long Step = 50_000;
    private static readonly Rect2 SliderRect = new(304, 386, 192, 20);
    private const int TrackX = 324, TrackLength = 192 - 20 - 20 - 20; // left arrow, right arrow and knob widths
    private static readonly int[] BoxX = { 218, 338, 458 }, ClickX = { 220, 340, 460 }, ClickW = { 26, 16, 16 }, NameX = { 248, 368, 488 };

    private readonly List<Team> _players;
    private readonly bool[] _chosen;
    private Texture2D _picture;
    private int _value;
    private int _hover;        // +4: 1 "Перевести", 2 "Вернуться"
    private int _sliderState;  // +98h: 1 dragging the knob, 2 left arrow held, 3 right arrow held
    private double _repeat;

    public MoneyWindow(IEnumerable<Team> players)
    {
        _players = players.Take(MaxRows).ToList();
        _chosen = new bool[_players.Count];
        Name = "MoneyWindow";
    }

    public long Amount => AmountOf(_value);

    public static long AmountOf(int position) => Step * (position + 1);

    /// <summary>sub_4629A6: every player but the local one that is still in the game (vt+54h).</summary>
    public static IEnumerable<Team> OtherPlayers(Team local) =>
        Teams.TeamsInGame.Values.Where(t => t != local && t.Org is { Eliminated: false }).OrderBy(t => t.PlayerId);

    /// <summary>
    /// The transfer event (sub_4294B0): when both players are in the game and the giver can afford it (vt+70h, cash −
    /// amount + 200 000 ≥ 0) the giver pays (vt+6Ch) and the receiver deposits it (vt+68h — split by its bribe shares
    /// like any income). The local giver reads 25,3 (receiver, amount), the local receiver 25,4 (giver, amount); sound
    /// 4 (no message when refused).
    /// </summary>
    public static bool Transfer(Team from, Team to, long amount)
    {
        if (from?.Org is not { Eliminated: false } giver || to?.Org is not { Eliminated: false } receiver
            || !giver.CanAfford(amount))
            return false;
        giver.Spend(amount);
        receiver.Deposit(amount);
        if (from.IsCurrentPlayerColor || to.IsCurrentPlayerColor)
        {
            var other = from.IsCurrentPlayerColor ? to : from;
            Messages.Show(PanelInfo.CFormat(OriginalTexts.Instance.Get(25, from.IsCurrentPlayerColor ? 3 : 4),
                other.PlayerName, PanelInfo.Money(amount)));
            Sound.PlayId(4);
        }
        GD.Print($"MoneyWindow: {from.PlayerName} gives {amount} to {to.PlayerName}");
        return true;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        if (OriginalData.Instance.Bmps.Bmps.TryGetValue("MONEYWIN.BMP", out var bmp) && bmp.Body != null)
        {
            var image = (Image)bmp.Body.Duplicate();
            image.Convert(Image.Format.Rgba8);
            var key = image.GetPixel(367, 312);
            for (var y = 300; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                    if (image.GetPixel(x, y).IsEqualApprox(key))
                        image.SetPixel(x, y, Colors.Transparent);
            _picture = ImageTexture.CreateFromImage(image);
        }
    }

    public override bool _HasPoint(Vector2 point) => new Rect2(200, 150, 400, 300).HasPoint(point);

    private static int ButtonAt(Vector2 p) =>
        p.Y > 418 && p.Y < 443 ? p.X > 250 && p.X < 350 ? 1 : p.X > 450 && p.X < 550 ? 2 : 0 : 0;

    private static int KnobX(int value) => TrackX + value * TrackLength / (Positions - 1);

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
            {
                var over = ButtonAt(motion.Position);
                if (over > 0 && over != _hover)
                    Sound.Play("OverButton.wav");
                if (over != _hover)
                {
                    _hover = over;
                    QueueRedraw();
                }
                if (_sliderState == 1)
                    DragTo(motion.Position.X);
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                AcceptEvent();
                if (!click.Pressed)
                {
                    _sliderState = 0;
                    QueueRedraw();
                    return;
                }
                Press(click.Position);
                break;
        }
    }

    private void Press(Vector2 p)
    {
        if (SliderRect.HasPoint(p))
        {
            if (p.X < TrackX)
                StepBy(-1, 2);
            else if (p.X >= SliderRect.End.X - 20)
                StepBy(1, 3);
            else
            {
                _sliderState = 1;
                DragTo(p.X);
            }
            return;
        }
        switch (ButtonAt(p))
        {
            case 1:
                Sound.Play("ButtonClick.wav");
                var local = Teams.GetCurrentPlayerTeam();
                for (var i = 0; i < _players.Count; i++)
                    if (_chosen[i])
                        Transfer(local, _players[i], Amount);
                Close();
                return;
            case 2:
                Sound.Play("ButtonClick.wav");
                Close();
                return;
        }
        for (var i = 0; i < _players.Count; i++)
        {
            int column = i / 6, row = i % 6;
            var top = 189 + 30 * row;
            if (p.X > ClickX[column] && p.X < ClickX[column] + ClickW[column] && p.Y > top && p.Y < top + 24)
            {
                _chosen[i] = !_chosen[i];
                QueueRedraw();
                return;
            }
        }
    }

    private void StepBy(int delta, int state)
    {
        _value = Mathf.Clamp(_value + delta, 0, Positions - 1);
        _sliderState = state;
        _repeat = 0.25;
        QueueRedraw();
    }

    /// <summary>sub_406C00 with mode 0: value = (range × (x − track start) / length + step / 2) / step × step.</summary>
    private void DragTo(float x)
    {
        _value = Mathf.Clamp(((int)x - TrackX) * (Positions - 1) / TrackLength, 0, Positions - 1);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_sliderState < 2)
            return;
        _repeat -= delta;
        if (_repeat > 0)
            return;
        StepBy(_sliderState == 2 ? -1 : 1, _sliderState);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Keycode: Key.Escape })
            return;
        Close();
        GetViewport().SetInputAsHandled();
    }

    public void Close()
    {
        if (!IsQueuedForDeletion())
            QueueFree();
    }

    /// <summary>Test hook: choose the player at <paramref name="index"/> and the slider position.</summary>
    public void Choose(int index, int position)
    {
        if (index >= 0 && index < _chosen.Length)
            _chosen[index] = true;
        _value = Mathf.Clamp(position, 0, Positions - 1);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_picture == null)
            return;
        DrawTextureRectRegion(_picture, new Rect2(200, 150, 400, 300), new Rect2(0, 0, 400, 300));
        if (_hover == 1)
            DrawTextureRectRegion(_picture, new Rect2(250, 418, 100, 25), new Rect2(0, 300, 100, 25));
        else if (_hover == 2)
            DrawTextureRectRegion(_picture, new Rect2(450, 418, 100, 25), new Rect2(100, 300, 100, 25));
        DrawTextureRectRegion(_picture, new Rect2(304, 386, 20, 20), new Rect2(_sliderState == 2 ? 220 : 200, 300, 20, 20));
        DrawTextureRectRegion(_picture, new Rect2(KnobX(_value), 386, 20, 20), new Rect2(240, 300, 20, 20));
        DrawTextureRectRegion(_picture, new Rect2(476, 386, 20, 20), new Rect2(_sliderState == 3 ? 280 : 260, 300, 20, 20));
        for (var i = 0; i < _players.Count; i++)
        {
            int column = i / 6, row = i % 6;
            var y = 189 + 30 * row;
            DrawTextureRectRegion(_picture, new Rect2(BoxX[column], y - 3, 25, 25), new Rect2(312, 300, 25, 25));
            if (_chosen[i])
                DrawTextureRectRegion(_picture, new Rect2(BoxX[column] + 6, y, 12, 18), new Rect2(300, 300, 12, 18));
            OriginalFonts.DrawScreen(this, _players[i].PlayerName, NameX[column], 193 + 30 * row, 90, 3, 2);
        }
        OriginalFonts.DrawScreen(this, PanelInfo.Money(Amount), 501, 386, 80, 3, 2);
    }
}
