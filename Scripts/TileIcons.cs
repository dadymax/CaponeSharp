using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// Panel button icons from Chicago.tile. The EXE draws them with frame −1 (ButtonBar sub_402070 → TileSet::Draw
/// sub_437780), i.e. animated by the tile set's shared frame counter, all the time — most action icons have 12
/// frames (some 6). A tile with several frames becomes an <see cref="AnimatedTexture"/> at the remake's usual 5 fps.
/// </summary>
public static class TileIcons
{
    public const float Fps = 5f;

    private static readonly Dictionary<int, Texture2D> Cache = new();

    public static Texture2D Get(int tileIndex)
    {
        if (Cache.TryGetValue(tileIndex, out var texture))
            return texture;
        var tile = OriginalData.Instance.Tiles.GetTileByIndex(tileIndex);
        var images = tile != null ? OriginalData.Instance.GetImageForTile(tile) : null;
        if (images is not { Count: > 0 })
            texture = null;
        else if (images.Count == 1)
            texture = ImageTexture.CreateFromImage(images[0]);
        else
        {
            var animated = new AnimatedTexture { Frames = Mathf.Min(images.Count, 256) };
            for (var i = 0; i < animated.Frames; i++)
            {
                animated.SetFrameTexture(i, ImageTexture.CreateFromImage(images[i]));
                animated.SetFrameDuration(i, 1f / Fps);
            }
            texture = animated;
        }
        Cache[tileIndex] = texture;
        return texture;
    }
}
