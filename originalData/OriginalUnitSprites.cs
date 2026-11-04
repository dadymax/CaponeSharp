using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Animation set of one unit type for one owner, built from Chicago.tile records (see OriginalTiles.ResolveTile).</summary>
public class OriginalUnitSprites
{
    // Member names double as animation names, so they stay lower-case.
    public enum SpriteLayersEnum { idle, moveSE, moveNE, moveNW, moveSW, attackSE, attackNE, attackNW, attackSW, killed, killed2 }
    public enum SlType { idle, move, attack, killed }

    public static readonly Dictionary<SpriteLayersEnum, StringName> SpriteLayersDict = new();
    public static readonly List<StringName> SpriteLayers = new();

    /// <summary>Tile references that did not resolve, for the smoke test.</summary>
    public static HashSet<string> MissingReferences { get; } = new();

    static OriginalUnitSprites()
    {
        foreach (SpriteLayersEnum layer in Enum.GetValues<SpriteLayersEnum>())
        {
            var name = new StringName(layer.ToString());
            SpriteLayers.Add(name);
            SpriteLayersDict[layer] = name;
        }
    }

    /// <summary>Animation layer name → Chicago.tile reference. Empty references mean "no such animation".</summary>
    public Dictionary<StringName, string> SearchLineDict { get; set; } = new();

    public static StringName IdleName => SpriteLayersDict[SpriteLayersEnum.idle];

    public static string GetAnimLayerName(SlType type, MapTileMath.Directions direction = MapTileMath.Directions.SE)
    {
        var result = type.ToString();
        if (type is SlType.move or SlType.attack)
            result += direction.ToString();
        return result;
    }

    /// <summary>Checks every reference and returns the unresolved ones.</summary>
    public IEnumerable<string> Validate()
    {
        foreach (var reference in SearchLineDict.Values)
            if (!string.IsNullOrEmpty(reference) && OriginalData.Instance.Tiles.ResolveTile(reference) == null)
                yield return reference;
    }

    /// <summary>SpriteFrames meta key: index of the first firing frame of an attack layer.</summary>
    public static StringName ShotStartMeta(StringName layer) => "shot_start_" + layer;

    /// <summary>
    /// First firing frame of an attack strip: the Sniper's strips repeat the aiming picture for 10 frames and fire in
    /// the last 2 (Chicago.tile "Own Snipper attack SE": 10 × (0,604), then (40,604), (80,604)); a strip without a
    /// repeated first picture fires all through (0).
    /// </summary>
    public static int ShotStart(IReadOnlyList<Vector2I> pairs)
    {
        if (pairs.Count < 2 || pairs[1] != pairs[0])
            return 0;
        for (var i = 1; i < pairs.Count; i++)
            if (pairs[i] != pairs[0])
                return i;
        return 0;
    }

    public void FillSpriteFrames(SpriteFrames sf)
    {
        var shotStarts = new Dictionary<StringName, int>();
        var loaded = new Dictionary<StringName, List<Texture2D>>();
        foreach (var layer in SpriteLayers)
        {
            if (!SearchLineDict.TryGetValue(layer, out var reference) || string.IsNullOrEmpty(reference))
                continue;
            var tile = OriginalData.Instance.Tiles.ResolveTile(reference);
            if (tile == null)
            {
                if (MissingReferences.Add(reference))
                    GD.PrintErr("OriginalUnitSprites: no tile for: ", reference);
                continue;
            }
            if (layer.ToString().StartsWith("attack"))
                shotStarts[layer] = ShotStart(tile.Pairs);
            var frames = new List<Texture2D>();
            foreach (var image in OriginalData.Instance.GetImageForTile(tile))
                frames.Add(ImageTexture.CreateFromImage(image));
            loaded[layer] = frames;
        }

        // Every layer must exist so Play() never fails: fill gaps from the closest animation the original has.
        foreach (var layer in SpriteLayers)
        {
            var frames = loaded.GetValueOrDefault(layer) ?? Fallback(layer, loaded);
            if (sf.HasAnimation(layer))
                sf.RemoveAnimation(layer);
            sf.AddAnimation(layer);
            // Falling down and lying there: the death pictures (2 frames) play once and hold the last frame.
            if (layer == SpriteLayersDict[SpriteLayersEnum.killed] || layer == SpriteLayersDict[SpriteLayersEnum.killed2])
                sf.SetAnimationLoop(layer, false);
            if (frames == null)
                continue;
            foreach (var texture in frames)
                sf.AddFrame(layer, texture);
            sf.SetMeta(ShotStartMeta(layer), shotStarts.GetValueOrDefault(layer));
        }
    }

    private static List<Texture2D> Fallback(StringName layer, Dictionary<StringName, List<Texture2D>> loaded)
    {
        var name = layer.ToString();
        var direction = name.Length > 2 ? name[^2..] : "SE";
        string[] order = name switch
        {
            _ when name.StartsWith("move") => new[] { "idle" },
            _ when name.StartsWith("attack") => new[] { "idle", "move" + direction },
            "idle" => new[] { "moveSE", "moveSW", "moveNE", "moveNW" },
            "killed2" => new[] { "killed" },
            _ => Array.Empty<string>(),
        };
        foreach (var candidate in order)
            if (loaded.TryGetValue(candidate, out var frames))
                return frames;
        return null;
    }
}
