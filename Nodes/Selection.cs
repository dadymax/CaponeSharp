using Godot;

namespace Capone;

/// <summary>Animated selection marker ("Select cursor" tile) shown above selected units/companies.</summary>
public partial class Selection : Node2D
{
    private static readonly StringName AnimName = "SelectionTriangle";
    private static SpriteFrames _animation;

    private static SpriteFrames Animation
    {
        get
        {
            if (_animation != null)
                return _animation;
            _animation = new SpriteFrames();
            _animation.AddAnimation(AnimName);
            _animation.SetAnimationLoopMode(AnimName, SpriteFrames.LoopMode.None);
            var tile = OriginalData.Instance.Tiles.GetRecordWithComment("Select cursor");
            foreach (var image in OriginalData.Instance.GetImageForTile(tile))
                _animation.AddFrame(AnimName, ImageTexture.CreateFromImage(image));
            return _animation;
        }
    }

    private AnimatedSprite2D _as2d;

    public bool Enabled
    {
        get => _as2d?.Visible ?? false;
        set
        {
            if (_as2d == null)
                return;
            _as2d.Visible = value;
            if (value)
            {
                _as2d.Frame = 0;
                _as2d.Play();
            }
            else
            {
                _as2d.Stop();
            }
        }
    }

    public override void _Ready()
    {
        _as2d = FindChild("AnimatedSprite2D") as AnimatedSprite2D;
        if (_as2d == null)
            return;
        _as2d.SpriteFrames = Animation;
        _as2d.Animation = AnimName;
        _as2d.Frame = 0;
        _as2d.Stop();
        _as2d.Visible = false;
        ZIndex = (int)RenderingServer.CanvasItemZMax;
    }
}
