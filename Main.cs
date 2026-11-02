using System.Linq;
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
	/// <summary>The order armed with an order button; its cursor shows until the map click (sub_41E970).</summary>
	public OrderMode PendingOrder
	{
		get => _pendingOrder;
		set
		{
			_pendingOrder = value;
			OriginalCursors.Set(value switch
			{
				OrderMode.Go or OrderMode.ForceGo or OrderMode.Patrol => OriginalCursors.Kind.Goto,
				OrderMode.Attack or OrderMode.ForceAttack => OriginalCursors.Kind.Attack,
				OrderMode.Extort => OriginalCursors.Kind.Protect,
				_ => OriginalCursors.Kind.Arrow,
			});
		}
	}
	private OrderMode _pendingOrder;

	/// <summary>Selected units the player may command.</summary>
	public System.Collections.Generic.IEnumerable<Unit> CommandableSelection()
	{
		foreach (var s in _selected)
			if (s is Unit unit && IsInstanceValid(unit) && IsCommandable(unit))
				yield return unit;
	}

	/// <summary>Episode (1-based) to start after a scene reload by the end screen; 0 = none.</summary>
	public static int PendingEpisode;
	private static bool _backToMenu;
	/// <summary>Menu "Переиграть": the episode to start again at once (no briefing).</summary>
	private static int _restartEpisode;
	private int _episode;

	/// <summary>
	/// Command line (after "--"): --episode=N starts episode N (default: the saved campaign progress);
	/// --skip-briefing starts the episode at once; --smoke-test checks all original data and quits with exit code 0/1;
	/// --scenario-test[=1,4,...] plays episodes headless and checks game rules (ScenarioTest).
	/// </summary>
	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		// A new main scene (start, end screen, leaving an episode): the last episode's timed events must not run on in
		// the menus — the clock is an autoload. Starting a scenario resets and runs it again.
		if (GameClock.Instance != null)
		{
			GameClock.Instance.Reset();
			GameClock.Instance.Paused = true;
		}
		// The window class cursor is the EXE's "ArrowCur".
		if (DisplayServer.GetName() != "headless")
			OriginalCursors.Set(OriginalCursors.Kind.Arrow);
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
		if (_backToMenu)
		{
			// Left the episode from the menu: the main menu, whatever the command line asked for.
			_backToMenu = false;
			skipBriefing = false;
			episode = CampaignProgress.Current;
		}
		if (_restartEpisode > 0)
		{
			episode = _restartEpisode;
			skipBriefing = true;
			_restartEpisode = 0;
		}
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
		screen.Switch += step =>
		{
			var next = Mathf.Clamp(episode + step, 1, Mathf.Min(CampaignProgress.MaxReached, scenarios.Count));
			if (next == episode)
				return;
			layer.QueueFree();
			ShowBriefing(next);
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
		// sub_41D3E0 (message kinds 2 / 3): the result text in the info window (title 9,37) first, then the end
		// screen after OK.
		ShowInfoWindow(OriginalTexts.Instance.Get(9, 37), text, () => OpenEndScreen(won, text, episode, count));
	}

	private void OpenEndScreen(bool won, string text, int episode, int count)
	{
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
		_episode = episode;
		var campaignManager = _campaignManager;
		var scenario = campaignManager.StartScenario(this, episode - 1);
		if (scenario == null)
			return;

		Map = TileMapGenerator.CreateTileMap(scenario.MapFile, campaignManager.BaseCount(), this, scenario.RandomBases);
		if (Map == null)
			return;
		Map.Name = "Map";
		MapTileMath.Tilemap = Map;
		Messages.Log.Clear();
		Vision.Init(Map);
		MoveMap.Init(Map);
		Unit.InitOccupancy();
		Map.AddChild(new FogOfWar { Name = "FogOfWar" });
		AddChild(Map);
		if (_orderMarker == null || !IsInstanceValid(_orderMarker))
		{
			_orderMarker = new OrderMarker();
			AddChild(_orderMarker);
		}
		else
			MoveChild(_orderMarker, -1); // above the new map

		campaignManager.TileMap = Map;
		campaignManager.AssignBases();
		campaignManager.SpawnStartingUnits();
		// sub_40A160: the view starts on the local player's headquarters, or (no base, e.g. episode 4) on one of the
		// player's units (sub_409FE7).
		var player = Teams.GetCurrentPlayerTeam();
		Node2D start = player?.HqCompany;
		start ??= GetTree().GetNodesInGroup(Unit.GroupName).OfType<Unit>().FirstOrDefault(u => u.OwnerTeam == player && !u.IsDead);
		if (start != null)
			Minimap.CenterView(start.GlobalPosition);
		campaignManager.StartEconomy();
		AddChild(new DebugCommands { Name = "Debug" });
		GD.Print($"Main: episode {scenario.EpisodeNumber} loaded — map: {scenario.MapFile}, funds: {scenario.Funds}, players: {scenario.Players.Count}");
		// sub_40A160 → sub_414760(1): in a single-player game the episode's tips open in the info window.
		if (ScenarioTest.Instance == null && DisplayServer.GetName() != "headless" && !string.IsNullOrWhiteSpace(scenario.TipText))
			ShowInfoWindow(OriginalTexts.Instance.Get(5, 85), scenario.TipText, null);
	}

	/// <summary>The info window (InfoWindow, sub_447CB0) over the game, on the GUI layer.</summary>
	public InfoWindow ShowInfoWindow(string title, string text, System.Action closed) =>
		ShowInfoWindow(new InfoWindow(title, text), closed);

	public InfoWindow ShowInfoWindow(InfoWindow window, System.Action closed)
	{
		var layer = new CanvasLayer { Name = "InfoLayer", Layer = 5, ProcessMode = ProcessModeEnum.Always };
		AddChild(layer);
		window.Closed += () =>
		{
			layer.QueueFree();
			closed?.Invoke();
		};
		layer.AddChild(window);
		return window;
	}

	/// <summary>The menu's alliances window (sub_45B796 / sub_45BA83) on the GUI layer.</summary>
	public AllianceWindow ShowAllianceWindow()
	{
		(GetTree().Root.FindChild("AllianceWindow", true, false) as AllianceWindow)?.Close();
		var local = Teams.GetCurrentPlayerTeam();
		if (local == null)
			return null;
		var layer = new CanvasLayer { Name = "AllianceLayer", Layer = 5, ProcessMode = ProcessModeEnum.Always };
		AddChild(layer);
		var window = new AllianceWindow(local, MoneyWindow.OtherPlayers(local));
		window.TreeExited += layer.QueueFree;
		layer.AddChild(window);
		return window;
	}

	/// <summary>The menu's money transfer window (sub_4629A6 / sub_462C13) on the GUI layer.</summary>
	public MoneyWindow ShowMoneyWindow()
	{
		(GetTree().Root.FindChild("MoneyWindow", true, false) as MoneyWindow)?.Close();
		var layer = new CanvasLayer { Name = "MoneyLayer", Layer = 5, ProcessMode = ProcessModeEnum.Always };
		AddChild(layer);
		var window = new MoneyWindow(MoneyWindow.OtherPlayers(Teams.GetCurrentPlayerTeam()));
		window.TreeExited += layer.QueueFree;
		layer.AddChild(window);
		return window;
	}

	/// <summary>The menu's statistics window (sub_45FD50 / sub_460290) on the GUI layer.</summary>
	public RankWindow ShowRankWindow()
	{
		var layer = new CanvasLayer { Name = "RankLayer", Layer = 5, ProcessMode = ProcessModeEnum.Always };
		AddChild(layer);
		var window = new RankWindow(RankWindow.LocalPlaces(GetTree()));
		window.TreeExited += layer.QueueFree;
		layer.AddChild(window);
		return window;
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
	private OrderMarker _orderMarker;

	/// <summary>The order animation (OrderMarker) on the clicked unit, or else on the clicked cell.</summary>
	private void ShowOrderMarker(object hit, Vector2 globalPoint)
	{
		if (_orderMarker == null || Map == null)
			return;
		var cell = Map.LocalToMap(Map.ToLocal(globalPoint));
		_orderMarker.Play(hit as Unit, Map, cell);
	}

	private void GiveOrder(Vector2 globalPoint)
	{
		var hit = IntersectPoint(globalPoint);
		var target = ToLocal(globalPoint);
		foreach (var first in CommandableSelection())
		{
			UnitVoice.Play(first, hit is Company or Unit ? UnitVoice.Kind.Attack : UnitVoice.Kind.Go);
			break;
		}
		var ordered = false;
		foreach (var selected in _selected)
		{
			if (selected is not Unit unit || !IsInstanceValid(unit) || !IsCommandable(unit))
				continue;
			ordered = true;
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
		if (ordered)
			ShowOrderMarker(hit, globalPoint);
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
		if (System.Linq.Enumerable.Any(CommandableSelection()))
			ShowOrderMarker(hit, globalPoint);
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

	// ---- unit groups (sub_410EC3 keys 0–9: Ctrl stores the selection in group n at +948h + 20 × n) -------------
	private readonly List<Unit>[] _groups = new List<Unit>[10];

	/// <summary>Ctrl + digit: the selected own units (not collectors) become group <paramref name="n"/>.</summary>
	public bool StoreGroup(int n)
	{
		var units = CommandableSelection().Where(u => u.UnitType != UnitCommon.UnitType.Collector).ToList();
		if (units.Count == 0)
			return false;
		_groups[n] = units;
		return true;
	}

	/// <summary>Digit: selects the living units of group <paramref name="n"/>.</summary>
	public bool RecallGroup(int n)
	{
		var units = _groups[n]?.Where(u => IsInstanceValid(u) && IsCommandable(u)).ToList();
		if (units == null || units.Count == 0)
			return false;
		// Already selected: the view goes to the group (sub_409FE7), as a second press in the original.
		if (_selected.Count == units.Count && units.All(u => _selected.Contains(u)))
		{
			Minimap.CenterView(units[0].GlobalPosition);
			return true;
		}
		ClearSelection();
		foreach (var u in units)
			Select(u);
		GuiGameback.UpdateSelectionIcon(units[0]);
		UnitVoice.Play(units[0], UnitVoice.Kind.Select);
		return true;
	}

	/// <summary>
	/// Menu "Exit" (sub_411F84 → info window mode 2, action 5): "Покинуть игру" (5,82) / "Покинуть игру" (5,83) and
	/// "** Вы уверены? **" (5,84) on line 4; OK leaves the episode for the main menu, "Вернуться" goes back.
	/// Menu "Переиграть" (single player only, sub_414DA0 = 0) opens the same window with action 8: OK ends the
	/// game (sub_419D00), shows "loading" (9,43) and starts the same episode again (sub_43A5A7) without the briefing.
	/// </summary>
	public void ConfirmLeave(bool restart = false)
	{
		if (GetTree().Root.FindChild("InfoWindow", true, false) != null)
			return;
		var t = OriginalTexts.Instance;
		var window = new InfoWindow(t.Get(5, 82), new[] { t.Get(5, 83), "", "", "", t.Get(5, 84) }, 2);
		window.Answered += ok =>
		{
			if (!ok)
				return;
			PendingEpisode = 0;
			if (restart && _episode > 0)
				_restartEpisode = _episode;
			else
				_backToMenu = true;
			GetTree().Paused = false;
			GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
		};
		ShowInfoWindow(window, null);
	}

	/// <summary>Test hook: a left click on the map at a screen point.</summary>
	public void ClickSelect(Vector2 screenPoint)
	{
		ClearSelection();
		SelectAtPoint(GetCanvasTransform().AffineInverse() * screenPoint);
	}

	private void SelectAtPoint(Vector2 globalPoint)
	{
		if (IntersectPoint(globalPoint) is ISelectable { Selectable: true } selectable)
		{
			Select(selectable);
			GuiGameback.UpdateSelectionIcon(selectable as Node2D);
			if (selectable is Unit unit)
				UnitVoice.Play(unit, UnitVoice.Kind.Select);
			else if (selectable is Company company)
				Sound.Play(company.SelectSound());
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
		canvasLayer.AddChild(new MessageLog());
		canvasLayer.AddChild(new GuiMenu());
	}

	/// <summary>Connected from the bottom panel in node_2d.tscn: clicks on the panel don't reach the map.</summary>
	public void OnGuiGamebackGuiInput(InputEvent @event) => GetViewport().SetInputAsHandled();
}
