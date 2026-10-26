using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// The recruit column at the panel's right edge (sub_40A660 draws the local player's recruit menu every frame;
/// slots 6–17 are 30×30 at x 730 / 760, y 392 + 33k, table 0x476B48). While a unit trains its button stays down and
/// Blood.bmp (6 animation columns of 60×210) rises over the strip 728..789 × 390..589 by the progress × 210.
/// </summary>
public partial class RecruitPanel : Control
{
    private const int Size30 = 30;
    private static readonly Vector2 StripTopLeft = new(730, 390 - PanelInfo.PanelTop);

    private readonly Dictionary<Training.Recruit, TextureButton> _buttons = new();
    private Texture2D _blood;
    private int _bloodColumn;
    private double _bloodTimer;
    private double _refresh;
    private string _shown = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _blood = ScreenPictures.Load("BLOOD.BMP");
        foreach (var recruit in Training.Recruits)
        {
            var r = recruit;
            var button = new TextureButton
            {
                Position = SlotPosition(recruit.Slot), Size = new Vector2(Size30, Size30), Visible = false,
                TextureNormal = Icon(recruit.Icon), TextureHover = Icon(recruit.Icon), TexturePressed = Icon(recruit.Icon + 1),
            };
            button.Pressed += () => Training.Start(Teams.GetCurrentPlayerTeam(), r);
            AddChild(button);
            _buttons[recruit] = button;
        }
    }

    private static Vector2 SlotPosition(int slot)
    {
        var i = slot - 6;
        return new Vector2(i < 6 ? 730 : 760, 392 + 33 * (i % 6) - PanelInfo.PanelTop);
    }

    private static Texture2D Icon(int tileIndex)
    {
        var tile = OriginalData.Instance.Tiles.GetTileByIndex(tileIndex);
        var images = tile != null ? OriginalData.Instance.GetImageForTile(tile) : null;
        return images is { Count: > 0 } ? ImageTexture.CreateFromImage(images[0]) : null;
    }

    public override void _Process(double delta)
    {
        var realDelta = delta / Engine.TimeScale;
        _bloodTimer += realDelta;
        if (_bloodTimer >= 0.1)
        {
            _bloodTimer = 0;
            _bloodColumn = (_bloodColumn + 1) % 6;
        }
        _refresh -= realDelta;
        if (_refresh > 0)
            return;
        _refresh = 0.1;
        Rebuild();
        QueueRedraw();
    }

    private void Rebuild()
    {
        var team = Teams.GetCurrentPlayerTeam();
        var org = team?.Org;
        var job = Training.Current(team);
        var key = org == null ? "" : $"{job?.Recruit.Type}|{Training.PrimaryCenter(team) != null}|{(int)org.Favour[1]}|{org.AuthorityMoney[1]}|{(int)org.Favour[3]}|{org.AuthorityMoney[3]}";
        if (key == _shown)
            return;
        _shown = key;
        foreach (var (recruit, button) in _buttons)
        {
            // The menu (sub_431FD3) is only built while no unit trains; the pressed button of the job stays drawn.
            var training = job != null;
            button.Visible = org != null && Training.PrimaryCenter(team) != null
                             && (training ? job.Recruit == recruit : Training.IsOffered(org, recruit));
            button.Disabled = training;
            button.TextureDisabled = Icon(recruit.Icon + 1);
            var price = PanelInfo.Money(Training.Price(recruit.Type));
            button.TooltipText = PanelInfo.CFormat(OriginalTexts.Instance.Get(20, recruit.Text), price, Training.ServicePoints(recruit));
        }
    }

    public override void _Draw()
    {
        var job = Training.Current(Teams.GetCurrentPlayerTeam());
        if (job == null || _blood == null || GameClock.Instance == null)
            return;
        var h = Mathf.RoundToInt(job.Progress(GameClock.Instance.Minute) * 210);
        var top = h > 200 ? h - 200 : 0;
        var visible = h - top;
        if (visible <= 0)
            return;
        var source = new Rect2(_bloodColumn * 60, top, 60, visible);
        DrawTextureRectRegion(_blood, new Rect2(StripTopLeft.X, StripTopLeft.Y + 200 - visible, 60, visible), source);
    }
}
