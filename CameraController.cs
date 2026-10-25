using Godot;

namespace Capone;

/// <summary>Arrow keys pan, mouse wheel zooms.</summary>
public partial class CameraController : Camera2D
{
    private const float MinZoom = 0.1f;
    private const float MaxZoom = 5f;

    [Export] public float Speed { get; set; } = 20;

    public void CameraZoomChange(bool up)
    {
        var value = up ? Mathf.Min(MaxZoom, Zoom.X + 0.1f) : Mathf.Max(MinZoom, Zoom.X - 0.1f);
        Zoom = new Vector2(value, value);
    }

    public override void _Input(InputEvent @event)
    {
        if (Input.IsMouseButtonPressed(MouseButton.WheelUp))
        {
            CameraZoomChange(true);
            return;
        }
        if (Input.IsMouseButtonPressed(MouseButton.WheelDown))
        {
            CameraZoomChange(false);
            return;
        }

        var x = Input.IsKeyPressed(Key.Right) ? 1 : Input.IsKeyPressed(Key.Left) ? -1 : 0;
        var y = Input.IsKeyPressed(Key.Down) ? 1 : Input.IsKeyPressed(Key.Up) ? -1 : 0;
        Position += new Vector2(x, y) * Speed;
    }
}
