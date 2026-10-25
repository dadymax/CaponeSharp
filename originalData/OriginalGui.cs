using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Cached GUI bitmaps from the original game.</summary>
public static class OriginalGui
{
    public enum GuiElements { GAMEBACK }

    private static readonly Dictionary<GuiElements, OriginalBmps.Bmp> Cache = new();

    static OriginalGui()
    {
        foreach (var key in Enum.GetValues<GuiElements>())
        {
            if (OriginalData.Instance.Bmps.Bmps.TryGetValue(key + ".BMP", out var bmp))
                Cache[key] = bmp;
            else
                GD.PrintErr("OriginalGui: missing bitmap ", key, ".BMP");
        }
    }

    public static Image GetGuiElement(GuiElements element) =>
        Cache.TryGetValue(element, out var bmp) ? bmp.Body : null;
}
