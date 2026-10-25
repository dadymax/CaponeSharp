using System.Collections.Generic;
using System.IO;
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
            SpawnBaseSquad(team);
        }
    }

    /// <summary>
    /// Every player with a base starts with a Thug and a Pistol man next to the HQ
    /// (EXE base setup sub_404098: unit types 0 and 1 on free cells found by sub_43B955).
    /// </summary>
    private void SpawnBaseSquad(Team team)
    {
        var unitScene = GD.Load<PackedScene>("res://Units/Unit.tscn");
        var blocked = new HashSet<Vector2I>(TileMap.GetMeta(TileMapGenerator.TileNonWalkable).AsGodotArray<Vector2I>());
        var used = new HashSet<Vector2I>(TileMap.GetUsedCells());
        var free = new List<Vector2I>();
        for (var radius = 1; radius <= 4 && free.Count < 2; radius++)
            for (var dx = -radius; dx <= radius; dx++)
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var cell = team.HqCoords + new Vector2I(dx, dy);
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == radius && used.Contains(cell) && !blocked.Contains(cell)
                        && free.Count < 2)
                        free.Add(cell);
                }

        UnitCommon.UnitType[] squad = { UnitCommon.UnitType.Thug, UnitCommon.UnitType.Pistol };
        for (var i = 0; i < squad.Length; i++)
        {
            var unit = unitScene.Instantiate<Unit>();
            unit.SetType(squad[i]);
            unit.TeamColor = team.TeamColor;
            unit.SetOwner(OwnerFor(team));
            unit.Position = TileMap.MapToLocal(i < free.Count ? free[i] : team.HqCoords);
            MainScene.AddChild(unit);
        }
        GD.Print($"CampaignManager: base of player {team.PlayerId} '{team.PlayerName}' at {team.HqCoords} with a Thug and a Pistol man");
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
        Teams.PlayerMoney.Clear();

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
            team.PlayerMoney = CurrentScenario.Funds;
            Teams.SetPlayerMoney(pData.Id, CurrentScenario.Funds);
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
                fromTeam.SetTargetRelation(toTeam.TeamColor, Teams.TeamRelation.Friend);
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
            team.AuthorityFavour[bribe.Authority] = bribe.Points;
            team.AuthorityMoney[bribe.Authority] = bribe.Money;
        }
    }

    private void InitVictoryConditions()
    {
        var victoryManager = new VictoryManager();
        AddChild(victoryManager);
        victoryManager.InitFromScenario(CurrentScenario);
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
