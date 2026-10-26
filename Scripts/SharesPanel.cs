using Godot;

namespace Capone;

/// <summary>
/// The "Расходы" chart: five coin bars that split the income between savings and bribes to the army, FBI, politician
/// and police (Organisation.Shares 0–4). Rows at x 546..680 (table 0x476BF8); a bar is GameButt.bmp's coin strip
/// (16,43, 20 high) as wide as its share on a piecewise scale (sub_409C60). Dragging a bar sets its share and scales
/// the others to keep 100 %; hovering a row prints its share (texts 5,63/64/67/70/73) and the authority's favour
/// (5,65/66…) on the status line at (232, 555) / (232, 573).
/// </summary>
public partial class SharesPanel : Control
{
    private static readonly Rect2I[] Rows =
    {
        new(546, 447, 134, 24), new(546, 472, 134, 26), new(546, 499, 134, 26), new(546, 526, 134, 27), new(546, 554, 134, 26),
    };
    public const int FullWidth = 134;

    /// <summary>Share text per row (5, n) and the favour text pair (points, point) for authorities.</summary>
    private static readonly (int Share, int Favours, int Favour)[] Texts =
        { (63, -1, -1), (64, 65, 66), (67, 68, 69), (70, 71, 72), (73, 74, 75) };

    private readonly int[] _widths = new int[5];
    private Texture2D _coins;
    private int _dragRow = -1;
    private int _hoverRow = -1;
    private Organisation _org;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;
        _coins = ScreenPictures.Load("GAMEBUTT.BMP");
    }

    // ---- scale (sub_409C60): pixel width ↔ percent, knots (0,0) (49,20) (88,40) (108,60) (122,80) (134,100) -------

    public static float PercentOf(int w) => w < 49 ? w * 20f / 49
        : w < 88 ? (w - 49) * 20f / 39 + 20
        : w < 108 ? (w - 88) * 20f / 20 + 40
        : w < 122 ? (w - 108) * 20f / 14 + 60
        : (w - 122) * 20f / 12 + 80;

    public static int WidthOf(float p) => (int)(p < 21 ? p * 49 / 20
        : p < 41 ? (p - 20) * 39 / 20 + 49
        : p < 61 ? (p - 40) * 20 / 20 + 88
        : p < 81 ? (p - 60) * 14 / 20 + 108
        : (p - 80) * 12 / 20 + 122);

    /// <summary>
    /// sub_409C60(k): bar <paramref name="k"/> keeps its width; the other bars are scaled to share the rest of 100 %
    /// (equally — a quarter each — when they were all zero); a rounding shortfall goes to the next bar (the first one
    /// after the last). Writes the shares to the organisation.
    /// </summary>
    private void Rebalance(int k)
    {
        var p = new float[5];
        float others = 0;
        for (var i = 0; i < 5; i++)
        {
            p[i] = PercentOf(_widths[i]);
            if (i != k)
                others += p[i];
        }
        var rest = 100f - p[k];
        for (var i = 0; i < 5; i++)
            if (i != k)
                p[i] = others > 0 ? p[i] * rest / others : rest / 4f;
        float total = 0;
        foreach (var v in p)
            total += v;
        if (total < 100f)
            p[k < 4 ? k + 1 : 0] += 100f - total;
        for (var i = 0; i < 5; i++)
        {
            if (i != k)
                _widths[i] = WidthOf(p[i]);
            if (_org != null)
                _org.Shares[i] = p[i];
        }
    }

    private void SyncFromOrg()
    {
        var org = Teams.GetCurrentPlayerTeam()?.Org;
        if (org == _org)
            return;
        _org = org;
        for (var i = 0; i < 5; i++)
            _widths[i] = org == null ? 0 : Mathf.Clamp(WidthOf(org.Shares[i]), 0, FullWidth);
    }

    private static Rect2 PanelRect(Rect2I r) => new(r.Position.X, r.Position.Y - PanelInfo.PanelTop, r.Size.X, r.Size.Y);

    private int RowAt(Vector2 local)
    {
        for (var i = 0; i < Rows.Length; i++)
            if (PanelRect(Rows[i]).HasPoint(local))
                return i;
        return -1;
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _dragRow = RowAt(button.Position);
                    if (_dragRow < 0)
                        return;
                    Drag(button.Position.X);
                    AcceptEvent();
                }
                else if (_dragRow >= 0)
                {
                    _dragRow = -1;
                    AcceptEvent();
                }
                break;
            case InputEventMouseMotion motion:
                _hoverRow = RowAt(motion.Position);
                if (_dragRow >= 0)
                {
                    Drag(motion.Position.X);
                    AcceptEvent();
                }
                QueueRedraw();
                break;
        }
    }

    private void Drag(float x)
    {
        SyncFromOrg();
        _widths[_dragRow] = Mathf.Clamp((int)(x - Rows[_dragRow].Position.X), 0, FullWidth);
        Rebalance(_dragRow);
        QueueRedraw();
    }

    public override bool _HasPoint(Vector2 point) => RowAt(point) >= 0;

    public override void _Process(double delta) => SyncFromOrg();

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hoverRow < 0)
            return;
        _hoverRow = -1;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_coins != null)
            for (var i = 0; i < 5; i++)
            {
                var w = Mathf.Min(_widths[i], 132);
                if (w > 0)
                    DrawTextureRectRegion(_coins, new Rect2(Rows[i].Position.X, Rows[i].Position.Y - PanelInfo.PanelTop, w, 20), new Rect2(16, 43, w, 20));
            }
        if (_hoverRow < 0 || _org == null)
            return;
        var (share, favours, favour) = Texts[_hoverRow];
        OriginalFonts.DrawPanel(this, PanelInfo.CFormat(OriginalTexts.Instance.Get(5, share), _org.Shares[_hoverRow]), 232, 555 - PanelInfo.PanelTop, 248, 1, 2);
        if (favours < 0)
            return;
        var points = _org.Favour[_hoverRow];
        OriginalFonts.DrawPanel(this, PanelInfo.CFormat(OriginalTexts.Instance.Get(5, (int)points == 1 ? favour : favours), points), 232, 573 - PanelInfo.PanelTop, 248, 1, 2);
    }
}
