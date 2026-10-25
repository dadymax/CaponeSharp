using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Main scene: loads the campaign, builds the map, handles selection and orders.</summary>
public partial class Main : Node2D
{
	private const string DefaultMap = "BigEight.map";

	public TileMapLayer Map { get; private set; }

	private readonly List<ISelectable> _selected = new();
	private bool _isMouseDrag;
	private Vector2 _dragStartGlobal;

	public override void _Ready()
	{
		YSortEnabled = true;
		SetupGui();

		var campaignManager = new CampaignManager();
		AddChild(campaignManager);
		campaignManager.LoadCampaign("res://campaigns/campaign1.txt");
		var scenario = campaignManager.StartScenario(this);

		const int teamCount = 2;
		var randomBases = scenario?.RandomBases ?? true;
		var mapName = string.IsNullOrEmpty(scenario?.MapFile) ? DefaultMap : scenario.MapFile;

		Map = TileMapGenerator.CreateTileMap(mapName, teamCount, this, randomBases);
		if (Map == null)
			return;
		Map.Name = "Map";
		MapTileMath.Tilemap = Map;
		AddChild(Map);

		if (scenario != null)
		{
			campaignManager.TileMap = Map;
			campaignManager.SpawnStartingUnits();
			GD.Print($"Main: scenario loaded — map: {scenario.MapFile}, funds: {scenario.Funds}, players: {scenario.Players.Count}");
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouse)
			return;

		if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
		{
			GiveOrder();
			return;
		}

		if (mouse.ButtonIndex != MouseButton.Left)
			return;

		if (mouse.Pressed)
		{
			_isMouseDrag = true;
			_dragStartGlobal = GetGlobalMousePosition();
			return;
		}

		if (!_isMouseDrag)
			return;
		_isMouseDrag = false;
		ClearSelection();

		var dragEndGlobal = GetGlobalMousePosition();
		if (dragEndGlobal == _dragStartGlobal)
			SelectAtPoint(dragEndGlobal);
		else
			SelectInRect(_dragStartGlobal, dragEndGlobal);
	}

	/// <summary>Right click: attack/visit the company under the cursor, or walk there.</summary>
	private void GiveOrder()
	{
		var hit = IntersectPoint(GetGlobalMousePosition());
		if (hit != null)
		{
			if (hit is StaticBody2D structure)
				foreach (var selected in _selected)
					if (selected is Unit unit)
						unit.TargetStructure = structure;
			return;
		}
		var target = GetLocalMousePosition();
		foreach (var selected in _selected)
			if (selected is Unit unit)
				unit.MoveTo(target);
	}

	private void SelectAtPoint(Vector2 globalPoint)
	{
		if (IntersectPoint(globalPoint) is ISelectable { Selectable: true } selectable)
		{
			Select(selectable);
			GuiGameback.UpdateSelectionIcon(selectable as Node2D);
		}
	}

	private void SelectInRect(Vector2 globalA, Vector2 globalB)
	{
		var rect = new Rect2(globalA, Vector2.Zero).Expand(globalB);
		var query = new PhysicsShapeQueryParameters2D
		{
			CollideWithBodies = true,
			Shape = new RectangleShape2D { Size = rect.Size },
			// The query shape is centred on the transform origin.
			Transform = new Transform2D(0, rect.GetCenter()),
		};
		foreach (var result in GetViewport().World2D.DirectSpaceState.IntersectShape(query, 256))
			if (result["collider"].AsGodotObject() is ISelectable { Selectable: true } selectable)
				Select(selectable);

		GuiGameback.UpdateSelectionIcon(_selected.Count > 0 ? _selected[0] as Node2D : null);
	}

	private void Select(ISelectable selectable)
	{
		selectable.Selected = true;
		_selected.Add(selectable);
	}

	private void ClearSelection()
	{
		foreach (var selectable in _selected)
			if (selectable is GodotObject obj && IsInstanceValid(obj))
				selectable.Selected = false;
		_selected.Clear();
		GuiGameback.UpdateSelectionIcon(null);
	}

	private GodotObject IntersectPoint(Vector2 globalPoint)
	{
		var query = new PhysicsPointQueryParameters2D
		{
			CollideWithBodies = true,
			CollideWithAreas = true,
			Position = globalPoint,
		};
		var results = GetViewport().World2D.DirectSpaceState.IntersectPoint(query, 1);
		return results.Count > 0 ? results[0]["collider"].AsGodotObject() : null;
	}

	private void SetupGui()
	{
		var canvasLayer = FindChild("CanvasLayerGUI") as CanvasLayer;
		if (canvasLayer == null)
		{
			canvasLayer = new CanvasLayer { Name = "CanvasLayerGUI" };
			AddChild(canvasLayer);
		}
		canvasLayer.AddChild(new GuiMenu());
	}

	/// <summary>Connected from the bottom panel in node_2d.tscn: clicks on the panel don't reach the map.</summary>
	public void OnGuiGamebackGuiInput(InputEvent @event) => GetViewport().SetInputAsHandled();
}
