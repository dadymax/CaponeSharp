using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Loads the original campaign (original_game/episode/ep1.epi, ep2.epi, … in order), starts its scenarios
/// and spawns their players and units.
/// </summary>
public partial class CampaignManager : Node
{
    public static CampaignManager Instance { get; private set; }

    public Campaign CurrentCampaign { get; private set; } = new();
    public Scenario CurrentScenario { get; private set; }

    /// <summary>Main scene node that units are added to.</summary>
    public Node2D MainScene { get; private set; }
    /// <summary>Map used to convert scenario cell coordinates to positions.</summary>
    public TileMapLayer TileMap { get; set; }

    /// <summary>Team colours in PLAYER order (episodes use up to 8 players).</summary>
    private static readonly Color[] PlayerColors =
    {
        Colors.Green, Colors.Red, Colors.Blue, Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.Orange, Colors.White,
    };

    private const int CadillacPassengers = 4;

    public override void _Ready() => Instance = this;

    /// <summary>Folder with the original episode files.</summary>
    public static string EpisodeDirectory => OriginalData.Instance.OriginalGameDirectory + "/episode";

    /// <summary>ep1.epi, ep2.epi, … until the first missing number (the Russian edition has 21).</summary>
    public static List<string> FindEpisodeFiles()
    {
        var files = new List<string>();
        for (var n = 1; n < 100; n++)
        {
            var path = $"{EpisodeDirectory}/ep{n}.epi";
            if (!File.Exists(path))
                break;
            files.Add(path);
        }
        return files;
    }

    public void LoadOriginalCampaign()
    {
        CurrentCampaign = new Campaign { CampaignName = "Capone" };
        var parser = new EpiParser();
        foreach (var path in FindEpisodeFiles())
            CurrentCampaign.AddScenario(parser.Parse(path));
        if (CurrentCampaign.GetScenarioCount() == 0)
            GD.PrintErr("CampaignManager: no episodes found in ", EpisodeDirectory);
        GD.Print("CampaignManager: loaded campaign with ", CurrentCampaign.GetScenarioCount(), " episodes");
    }

    /// <summary>Sets up players, alliances, bribes and victory conditions for the scenario at <paramref name="index"/>.</summary>
    public Scenario StartScenario(Node2D mainSceneRef, int index = 0)
    {
        CurrentScenario = CurrentCampaign.LoadScenario(index);
        if (CurrentScenario == null)
        {
            GD.PrintErr("CampaignManager: no scenario to start");
            return null;
        }

        MainScene = mainSceneRef;
        GameClock.Instance?.Reset();
        RandomNames.Reset();
        World.Reset();
        Training.Reset();
        InitPlayersFromScenario();
        InitAlliancesFromScenario();
        InitBribeAdvances();
        InitVictoryConditions();

        GD.Print($"CampaignManager: starting episode {CurrentScenario.EpisodeNumber} '{CurrentScenario.ScenarioName}' on map '{CurrentScenario.MapFile}' with {CurrentScenario.Players.Count} players");
        return CurrentScenario;
    }

    /// <summary>Number of HQs the map has to provide.</summary>
    public int BaseCount()
    {
        var count = 0;
        if (CurrentScenario != null)
            foreach (var p in CurrentScenario.Players)
                if (p.HasBase)
                    count++;
        return count;
    }

    /// <summary>Hands the map's HQs (in start-point order) to the players that have a base, in PLAYER order.</summary>
    public void AssignBases()
    {
        if (CurrentScenario == null || TileMap == null || !TileMap.HasMeta(TileMapGenerator.MetaNameBases))
            return;
        var bases = TileMap.GetMeta(TileMapGenerator.MetaNameBases).AsGodotArray();
        var next = 0;
        foreach (var player in CurrentScenario.Players)
        {
            if (!player.HasBase)
                continue;
            var team = Teams.GetTeamByPlayerId(player.Id);
            if (next >= bases.Count)
            {
                GD.PrintErr($"CampaignManager: no start point left for player {player.Id} '{player.Name}'");
                continue;
            }
            var entry = bases[next++].AsGodotArray();
            team.HqCoords = entry[0].AsVector2I();
            team.HqCompany = entry[1].AsGodotObject() as Company;
            team.HqCompany?.MakeHq(team);
            SpawnBaseSquad(team);
        }
    }

