using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// The animation on the point of a map order (map view +0A28h target, +0A24h frame; set by the map click sub_411F84
/// and the order handlers at 0x413FD8 / 0x4141BE / 0x41438A, drawn by sub_40B914): Chicago.tile 0AD3h "Attack cursor"
/// (Signs.bmp, 6 frames of 36 × 33 at (288, 66)) plays once, one frame per map redraw (the redraw timer is about
/// 100 ms, sub_4198A0). It is drawn bottom-centred on the target: a unit (vt+94h) at its walk position 2 px up,
/// anything else (the clicked tile, a company) at its cell 1 px down. Then the target is cleared.
/// </summary>
public partial class OrderMarker : Node2D
{
    public const string TileName = "Attack cursor";
    public const double FrameSeconds = 0.1;

    private readonly List<Texture2D> _frames = new();
    private Node2D _unit;
    private Vector2 _bottom;
    private int _frame = -1;
    private double _time;

    public override void _Ready()
    {
        Name = "OrderMarker";
        ZIndex = 100;
        TextureFilter = TextureFilterEnum.Nearest;
        var tile = OriginalData.Instance.Tiles.ResolveTile(TileName);
        if (tile != null)
            foreach (var image in OriginalData.Instance.GetImageForTile(tile))
                _frames.Add(ImageTexture.CreateFromImage(image));
        Visible = false;
    }

    /// <summary>Plays the animation on a unit (it follows the unit) or, with <paramref name="unit"/> null, on a cell.</summary>
    public void Play(Node2D unit, TileMapLayer map, Vector2I cell)
    {
        if (_frames.Count == 0)
            return;
        _unit = unit;
        if (unit == null)
            _bottom = map.ToGlobal(map.MapToLocal(cell) + new Vector2(0, map.TileSet.TileSize.Y / 2f + 1));
        _frame = 0;
        _time = 0;
        Visible = true;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_frame < 0)
            return;
        if (_unit != null && !IsInstanceValid(_unit))
            _unit = null;
        _time += delta;
        while (_time >= FrameSeconds && _frame >= 0)
        {
            _time -= FrameSeconds;
            if (++_frame >= _frames.Count)
            {
                _frame = -1;
                Visible = false;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_frame < 0 || _frame >= _frames.Count)
            return;
        // Units stand with their feet about 2 px below their position (sprite centred 14 px up).
        var bottom = _unit != null ? _unit.GlobalPosition : _bottom;
        var texture = _frames[_frame];
        var size = texture.GetSize();
        DrawTexture(texture, ToLocal(bottom) - new Vector2(Mathf.Floor(size.X / 2), size.Y));
    }
}
