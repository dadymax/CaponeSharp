using Godot;

namespace Capone;

/// <summary>
/// Arrow keys pan, mouse wheel zooms, and the cursor at an edge of the screen scrolls the map (TODO item 24). Edge
/// scrolling follows the EXE: the mouse handler (sub_413A04) marks x ≤ 1 / x ≥ 798 and y ≤ 1 / y ≥ 598 (+924h /
/// +928h), and the map view (0x40D3F4) moves by a step that starts at 10 pixels, grows by 10 per step up to 50, and
/// starts again at 10 after a 350 ms break (+0AECh, +0AE8h). The EXE steps once per redraw; the remake steps
/// <see cref="EdgeStepsPerSecond"/> times a second. With a scaled window the edge is the window's edge (the cursor
/// over a black bar is past the 800 × 600 picture, so it counts). During an episode the mouse is kept inside the
/// window (user request); it is let go while the game is paused or no episode runs.
/// </summary>
public partial class CameraController : Camera2D
{
    private const float MinZoom = 0.1f;
    private const float MaxZoom = 5f;

    public const int EdgePixels = 2;
    public const int EdgeStepFirst = 10, EdgeStepGrowth = 10, EdgeStepMax = 50;
    public const double EdgeBreakSeconds = 0.35;
    public const double EdgeStepsPerSecond = 20;

    [Export] public float Speed { get; set; } = 20;

    private int _edgeStep = EdgeStepFirst;
    private double _sinceStep;
    private double _sinceLastScroll = double.MaxValue;

    // Runs while paused too, so it can let go of the mouse; scrolling and keys stay off then.
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public void CameraZoomChange(bool up)
    {
        var value = up ? Mathf.Min(MaxZoom, Zoom.X + 0.1f) : Mathf.Max(MinZoom, Zoom.X - 0.1f);
        Zoom = new Vector2(value, value);
    }

    public override void _Input(InputEvent @event)
    {
        if (GetTree().Paused)
            return;
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

    /// <summary>Direction (−1, 0, 1 per axis) of edge scrolling for a cursor at <paramref name="mouse"/>.</summary>
    public static Vector2I EdgeDirection(Vector2 mouse, Vector2 screen) => new(
        mouse.X <= EdgePixels - 1 ? -1 : mouse.X >= screen.X - EdgePixels ? 1 : 0,
        mouse.Y <= EdgePixels - 1 ? -1 : mouse.Y >= screen.Y - EdgePixels ? 1 : 0);

    /// <summary>The EXE's accelerating step: 10, 20 … 50 pixels; back to 10 after a break.</summary>
    public static int NextStep(int step, double secondsSinceLastScroll) =>
        secondsSinceLastScroll > EdgeBreakSeconds ? EdgeStepFirst : Mathf.Min(EdgeStepMax, step + EdgeStepGrowth);

    public override void _Process(double delta)
    {
        var inEpisode = Main.Instance?.Map != null && !GetTree().Paused;
        var wanted = inEpisode && DisplayServer.GetName() != "headless" ? Input.MouseModeEnum.Confined : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != wanted)
            Input.MouseMode = wanted;
        if (!inEpisode || !GetWindow().HasFocus())
            return;

        var realDelta = delta / Engine.TimeScale;
        _sinceLastScroll += realDelta;
        var dir = EdgeDirection(GetViewport().GetMousePosition(), GetViewport().GetVisibleRect().Size);
        if (dir == Vector2I.Zero)
        {
            _sinceStep = 0;
            return;
        }
        _sinceStep += realDelta;
        var period = 1.0 / EdgeStepsPerSecond;
        while (_sinceStep >= period)
        {
            _sinceStep -= period;
            _edgeStep = NextStep(_edgeStep, _sinceLastScroll);
            _sinceLastScroll = 0;
            Position += (Vector2)dir * _edgeStep / Zoom.X;
        }
    }
}
