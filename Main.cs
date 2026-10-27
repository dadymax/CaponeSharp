using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Main scene: loads the campaign, builds the map, handles selection and orders.</summary>
public partial class Main : Node2D
{
	public TileMapLayer Map { get; private set; }

	private readonly List<ISelectable> _selected = new();
	private bool _isMouseDrag;
	private Vector2 _dragStartGlobal;
	private CampaignManager _campaignManager;

	/// <summary>Episode (1-based) to start after a scene reload by the end screen; 0 = none.</summary>
	public static int PendingEpisode;

	/// <summary>
	/// Command line (after "--"): --episode=N starts episode N (default: the saved campaign progress);
	/// --skip-briefing starts the episode at once; --smoke-test checks all original data and quits with exit code 0/1.
	/// </summary>
	public override void _Ready()
	{
		var args = OS.GetCmdlineUserArgs();
		if (System.Array.IndexOf(args, "--smoke-test") >= 0)
		{
			var failures = SmokeTest.Run();
			GetTree().Quit(failures == 0 ? 0 : 1);
			return;
		}
		CampaignProgress.Load();
		var episode = CampaignProgress.Current;
		if (PendingEpisode > 0)
			episode = PendingEpisode;
		else
			foreach (var arg in args)
				if (arg.StartsWith("--episode=") && int.TryParse(arg["--episode=".Length..], out var n))
					episode = n;
		var skipBriefing = System.Array.IndexOf(args, "--skip-briefing") >= 0;
		PendingEpisode = 0;

		YSortEnabled = true;
		SetupGui();

		_campaignManager = new CampaignManager();
		AddChild(_campaignManager);
		_campaignManager.LoadOriginalCampaign();
		var count = _campaignManager.CurrentCampaign.GetScenarioCount();
		episode = Mathf.Clamp(episode, 1, Mathf.Max(1, count));
		if (skipBriefing)
			StartEpisode(episode);
		else
			ShowBriefing(episode);
	}

	private CanvasLayer AddScreenLayer()
	{
		var layer = new CanvasLayer { Name = "Screens", Layer = 10, ProcessMode = ProcessModeEnum.Always };
		AddChild(layer);
		return layer;
	}

	private void ShowBriefing(int episode)
	{
		var scenarios = _campaignManager.CurrentCampaign.Scenarios;
		if (episode < 1 || episode > scenarios.Count)
			return;
		var layer = AddScreenLayer();
		var screen = new BriefingScreen(scenarios[episode - 1], episode - 1);
		screen.Started += () =>
		{
			layer.QueueFree();
			StartEpisode(episode);
		};
		// TODO (M7): the main menu (MAINMENU.bmp); for now "Back" leaves the game.
		screen.Back += () => GetTree().Quit();
		layer.AddChild(screen);
	}

	/// <summary>
	/// The episode ended (sub_43C930): a won episode advances and saves the campaign; the end screen's button reloads the
	/// scene with the next episode (won) or the same one (lost), starting at its briefing.
	/// </summary>
	public void ShowEndScreen(bool won, string text, int episode)
	{
		var count = _campaignManager.CurrentCampaign.GetScenarioCount();
		if (won)
			CampaignProgress.Won(episode, count);
		var layer = AddScreenLayer();
		var screen = new EndScreen(won, text);
		screen.Closed += () =>
		{
			PendingEpisode = won ? Mathf.Min(episode + 1, count) : episode;
			GetTree().Paused = false;
			GetTree().ReloadCurrentScene();
		};
		layer.AddChild(screen);
	}

	private void StartEpisode(int episode)
	{
		var campaignManager = _campaignManager;
		var scenario = campaignManager.StartScenario(this, episode - 1);
		if (scenario == null)
			return;

		Map = TileMapGenerator.CreateTileMap(scenario.MapFile, campaignManager.BaseCount(), this, scenario.RandomBases);
		if (Map == null)
			return;
		Map.Name = "Map";
		MapTileMath.Tilemap = Map;
		AddChild(Map);

		campaignManager.TileMap = Map;
		campaignManager.AssignBases();
		campaignManager.SpawnStartingUnits();
		campaignManager.StartEconomy();
		AddChild(new DebugCommands { Name = "Debug" });
		GD.Print($"Main: episode {scenario.EpisodeNumber} loaded — map: {scenario.MapFile}, funds: {scenario.Funds}, players: {scenario.Players.Count}");
	}

	/// <summary>Map clicks. Unhandled input, so clicks on GUI controls (menu, bottom panel) don't reach the map.</summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion && Map != null)
		{
			UpdateMapHint(GetCanvasTransform().AffineInverse() * motion.Position);
			return;
		}
		if (@event is not InputEventMouseButton mouse || Map == null)
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

	/// <summary>
	/// Right click: extort the company under the cursor, attack the unit under it, or walk there.
	/// Collectors and the police are not under the player's command.
	/// </summary>
	private void GiveOrder()
	{
		var hit = IntersectPoint(GetGlobalMousePosition());
		var target = GetLocalMousePosition();
		foreach (var selected in _selected)
		{
			if (selected is not Unit unit || !IsInstanceValid(unit) || !IsCommandable(unit))
				continue;
			if (hit is Company or Unit && hit != unit)
				unit.TargetStructure = (Node2D)hit;
			else
				unit.MoveTo(target);
		}
	}

	private static bool IsCommandable(Unit unit) =>
		!unit.IsDead && !unit.IsArrested && unit.OwnerTeam?.IsCurrentPlayerColor == true
		&& unit.UnitType is not (UnitCommon.UnitType.Collector or UnitCommon.UnitType.PoliceCar or UnitCommon.UnitType.Policeman);

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

	private string _mapHint = "";

	/// <summary>
	/// sub_40C2C2: the name of the company or unit under the cursor goes to the panel's status line.
	/// TODO: with an order button chosen it reads "Extort %s" / "Attack %s" / "Destroy %s" (texts 5,40–62).
	/// </summary>
	private void UpdateMapHint(Vector2 globalPoint)
	{
		var hint = IntersectPoint(globalPoint) switch
		{
			Company { IsHq: true } hq => hq.OwnerTeam?.PlayerName ?? hq.KindName,
			Company company => company.CompanyName,
			Unit unit => unit.DisplayName,
			_ => "",
		};
		if (hint == _mapHint)
			return;
		if (PanelInfo.StatusText == _mapHint)
			PanelInfo.StatusText = hint;
		_mapHint = hint;
	}

	public override void _Process(double delta)
	{
		// Leaving the map for the GUI clears the map hint (button hints set their own text).
		if (_mapHint.Length > 0 && GetViewport().GuiGetHoveredControl() != null)
		{
			if (PanelInfo.StatusText == _mapHint)
				PanelInfo.StatusText = "";
			_mapHint = "";
		}
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
		canvasLayer.ProcessMode = ProcessModeEnum.Always;
		canvasLayer.AddChild(new GuiMenu());
	}

	/// <summary>Connected from the bottom panel in node_2d.tscn: clicks on the panel don't reach the map.</summary>
	public void OnGuiGamebackGuiInput(InputEvent @event) => GetViewport().SetInputAsHandled();
}
