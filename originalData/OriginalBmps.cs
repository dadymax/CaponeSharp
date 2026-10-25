using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>All bitmaps from original_game/PICS. Files are BMPs with every byte XOR 0xCD; loaded lazily.</summary>
public class OriginalBmps
{
    private static OriginalBmps _instance;
    public static OriginalBmps Instance => _instance ??= new OriginalBmps();

    /// <summary>Upper-case file name → bitmap.</summary>
    public Dictionary<string, Bmp> Bmps { get; } = new();

    private OriginalBmps()
    {
        var path = OriginalData.Instance.OriginalGameDirectory + "/PICS";
        var files = DirAccess.GetFilesAt(path);
        if (files.Length == 0)
        {
            GD.PrintErr("OriginalBmps: no files found at ", path);
            return;
        }
        foreach (var file in files)
        {
            var bmp = Bmp.Create(path + "/" + file);
            Bmps.TryAdd(bmp.Name, bmp);
        }
    }

    public void FreeCache()
    {
        foreach (var bmp in Bmps.Values)
            bmp.Unload();
    }

    public class Bmp
    {
        // Pixels of these two colours are the original game's transparency keys.
        private static readonly Color Color1 = Color.Color8(79, 55, 74);
        private static readonly Color Color2 = Color.Color8(74, 49, 74);

        private Image _body;
        private string _fullPath;

        public string Name { get; private set; }

        public Image Body
        {
            get
            {
                if (_body == null)
                    ActualLoad();
                return _body;
            }
        }

        public static Bmp Create(string path) => new()
        {
            _fullPath = path,
            Name = path.GetFile().ToUpperInvariant(),
        };

        public void Unload() => _body = null;

        private void ActualLoad()
        {
            // TODO: make case for vanilla Legal Crime
            var bytes = FileAccess.GetFileAsBytes(_fullPath);
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] ^= 0xCD;

            var image = new Image();
            image.LoadBmpFromBuffer(bytes);
            image.Convert(Image.Format.Rgba8);

            // Work on the raw RGBA buffer: much faster than GetPixel/SetPixel per pixel.
            var data = image.GetData();
            byte r1 = (byte)Color1.R8, g1 = (byte)Color1.G8, b1 = (byte)Color1.B8;
            byte r2 = (byte)Color2.R8, g2 = (byte)Color2.G8, b2 = (byte)Color2.B8;
            for (var i = 0; i + 3 < data.Length; i += 4)
            {
                var r = data[i];
                var g = data[i + 1];
                var b = data[i + 2];
                if ((r == r1 && g == g1 && b == b1) || (r == r2 && g == g2 && b == b2))
                {
                    data[i] = 0;
                    data[i + 1] = 0;
                    data[i + 2] = 0;
                    data[i + 3] = 0;
                }
            }
            image.SetData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, data);
            _body = image;
        }
    }
}
