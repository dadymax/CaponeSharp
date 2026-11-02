using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// The menu's "Statistics" window (main window +0A20h, built by sub_45FD50, loaded by sub_460206, drawn by
/// sub_460290): "Ваше положение относительно других кланов". GameRank.bmp's top 600 × 400 at (100, 100); a hand
/// (50 × 60 at (100, 400) of the same picture, colour key = the pixel at (239, 421)) marks the local player's place
/// in each of five columns, between the best (top, y 135) and the worst (y 375) of the players in the game.
/// The "Вернуться" button (350, 468)–(450, 493) shows the picture's (0, 400, 100, 25) while the mouse is over it
/// (sub_4603F0, OverButton on entering) and closes the window on a click (sub_4603A2, ButtonClick).
/// The places are computed once, when the window opens.
/// </summary>
public partial class RankWindow : Control
{
    public const int MetricCount = 5;
    /// <summary>dword_48E4F8 − dword_48E4F0: the column height; 120 (the middle) when all players are equal.</summary>
    public const int Height = 375 - 135, Middle = 0x78;
    /// <summary>Column x (table 0x48E4EC, 16 bytes per column: x, top, x + 50, bottom).</summary>
    public static readonly int[] ColumnX = { 140, 258, 376, 496, 602 };
    private const int Top = 135;
    private static readonly Rect2 ButtonRect = new(350, 468, 100, 25);

    private readonly int[] _places;
    private Texture2D _picture, _hand;
    private bool _hover;

    public RankWindow(int[] places)
    {
        _places = places;
        Name = "RankWindow";
    }

    /// <summary>
    /// sub_45FD50: per metric, min and max over the players still in the game (class 2, vt+54h); the local player's
    /// place = H − (value − min) × H / (max − min), clamped to 0..H, or 120 when max = min. Metrics: cash (vt+60h),
    /// weekly net income (vt+64h), gangsters (own units other than Collectors), own legal companies (catalogue +10h
    /// = 0, company vt+1CCh = 0) and own businesses (vt+1CCh ≠ 0).
    /// </summary>
    public static int[] Places(long[] own, IEnumerable<long[]> players)
    {
        var all = players.ToList();
        var places = new int[MetricCount];
        for (var i = 0; i < MetricCount; i++)
        {
            places[i] = Middle;
            if (all.Count == 0)
                continue;
            var min = all.Min(p => p[i]);
            var range = all.Max(p => p[i]) - min;
            if (range <= 0)
                continue;
            // Cash goes through the FPU (truncated), the counts through integer division: the same result here.
            var place = Height - (own[i] - min) * Height / range;
            places[i] = (int)Math.Clamp(place, 0, Height);
        }
        return places;
    }

    public static long[] Metrics(Team team, SceneTree tree)
    {
        var units = tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
            .Count(u => !u.IsDead && u.OwnerTeam == team && u.UnitType != UnitCommon.UnitType.Collector);
        var companies = tree.GetNodesInGroup(Company.GroupName).OfType<Company>()
            .Where(c => GodotObject.IsInstanceValid(c) && c.IsEconomic && c.OwnerTeam == team).ToList();
        var businesses = companies.Count(c => c.Entry.Type != OriginalBusinessTable.BusinessType.Legal);
        return new[] { team.Org?.Cash ?? 0, team.Org?.NetIncome ?? 0, units, companies.Count - businesses, businesses };
    }

    public static int[] LocalPlaces(SceneTree tree)
    {
        var local = Teams.GetCurrentPlayerTeam();
        if (local == null)
            return Enumerable.Repeat(Middle, MetricCount).ToArray();
        var players = Teams.TeamsInGame.Values.Where(t => t.Org is { Eliminated: false }).Select(t => Metrics(t, tree));
        return Places(Metrics(local, tree), players);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        if (OriginalData.Instance.Bmps.Bmps.TryGetValue("GAMERANK.BMP", out var bmp) && bmp.Body != null)
        {
            _picture = ImageTexture.CreateFromImage(bmp.Body);
            var hand = bmp.Body.GetRegion(new Rect2I(100, 400, 50, 60));
            hand.Convert(Image.Format.Rgba8);
            var key = bmp.Body.GetPixel(239, 421);
            for (var y = 0; y < hand.GetHeight(); y++)
                for (var x = 0; x < hand.GetWidth(); x++)
                    if (hand.GetPixel(x, y).IsEqualApprox(key))
                        hand.SetPixel(x, y, Colors.Transparent);
            _hand = ImageTexture.CreateFromImage(hand);
        }
    }

    public override bool _HasPoint(Vector2 point) => new Rect2(100, 100, 600, 400).HasPoint(point);

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                var over = ButtonRect.HasPoint(motion.Position);
                if (over && !_hover)
                    Sound.Play("OverButton.wav");
                if (over != _hover)
                {
                    _hover = over;
                    QueueRedraw();
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                AcceptEvent();
                if (ButtonRect.HasPoint(click.Position))
                {
                    Sound.Play("ButtonClick.wav");
                    Close();
                }
                break;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Keycode: Key.Escape or Key.Enter or Key.KpEnter })
            return;
        Close();
        GetViewport().SetInputAsHandled();
    }

    public void Close()
    {
        if (!IsQueuedForDeletion())
            QueueFree();
    }

    public override void _Draw()
    {
        if (_picture == null)
            return;
        DrawTextureRectRegion(_picture, new Rect2(100, 100, 600, 400), new Rect2(0, 0, 600, 400));
        if (_hover)
            DrawTextureRectRegion(_picture, ButtonRect, new Rect2(0, 400, 100, 25));
        for (var i = 0; i < MetricCount; i++)
            DrawTexture(_hand, new Vector2(ColumnX[i], Top + _places[i]));
    }
}
