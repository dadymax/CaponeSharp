using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Animation set of one unit type for one owner, built from Chicago.tile records found by comment.</summary>
public class OriginalUnitSprites
{
    // Member names double as animation names, so they stay lower-case.
    public enum SpriteLayersEnum { idle, moveSE, moveNE, moveNW, moveSW, attackSE, attackNE, attackNW, attackSW, killed, killed2 }
    public enum SlType { idle, move, attack, killed }

    public static readonly Dictionary<SpriteLayersEnum, StringName> SpriteLayersDict = new();
    public static readonly List<StringName> SpriteLayers = new();

    static OriginalUnitSprites()
    {
        foreach (SpriteLayersEnum layer in Enum.GetValues<SpriteLayersEnum>())
        {
            var name = new StringName(layer.ToString());
            SpriteLayers.Add(name);
            SpriteLayersDict[layer] = name;
        }
    }

    /// <summary>Animation layer name → Chicago.tile comment to search.</summary>
    public Dictionary<StringName, string> SearchLineDict { get; set; } = new();

    public static StringName IdleName => SpriteLayersDict[SpriteLayersEnum.idle];

    public static string GetAnimLayerName(SlType type, MapTileMath.Directions direction = MapTileMath.Directions.SE)
    {
        var result = type.ToString();
        if (type is SlType.move or SlType.attack)
            result += direction.ToString();
        return result;
    }

    public void FillSpriteFrames(SpriteFrames sf)
    {
        foreach (var layer in SpriteLayers)
        {
            if (!SearchLineDict.TryGetValue(layer, out var comment))
                continue;
            sf.RemoveAnimation(layer);
            sf.AddAnimation(layer);
            var tile = OriginalData.Instance.Tiles.GetRecordWithComment(comment);
            if (tile == null)
            {
                GD.PrintErr("OriginalUnitSprites: no tile with comment: ", comment);
                continue;
            }
            foreach (var image in OriginalData.Instance.GetImageForTile(tile))
                sf.AddFrame(layer, ImageTexture.CreateFromImage(image));
        }
    }
}
