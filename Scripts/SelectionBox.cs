using Godot;

namespace Capone;

/// <summary>The rectangle drawn while the player drags a box selection.</summary>
public partial class SelectionBox : Node2D
{
	private Rect2 _rect;

	public SelectionBox()
	{
		ZIndex = (int)RenderingServer.CanvasItemZMax;
		Visible = false;
	}

	public void Show(Vector2 a, Vector2 b)
	{
		_rect = new Rect2(a, Vector2.Zero).Expand(b);
		Visible = true;
		QueueRedraw();
	}

	public override void _Draw()
	{
		DrawRect(_rect, new Color(0.6f, 1f, 0.6f, 0.12f));
		DrawRect(_rect, new Color(0.6f, 1f, 0.6f, 0.9f), false, 1f);
	}
}
