using Godot;

namespace Capone;

/// <summary>
/// The message lines over the map (sub_40C2C2): each message of the last 15 seconds (sub_43C6E0 drops older ones) at
/// (25, 13 + 16n), panel font 3, width 700, oldest first; kind 4 green, kinds 2 / 3 / 5 red, others yellow.
/// A message with a map position (FBI reports "…at the following address…") centres the view there when clicked —
/// an assumption: the original keeps a position with every message (sub_40FFD0) but its use is not traced.
/// </summary>
public partial class MessageLog : Control
{
    public const int Seconds = 15;

    public MessageLog()
    {
        Name = "MessageLog";
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready() => SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

    public override void _Process(double delta) => QueueRedraw();

    private readonly System.Collections.Generic.List<(Rect2 Rect, Vector2I Cell)> _links = new();

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click || _links.Count == 0)
            return;
        foreach (var (rect, cell) in _links)
        {
            if (!rect.HasPoint(click.Position) || Main.Instance?.Map is not { } map)
                continue;
            if (Main.Instance.GetNodeOrNull<Camera2D>("Camera2D") is { } camera)
                camera.GlobalPosition = map.ToGlobal(map.MapToLocal(cell));
            GetViewport().SetInputAsHandled();
            return;
        }
    }

    public override void _Draw()
    {
        var now = Time.GetTicksMsec();
        var y = 13;
        _links.Clear();
        foreach (var m in Messages.Log)
        {
            if (now - m.Ticks > Seconds * 1000UL || m.Kind is Messages.Lost or Messages.Won)
                continue;
            var colour = m.Kind switch { Messages.Good => 0, 5 => 1, _ => 2 };
            if (m.Cell is { } cell)
                _links.Add((new Rect2(25, y, 700, 16), cell));
            foreach (var line in m.Text.Replace("\r", "").Split('\n'))
            {
                OriginalFonts.DrawPanel(this, line, 25, y, 700, 3, colour);
                y += 16;
            }
        }
    }
}
