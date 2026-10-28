using Godot;

namespace Capone;

/// <summary>
/// The original's GDI text drawing. Fonts from WinMain (0x41A0D3…, read by GetFont sub_41AB73), all Arial with the
/// Cyrillic charset, by cell height / weight: 0 = 14/400, 1 = 16/500, 2 = 24/700, 3 = 16/700, 4 = 28/700.
/// Both text routines draw a 0x101010 shadow at +2,+2, cut the text to the given width and centre it on x when the
/// font argument is 100 or more; they differ only in their colour tables.
/// </summary>
public static class OriginalFonts
{
    private static readonly (int Height, int Weight)[] Specs = { (14, 400), (16, 500), (24, 700), (16, 700), (28, 700) };
    private static readonly Font[] Fonts = new Font[Specs.Length];
    private static readonly Color Shadow = Color.Color8(16, 16, 16);

    /// <summary>Panel routine sub_40F126: green, red, yellow (default), light green, brown.</summary>
    public static readonly Color[] PanelColors =
    {
        Color.Color8(0, 255, 0), Color.Color8(214, 74, 74), Color.Color8(255, 247, 123),
        Color.Color8(140, 239, 198), Color.Color8(82, 66, 24),
    };

    /// <summary>Screen routine fnPrintText_0 (sub_41A648): green, red, yellow (default), blue, brown, brown without shadow.</summary>
    public static readonly Color[] ScreenColors =
    {
        Color.Color8(0, 255, 0), Color.Color8(255, 0, 0), Color.Color8(255, 255, 0),
        Color.Color8(0, 0, 255), Color.Color8(82, 66, 24), Color.Color8(82, 66, 24),
    };

    public static Font Get(int index)
    {
        index = Mathf.Clamp(index, 0, Specs.Length - 1);
        return Fonts[index] ??= new SystemFont { FontNames = new[] { "Arial" }, FontWeight = Specs[index].Weight };
    }

    // Godot sizes fonts by em; a Windows cell height is ascent + descent ≈ em × 2288 / 2048 for Arial.
    public static int Size(int index) => Mathf.RoundToInt(Specs[Mathf.Clamp(index, 0, Specs.Length - 1)].Height * 2048f / 2288f);

    /// <summary>
    /// Draws <paramref name="text"/> with its top-left at (<paramref name="x"/>, <paramref name="y"/>) in the canvas item's
    /// coordinates (top-centre when <paramref name="font"/> ≥ 100, top-right when ≥ 200), cut to <paramref name="width"/> pixels.
    /// </summary>
    public static void Draw(CanvasItem canvas, string text, float x, float y, int width, int font, Color color, bool shadow = true)
    {
        if (string.IsNullOrEmpty(text))
            return;
        // fnPrintText_0: font 200+ right-aligns on x (TA_RIGHT), 100+ centres (TA_CENTER).
        var right = font >= 200;
        var centred = !right && font >= 100;
        if (right)
            font -= 200;
        else if (centred)
            font -= 100;
        var f = Get(font);
        var size = Size(font);
        while (text.Length > 1 && f.GetStringSize(text, HorizontalAlignment.Left, -1, size).X > width)
            text = text[..^1];
        if (centred)
            x -= f.GetStringSize(text, HorizontalAlignment.Left, -1, size).X / 2;
        else if (right)
            x -= f.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var pos = new Vector2(x, y + f.GetAscent(size));
        if (shadow)
            canvas.DrawString(f, pos + new Vector2(2, 2), text, HorizontalAlignment.Left, -1, size, Shadow);
        canvas.DrawString(f, pos, text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>fnPrintText_0 colours; colour 5 has no shadow.</summary>
    public static void DrawScreen(CanvasItem canvas, string text, float x, float y, int width, int font, int color) =>
        Draw(canvas, text, x, y, width, font, ScreenColors[color is >= 0 and < 6 ? color : 2], color != 5);

    public static void DrawPanel(CanvasItem canvas, string text, float x, float y, int width, int font, int color) =>
        Draw(canvas, text, x, y, width, font, PanelColors[color is >= 0 and < 5 ? color : 2]);
}
