using System;
using Godot;

namespace Capone;

/// <summary>
/// The mouse cursors of Capone.EXE (fnLegalCrimeMainWindowMain 0x419F10: LoadCursor "ArrowCur", "GotoCur", "AttackCu",
/// "ProtectCu"; PE cursor groups → monochrome 32 × 32 cursors with a hotspot). sub_41E970(n) picks one: 0 arrow,
/// 1 go, 2 attack, 3 protect. An armed order shows its cursor until the map click carries it out (sub_410EC3 keys
/// G / O → 1, A → 2, E → 3; the action bar the same); then the arrow again.
/// </summary>
public static class OriginalCursors
{
    public enum Kind { Arrow, Goto, Attack, Protect }

    private static readonly string[] Names = { "ARROWCUR", "GOTOCUR", "ATTACKCU", "PROTECTCU" };
    private static readonly (Image Image, Vector2 Hotspot)?[] Loaded = new (Image, Vector2)?[4];
    private static bool _tried;
    private static Kind _current = (Kind)(-1);

    public static Kind Current => _current;

    private static void Load()
    {
        if (_tried)
            return;
        _tried = true;
        var exe = OriginalExe.Instance;
        for (var i = 0; i < Names.Length; i++)
        {
            var group = exe.ReadResource(12, Names[i]);
            if (group == null || group.Length < 20)
            {
                GD.PrintErr($"OriginalCursors: no cursor group {Names[i]}");
                continue;
            }
            // NEWHEADER (6 bytes) + the first RESDIR (14 bytes): the cursor's resource id is its last word.
            var id = BitConverter.ToUInt16(group, 6 + 12);
            var data = exe.ReadResource(1, null, id);
            Loaded[i] = data == null ? null : Decode(data);
        }
    }

    /// <summary>A cursor resource: hotspot (2 words), BITMAPINFOHEADER, palette, XOR then AND mask, rows bottom-up.</summary>
    internal static (Image, Vector2)? Decode(byte[] data)
    {
        var hotspot = new Vector2(BitConverter.ToUInt16(data, 0), BitConverter.ToUInt16(data, 2));
        const int header = 4;
        var headerSize = BitConverter.ToInt32(data, header);
        var width = BitConverter.ToInt32(data, header + 4);
        var height = BitConverter.ToInt32(data, header + 8) / 2;
        var bits = BitConverter.ToUInt16(data, header + 14);
        if (bits != 1 || width <= 0 || height <= 0)
            return null;
        var palette = header + headerSize;
        Color PaletteColor(int index)
        {
            var o = palette + 4 * index;
            return Color.Color8(data[o + 2], data[o + 1], data[o]);
        }
        var stride = (width + 31) / 32 * 4;
        var xor = palette + 8;
        var and = xor + stride * height;
        if (and + stride * height > data.Length)
            return null;
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var row = height - 1 - y;
                var bit = 0x80 >> (x & 7);
                var x1 = (data[xor + row * stride + x / 8] & bit) != 0;
                var a1 = (data[and + row * stride + x / 8] & bit) != 0;
                Color c;
                if (!a1)
                    c = PaletteColor(x1 ? 1 : 0);
                else if (x1)
                    c = Colors.Black; // inverts the screen in Windows; shown as black
                else
                    c = new Color(0, 0, 0, 0);
                image.SetPixel(x, y, c);
            }
        return (image, hotspot);
    }

    /// <summary>sub_41E970: shows cursor <paramref name="kind"/>.</summary>
    public static void Set(Kind kind)
    {
        if (kind == _current)
            return;
        Load();
        var cursor = Loaded[(int)kind];
        if (cursor == null)
            return;
        _current = kind;
        Input.SetCustomMouseCursor(ImageTexture.CreateFromImage(cursor.Value.Image), Input.CursorShape.Arrow, cursor.Value.Hotspot);
    }
}
