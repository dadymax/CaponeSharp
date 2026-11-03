using Godot;

namespace Capone;

/// <summary>
/// The label over the map object under the mouse (sub_40C2C2, from 0x40C9FB): the object's name, or — while an own unit
/// is selected — what the chosen order would do to it ("Extort %s", "Attack %s", "Destroy %s", "Terminate %s", texts
/// 5,40–62). Drawn at the object's map position (vt+60h → screen, sub_414A30) when that point lies in the map view,
/// with the panel text routine sub_40F126: font 3, width 150, centred, coloured by the relation (vt+0E4h): 0 / 4 green
/// (own, none), 1 red (enemy), 2 yellow (ally), 3 light green (nobody's).
/// </summary>
public partial class MapHint : Control
{
    /// <summary>The selected unit's order mode (panel slot +4, sub_414DC0): 0 Go / default, 1 Attack, 2 Extort, 4 Destroy, 5 Force Go, 6 Force Attack; -1 none.</summary>
    public const int NoOrder = -1;

    private string _text = "";
    private int _colour;
    private Vector2 _at;

    public MapHint()
    {
        Name = "MapHint";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>
    /// The text id (section 5) for an object, or -1 for its plain name. <paramref name="building"/>: a company or HQ
    /// (class 0 / 9); <paramref name="relation"/>: vt+0E4h (0 own, 1 enemy, 2 ally, 3 nobody's, 4 none).
    /// </summary>
    public static int TextId(bool building, bool hq, int relation, int mode)
    {
        if (mode == NoOrder)
            return -1;
        if (building)
            return (relation, mode) switch
            {
                (1, 0 or 5) => hq ? 40 : 41,
                (1, 1 or 6) => 42,
                (1, 2) => 43,
                (1, 4) => 44,
                (2, 0 or 5) => hq ? 45 : 46,
                (2, 1 or 6) => 47,
                (2, 2) => 48,
                (2, 4) => 49,
                (3, 0 or 5) => hq ? 50 : 51,
                (3, 6) => 52,
                (3, 2) => 53,
                (3, 4) => 54,
                _ => -1,
            };
        return (relation, mode) switch
        {
            (1, 0 or 5) => 55,
            (1, 1 or 6) => 56,
            (1, 2) => 57,
            (1, 4) => 58,
            (2, 0 or 5) => 59,
            (2, 1 or 6) => 60,
            (2, 2) => 61,
            (2, 4) => 62,
            _ => -1,
        };
    }

    public void Show(string text, int colour, Vector2 screen)
    {
        if (text == _text && colour == _colour && screen == _at)
            return;
        _text = text ?? "";
        _colour = colour;
        _at = screen;
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Only while the object's point is inside the map view (+900h..+90Ch: the 800 × 380 area above the panel).
        if (_text.Length == 0 || _at.X <= 0 || _at.X >= 800 || _at.Y <= 0 || _at.Y >= 380)
            return;
        OriginalFonts.DrawPanel(this, _text, _at.X, _at.Y, 150, 103, _colour);
    }
}