    /// <summary>
    /// Every player with a base starts with a Thug and a Pistol man next to the HQ
    /// (EXE base setup sub_404098: unit types 0 and 1 on free cells found by sub_43B955).
    /// </summary>
    private void SpawnBaseSquad(Team team)
    {
        var free = FreeCellsAround(team.HqCoords, 2);
        UnitCommon.UnitType[] squad = { UnitCommon.UnitType.Thug, UnitCommon.UnitType.Pistol };
        for (var i = 0; i < squad.Length; i++)
            CreateUnit(squad[i], team, i < free.Count ? free[i] : team.HqCoords);
        GD.Print($"CampaignManager: base of player {team.PlayerId} '{team.PlayerName}' at {team.HqCoords} with a Thug and a Pistol man");
    }

    /// <summary>
    /// Walkable cells nearest to <paramref name="center"/> (rings of growing radius) without a living unit on them,
    /// like World::FindFreeCellNearObject (sub_43B955).
    /// </summary>
    public List<Vector2I> FreeCellsAround(Vector2I center, int count, int maxRadius = 6)
    {
        var blocked = new HashSet<Vector2I>(TileMap.GetMeta(TileMapGenerator.TileNonWalkable).AsGodotArray<Vector2I>());
        var used = new HashSet<Vector2I>(TileMap.GetUsedCells());
        var occupied = new HashSet<Vector2I>(MainScene.GetTree().GetNodesInGroup(Unit.GroupName).OfType<Unit>()
            .Where(u => !u.IsDead && u.Map != null).Select(u => u.Cell));
        var free = new List<Vector2I>();
        for (var radius = 1; radius <= maxRadius && free.Count < count; radius++)
            for (var dx = -radius; dx <= radius && free.Count < count; dx++)
                for (var dy = -radius; dy <= radius && free.Count < count; dy++)
                {
                    var cell = center + new Vector2I(dx, dy);
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == radius && used.Contains(cell)
                        && !blocked.Contains(cell) && !occupied.Contains(cell))
                        free.Add(cell);
                }
        return free;
    }

    /// <summary>A new unit of <paramref name="team"/> (null: neutral) on <paramref name="cell"/>.</summary>
    public Unit CreateUnit(UnitCommon.UnitType type, Team team, Vector2I cell)
    {
        var unit = GD.Load<PackedScene>("res://Units/Unit.tscn").Instantiate<Unit>();
        unit.SetType(type);
        // Unit::Unit (sub_42A150): every gangster but the Collector gets a random personal name.
        if (team != null && type != UnitCommon.UnitType.Collector)
            unit.PersonalName = RandomNames.Gangster();
        if (team != null)
        {
            unit.TeamColor = team.TeamColor;
            unit.SetOwner(OwnerFor(team));
        }
        else
            unit.SetOwner(UnitCommon.UnitOwner.Enemy); // neutral police use the enemy sprite set
        unit.Position = TileMap.MapToLocal(cell);
        MainScene.AddChild(unit);
        return unit;
    }

    /// <summary>A collector for <paramref name="team"/>, standing next to its HQ (sub_41FEF0 + Unit::Unit(type 7)).</summary>
    public Unit SpawnCollector(Team team)
    {
        if (team?.HqCompany == null || MainScene == null)
            return null;
        var free = FreeCellsAround(team.HqCoords, 1);
        return CreateUnit(UnitCommon.UnitType.Collector, team, free.Count > 0 ? free[0] : team.HqCoords);
    }

    /// <summary>A police car next to <paramref name="station"/> (sub_4461A4: object sub_4459D0 placed at the station).</summary>
    public Unit SpawnPoliceCar(Company station)
    {
        if (MainScene == null)
            return null;
        var free = FreeCellsAround(station.Cell, 1);
        return CreateUnit(UnitCommon.UnitType.PoliceCar, null, free.Count > 0 ? free[0] : station.Cell);
    }

    /// <summary>
    /// Starts the timed events once the map, the bases and the units exist: the players' hourly statistics and
    /// debt interest, the blocks' weekly collector dispatch, the companies' income, healing and weekly reset.
    /// </summary>
    public void StartEconomy()
    {
        var clock = GameClock.Instance;
        if (clock == null || MainScene == null)
            return;
        if (Police.Instance == null)
            MainScene.AddChild(new Police { Name = "Police" });
        foreach (var team in Teams.TeamsInGame.Values)
            team.Org?.Start(clock);
        foreach (var team in Teams.TeamsInGame.Values.Where(t => t.Kind != 0 && !t.IsCurrentPlayerColor && t.Org != null))
        {
            team.Ai = new ComputerPlayer(team);
            team.Ai.Start(clock);
        }
        foreach (var block in CityBlock.All)
            block.Start(clock);
        var companies = MainScene.GetTree().GetNodesInGroup(Company.GroupName).OfType<Company>().ToList();
        foreach (var company in companies)
            company.StartEconomy(clock);
        foreach (var team in Teams.TeamsInGame.Values)
            team.Org?.UpdateStats();
        VictoryManager.Instance?.Start(clock);
        GD.Print($"CampaignManager: economy started — {companies.Count(c => c.IsEconomic)} businesses in {CityBlock.All.Count} blocks, "
                 + $"{companies.Count(c => c.IsPoliceStation)} police stations");
    }

    private static UnitCommon.UnitOwner OwnerFor(Team team) =>
        Teams.GetCurrentPlayerTeam()?.GetTargetRelation(team.TeamColor) switch
        {
            Teams.TeamRelation.Self => UnitCommon.UnitOwner.Player,
            Teams.TeamRelation.Friend => UnitCommon.UnitOwner.Teammate,
            _ => UnitCommon.UnitOwner.Enemy,
        };

    /// <summary>Call after the map exists.</summary>
    public void SpawnStartingUnits()
    {
        if (CurrentScenario == null || MainScene == null)
            return;
        if (TileMap == null)
        {
            GD.PrintErr("CampaignManager: tileMap not set, cannot spawn units");
            return;
        }

        var unitScene = GD.Load<PackedScene>("res://Units/Unit.tscn");
        var spawned = 0;
        foreach (var sUnit in CurrentScenario.StartingUnits)
        {
            if (sUnit.UnitType == null)
            {
                GD.PrintErr($"CampaignManager: skipping unit of unknown type {sUnit.OriginalType}");
                continue;
            }
            var unit = SpawnUnit(unitScene, sUnit.UnitType.Value, sUnit);
            spawned++;

            // A Cadillac starts with Thompson men inside (EXE loop at 0x467F7A).
            // There is no transport yet, so they stand on the car's cell.
            if (sUnit.UnitType == UnitCommon.UnitType.Cadillac)
                for (var i = 0; i < CadillacPassengers; i++, spawned++)
                    SpawnUnit(unitScene, UnitCommon.UnitType.Thompson, sUnit, unnamed: true);

            GD.Print($"CampaignManager: spawned {unit.DisplayName} ({sUnit.UnitType}) for player {sUnit.PlayerId} at {sUnit.Position}");
        }
        GD.Print("CampaignManager: spawned ", spawned, " starting units");
    }

    private Unit SpawnUnit(PackedScene unitScene, UnitCommon.UnitType type, Scenario.EpiStartingUnit sUnit, bool unnamed = false)
    {
        var unit = unitScene.Instantiate<Unit>();
        unit.SetType(type);
        unit.PersonalName = unnamed ? "" : sUnit.Name;
        unit.EpiFlagA = sUnit.FlagA;
        unit.EpiFlagB = sUnit.FlagB;
        // Episode units with flag A = 0 cost no salary (TODO: confirm the meaning of flag A).
        if (sUnit.FlagA == 0)
            unit.Salary = 0;

        var playerTeam = Teams.GetTeamByPlayerId(sUnit.PlayerId);
        if (playerTeam != null)
        {
            unit.TeamColor = playerTeam.TeamColor;
            unit.SetOwner(OwnerFor(playerTeam));
        }
        else
        {
            GD.PrintErr("CampaignManager: player team not found for ID: ", sUnit.PlayerId);
            unit.SetOwner(UnitCommon.UnitOwner.Neutral);
        }

        unit.Position = TileMap.MapToLocal(MapTileMath.FromOriginal(TileMap, sUnit.Position));
        MainScene.AddChild(unit);
        return unit;
    }

    /// <summary>One team per PLAYER line. The human is the player with kind 0. FUNDS goes to every player.</summary>
    private void InitPlayersFromScenario()
    {
        Teams.TeamsInGame.Clear();

        var humanIndex = CurrentScenario.Players.FindIndex(p => p.IsHuman);
        if (humanIndex < 0)
        {
            GD.PrintErr("CampaignManager: episode has no human player (kind 0); using the first player");
            humanIndex = 0;
        }

        for (var i = 0; i < CurrentScenario.Players.Count; i++)
        {
            var pData = CurrentScenario.Players[i];
            var team = Teams.GetOrAddTeam(PlayerColors[i % PlayerColors.Length]);
            team.PlayerId = pData.Id;
            team.Kind = pData.Kind;
            team.PlayerName = pData.Name;
            team.HasBase = pData.HasBase;
            team.IsCurrentPlayerColor = i == humanIndex;
            team.Org = new Organisation(team, CurrentScenario.Funds);
        }
        GD.Print($"CampaignManager: initialized {Teams.TeamsInGame.Count} teams, funds {CurrentScenario.Funds} each");
    }

    private void InitAlliancesFromScenario()
    {
        foreach (var alliance in CurrentScenario.Alliances)
        {
            var fromTeam = Teams.GetTeamByPlayerId(alliance.FromPlayer);
            var toTeam = Teams.GetTeamByPlayerId(alliance.ToPlayer);
            if (fromTeam != null && toTeam != null)
            {
                // Mutual, as in the EXE: a->SetRelation(b, 2) and b->SetRelation(a, 2) (0x467714 / 0x467721).
                fromTeam.SetTargetRelation(toTeam.TeamColor, Teams.TeamRelation.Friend);
                toTeam.SetTargetRelation(fromTeam.TeamColor, Teams.TeamRelation.Friend);
            }
            else
                GD.PrintErr($"CampaignManager: alliance team not found: {alliance.FromPlayer} -> {alliance.ToPlayer}");
        }
    }

    private void InitBribeAdvances()
    {
        foreach (var bribe in CurrentScenario.BribeAdvances)
        {
            var team = Teams.GetTeamByPlayerId(bribe.PlayerId);
            if (team == null)
            {
                GD.PrintErr("CampaignManager: Invalid player id in bribeadvance desc: ", bribe.PlayerId);
                continue;
            }
            if (team.Org == null || bribe.Authority < 0 || bribe.Authority >= Organisation.TypeCount)
                continue;
            team.Org.Favour[bribe.Authority] = bribe.Points;
            team.Org.AuthorityMoney[bribe.Authority] = bribe.Money;
        }
    }

    private void InitVictoryConditions()
    {
        var victoryManager = new VictoryManager();
        AddChild(victoryManager);
        victoryManager.InitFromScenario(CurrentScenario);
    }

    /// <summary>
    /// A player met all their victory conditions. TODO (R8/M7): the original's success / failure screens and the move to
    /// the next episode; for now the game stops with the episode's success (or failure) text as a message.
    /// </summary>
    /// <summary>The local player was eliminated (sub_43B677): the failure screen with the defeat text.</summary>
    public void OnPlayerDefeated(Team team, string text)
    {
        GD.Print($"CampaignManager: '{team?.PlayerName}' defeated in episode {CurrentScenario?.EpisodeNumber} at {GameClock.Format(GameClock.Instance?.Minute ?? 0)}: {team?.Org}");
        Messages.Show(text);
        if (GameClock.Instance != null)
            GameClock.Instance.Paused = true;
        if (MainScene != null)
            MainScene.GetTree().Paused = true;
        (MainScene as Main)?.ShowEndScreen(false, text, CurrentScenario?.EpisodeNumber ?? 1);
    }

    public void OnPlayerWon(int playerId)
    {
        var team = Teams.GetTeamByPlayerId(playerId);
        var human = team?.IsCurrentPlayerColor == true;
        GD.Print($"CampaignManager: player {playerId} '{team?.PlayerName}' won episode {CurrentScenario?.EpisodeNumber} at {GameClock.Format(GameClock.Instance?.Minute ?? 0)}: {team?.Org}");
        Messages.Show(human ? CurrentScenario?.SuccessText ?? "" : CurrentScenario?.FailureText ?? "");
        if (GameClock.Instance != null)
            GameClock.Instance.Paused = true;
        // Units move and act in physics frames, so the whole world stops (the GUI layer keeps running).
        if (MainScene != null)
            MainScene.GetTree().Paused = true;
        (MainScene as Main)?.ShowEndScreen(human, human ? CurrentScenario?.SuccessText : CurrentScenario?.FailureText,
            CurrentScenario?.EpisodeNumber ?? 1);
    }

    /// <summary>Scenario won: move to the next one if there is one.</summary>
    public void CompleteScenario()
    {
        if (CurrentCampaign.LoadNextScenario())
        {
            CurrentScenario = CurrentCampaign.GetCurrentScenario();
            GD.Print($"CampaignManager: completed scenario, moving to next: '{CurrentScenario.ScenarioName}'");
        }
        else
        {
            GD.Print("CampaignManager: campaign complete!");
        }
    }

    public void FailScenario()
    {
        GD.Print($"CampaignManager: scenario failed — reloading: '{CurrentScenario?.ScenarioName}'");
        // TODO: reload the scenario or end the campaign
    }

    public int GetScenarioFunds() => CurrentScenario?.Funds ?? 0;
    public string GetScenarioMapFile() => CurrentScenario?.MapFile ?? "";
}
