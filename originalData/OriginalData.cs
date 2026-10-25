using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Entry point to everything loaded from the original game folder (path comes from original.cfg).</summary>
public class OriginalData
{
    public static readonly Vector2I StandartTile = new(40, 19);

    private static OriginalData _instance;
    public static OriginalData Instance => _instance ??= new OriginalData();

    public string OriginalGameDirectory { get; }

    public OriginalTiles Tiles => OriginalTiles.Instance;
    public OriginalMaps Maps => OriginalMaps.Instance;
    public OriginalBmps Bmps => OriginalBmps.Instance;
    public OriginalBlocks BuildingBlocks => OriginalBlocks.Instance;

    private OriginalData()
    {
        var cfg = new ConfigFile();
        var err = cfg.Load("res://originalData/original.cfg");
        if (err != Error.Ok)
            GD.PrintErr("OriginalData: failed to load res://originalData/original.cfg: ", err);
        OriginalGameDirectory = cfg.GetValue("directory", "originalGameDirectory", "").AsString();
    }

    /// <summary>Cuts every frame of a Chicago.tile record out of its bitmap.</summary>
    public List<Image> GetImageForTile(OriginalTiles.SingleTileLine tile)
    {
        var result = new List<Image>();
        if (tile == null)
            return result;

        var bmpName = Tiles.Bmps[tile.BmpInd];
        var bmpImage = Bmps.Bmps[bmpName].Body;
        foreach (var pair in tile.Pairs)
        {
            var image = Image.CreateEmpty(tile.Size.X, tile.Size.Y, false, Image.Format.Rgba8);
            image.BlitRect(bmpImage, new Rect2I(pair, tile.Size), Vector2I.Zero);
            result.Add(image);
        }
        return result;
    }
}
