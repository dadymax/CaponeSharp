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
	private Vector2 _dragStartScreen;
	private SelectionBox _selectionBox;

	/// <summary>A drag shorter than this (screen pixels) is a click.</summary>
	private const float DragThreshold = 4f;
	private CampaignManager _campaignManager;

	public static Main Instance { get; private set; }

	/// <summary>Order buttons (unit menu sub_42D2C1): the next left click on the map carries out the chosen order.</summary>
	public enum OrderMode { None, Go, Attack, Extort, ForceGo, ForceAttack, Patrol }
	public OrderMode PendingOrder { get; set; }

	/// <summary>Selected units the player may command.</summary>
	public System.Collections.Generic.IEnumerable<Unit> CommandableSelection()
	{
		foreach (var s in _selected)
			if (s is Unit unit && IsInstanceValid(unit) && IsCommandable(unit))
				yield return unit;
	}

	/// <summary>Episode (1-based) to start after a scene reload by the end screen; 0 = none.</summary>
	public static int PendingEpisode;

	/// <summary>
	/// Command line (after "--"): --episode=N starts episode N (default: the saved campaign progress);
	/// --skip-briefing starts the episode at once; --smoke-test checks all original data and quits with exit code 0/1;
	/// --scenario-test[=1,4,...] plays episodes headless and checks game rules (ScenarioTest).
	/// </summary>
	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		var args = OS.GetCmdlineUserArgs();
		if (System.Array.IndexOf(args, "--smoke-test") >= 0)
		{
			var failures = SmokeTest.Run();
			GetTree().Quit(failures == 0 ? 0 : 1);
			return;
		}
		if (System.Array.Find(args, a => a == "--unit-test" || a.StartsWith("--unit-test=")) is { } unitTest)
		{
			var failures = Tests.UnitTests.Run(unitTest.Contains('=') ? unitTest[(unitTest.IndexOf('=') + 1)..] : null);
			GetTree().Quit(failures == 0 ? 0 : 1);
			return;
		}
		CampaignProgress.Load();
		var episode = CampaignProgress.Current;
		var afterEndScreen = PendingEpisode > 0;
		if (afterEndScreen)
			episode = PendingEpisode;
		else
			foreach (var arg in args)
				if (arg.StartsWith("--episode=") && int.TryParse(arg["--episode=".Length..], out var n))
					episode = n;
		var skipBriefing = System.Array.IndexOf(args, "--skip-briefing") >= 0;
		PendingEpisode = 0;
		var scenarioTest = ScenarioTest.Requested(args);
		if (scenarioTest)
		{
			episode = ScenarioTest.NextEpisode(args);
			if (episode == 0)
			{
				GetTree().Quit(ScenarioTest.Finish());
				return;
			}
			skipBriefing = true;
		}

		YSortEnabled = true;
		_selectionBox = new SelectionBox { Name = "SelectionBox" };
		AddChild(_selectionBox);
		SetupGui();

		_campaignManager = new CampaignManager();
		AddChild(_campaignManager);
		_campaignManager.LoadOriginalCampaign();
		var count = _campaignManager.CurrentCampaign.GetScenarioCount();
		episode = Mathf.Clamp(episode, 1, Mathf.Max(1, count));
		if (skipBriefing)
		{
			StartEpisode(episode);
			if (scenarioTest)
				AddChild(new ScenarioTest(episode));
		}
		else if (afterEndScreen)
			ShowBriefing(episode);
		else
			ShowMainMenu(episode);
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
		screen.Back += () =>
		{
			layer.QueueFree();
			ShowMainMenu(episode);
		};
		layer.AddChild(screen);
	}

	/// <summary>Main menu (PLAN M12): Campaign opens the briefing of the current episode, Exit leaves the game.</summary>
	private void ShowMainMenu(int episode)
	{
		var layer = AddScreenLayer();
		var menu = new MainMenuScreen();
		menu.CampaignChosen += () =>
		{
			layer.QueueFree();
			ShowBriefing(episode);
		};
		menu.ExitChosen += () => GetTree().Quit();
		layer.AddChild(menu);
	}

	/// <summary>
	/// The episode ended (sub_43C930): a won episode advances and saves the campaign; the end screen's button reloads the
	/// scene with the next episode (won) or the same one (lost), starting at its briefing.
	/// </summary>
	public void ShowEndScreen(bool won, string text, int episode)
	{
		if (ScenarioTest.Instance != null)
		{
			ScenarioTest.Instance.EpisodeEnded(won, text);
			return;
		}
		var count = _campaignManager.CurrentCampaign.GetScenarioCount();
		if (won)
			CampaignProgress.Won(episode, count);
		var layer = AddScreenLayer();
		var screen = new EndScreen(won, text, Statistics.Current);
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
		Vision.Init(Map);
		MoveMap.Init(Map);
		Unit.InitOccupancy();
		Map.AddChild(new FogOfWar { Name = "FogOfWar" });
		AddChild(Map);

		campaignManager.TileMap = Map;
		campaignManager.AssignBases();
		// The view starts on the local player's headquarters.
		if (GetNodeOrNull<Camera2D>("Camera2D") is { } camera && Teams.GetCurrentPlayerTeam()?.HqCompany is { } hq)
			camera.GlobalPosition = hq.GlobalPosition;
		campaignManager.SpawnStartingUnits();
		campaignManager.StartEconomy();
		AddChild(new DebugCommands { Name = "Debug" });
		GD.Print($"Main: episode {scenario.EpisodeNumber} loaded — map: {scenario.MapFile}, funds: {scenario.Funds}, players: {scenario.Players.Count}");
	}

	/// <summary>Map clicks. Unhandled input, so clicks on GUI controls (menu, bottom panel) don't reach the map.</summary>
	/// <summary>
	/// The FBI "visits" the local player (HQ vt+104h, 30 game hours): the original sets the main window's +0A40h flag,
	/// which its input handlers check, and shows "FBI \"visits\" you ..." (20,2) with a progress bar.
	/// </summary>
	public static bool InputBlocked => Teams.GetCurrentPlayerTeam()?.Org?.FbiVisitActive == true;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion && Map != null)
		{
			var global = GetCanvasTransform().AffineInverse() * motion.Position;
			UpdateMapHint(global);
			if (_isMouseDrag && PendingOrder == OrderMode.None && motion.Position.DistanceTo(_dragStartScreen) >= DragThreshold)
				_selectionBox.Show(ToLocal(_dragStartGlobal), ToLocal(global));
			return;
		}
		if (@event is not InputEventMouseButton mouse || Map == null)
			return;
		if (InputBlocked)
			return;

		// Event position, not GetGlobalMousePosition (that reads the OS cursor; same for a real mouse).
		var eventGlobal = GetCanvasTransform().AffineInverse() * mouse.Position;
		if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
		{
			GiveOrder(eventGlobal);
			return;
		}

		if (mouse.ButtonIndex != MouseButton.Left)
			return;

		if (mouse.Pressed)
		{
			_isMouseDrag = true;
			_dragStartGlobal = eventGlobal;
			_dragStartScreen = mouse.Position;
			return;
		}

		if (!_isMouseDrag)
			return;
		_isMouseDrag = false;
		_selectionBox.Hide();
		if (PendingOrder != OrderMode.None)
		{
			ExecutePendingOrder(eventGlobal);
			return;
		}
		ClearSelection();

		if (mouse.Position.DistanceTo(_dragStartScreen) < DragThreshold)
			SelectAtPoint(eventGlobal);
		else
			SelectInRect(_dragStartGlobal, eventGlobal);
	}

	/// <summary>
	/// Right click: extort the company under the cursor, attack the unit under it, or walk there.
	/// Collectors and the police are not under the player's command.
	/// </summary>
	private void GiveOrder(Vector2 globalPoint)
	{
		var hit = IntersectPoint(globalPoint);
		var target = ToLocal(globalPoint);
		foreach (var first in CommandableSelection())
		{
			UnitVoice.Play(first, hit is Company or Unit ? UnitVoice.Kind.Attack : UnitVoice.Kind.Go);
			break;
		}
		foreach (var selected in _selected)
		{
			if (selected is not Unit unit || !IsInstanceValid(unit) || !IsCommandable(unit))
				continue;
			if (unit.IsCar)
				unit.GoTo(target, false); // the car only drives (sub_42F138)
			else if (hit is Unit car && car.CanBoard(unit))
				unit.SetOrder(new BoardOrder(unit, car)); // onto an own Cadillac: get in (sub_42C1F6)
			else if (hit is Unit && hit != unit && unit.HasWeapon)
				unit.AttackOrder((Node2D)hit, false);
			else if (hit is Unit && hit != unit)
				unit.GoTo(((Node2D)hit).Position, false); // no weapon (Thief, Godfather): just go there
			else if (hit is Company)
				unit.TargetStructure = (Node2D)hit;
			else
				unit.GoTo(target, false);
		}
	}

	/// <summary>Carries out the order chosen with an order button on the map point <paramref name="globalPoint"/>.</summary>
	private void ExecutePendingOrder(Vector2 globalPoint)
	{
		var order = PendingOrder;
		PendingOrder = OrderMode.None;
		var hit = IntersectPoint(globalPoint);
		var local = ToLocal(globalPoint);
		foreach (var first in CommandableSelection())
		{
			UnitVoice.Play(first, order switch
			{
				OrderMode.Go or OrderMode.Patrol => UnitVoice.Kind.Go,
				OrderMode.ForceGo => UnitVoice.Kind.GoHurry,
				_ => UnitVoice.Kind.Attack,
			});
			break;
		}
		foreach (var unit in CommandableSelection())
			switch (order)
			{
				case OrderMode.Go or OrderMode.ForceGo when hit is Unit car && car.CanBoard(unit):
					unit.SetOrder(new BoardOrder(unit, car));
					break;
				case OrderMode.Go or OrderMode.ForceGo when hit is Company building && StealOrder.CanSteal(unit):
					unit.TargetStructure = building; // a Thief sent onto a building steals or delivers (sub_42C1F6)
					break;
				case OrderMode.Go or OrderMode.ForceGo:
					unit.GoTo(local, order == OrderMode.ForceGo);
					break;
				case OrderMode.Attack or OrderMode.ForceAttack when hit is Unit target && target != unit:
					unit.AttackOrder(target, order == OrderMode.ForceAttack);
					break;
				case OrderMode.Patrol:
					unit.SetOrder(new PatrolOrder(unit, unit.Cell, Map.LocalToMap(local)));
					break;
				case OrderMode.Extort when hit is Company company && ExtortOrder.CanExtort(unit, company):
					unit.SetOrder(new ExtortOrder(unit, company));
					break;
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
			if (selectable is Unit unit)
				UnitVoice.Play(unit, UnitVoice.Kind.Select);
		}
	}

	/// <summary>
	/// Box selection (user): the screen rectangle picks only the player's own gangsters standing in it — never
	/// companies, collectors or the police.
	/// </summary>
	private void SelectInRect(Vector2 globalA, Vector2 globalB)
	{
		var rect = new Rect2(globalA, Vector2.Zero).Expand(globalB);
		foreach (var node in GetTree().GetNodesInGroup(Unit.GroupName))
			if (node is Unit { Selectable: true } unit && IsCommandable(unit) && rect.HasPoint(unit.GlobalPosition))
				Select(unit);

		GuiGameback.UpdateSelectionIcon(_selected.Count > 0 ? _selected[0] as Node2D : null);
		if (_selected.Count > 0 && _selected[0] is Unit first)
			UnitVoice.Play(first, UnitVoice.Kind.Select);
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
		// Units hidden by the fog of war and companies not discovered yet can't be picked.
		foreach (var hit in GetViewport().World2D.DirectSpaceState.IntersectPoint(query, 8))
		{
			var collider = hit["collider"].AsGodotObject();
			if (collider is Unit { Visible: false } or Company { Visible: false })
				continue;
			return collider;
		}
		return null;
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
