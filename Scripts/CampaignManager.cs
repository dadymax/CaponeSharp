using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Loads a campaign, starts its scenarios and spawns their players and units.</summary>
public partial class CampaignManager : Node
{
    public static CampaignManager Instance { get; private set; }

    public Campaign CurrentCampaign { get; private set; } = new();
    public Scenario CurrentScenario { get; private set; }

    public string CampaignDirectory { get; set; } = "res://campaigns/";

    /// <summary>Main scene node that units are added to.</summary>
    public Node2D MainScene { get; private set; }
    /// <summary>Map used to convert scenario cell coordinates to positions.</summary>
    public TileMapLayer TileMap { get; set; }

    private static readonly Color[] PlayerColors = { Colors.Green, Colors.Red, Colors.Blue, Colors.Yellow };

    public override void _Ready() => Instance = this;

    /// <summary>Campaign file: one .epi file name per line.</summary>
    public void LoadCampaign(string campaignFilePath)
    {
        CurrentCampaign = new Campaign();

        using var file = FileAccess.Open(campaignFilePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr("CampaignManager: failed to open campaign file: ", campaignFilePath);
            return;
        }

        var epiFiles = new List<string>();
        while (!file.EofReached())
        {
            var line = file.GetLine().Trim();
            if (line.Length > 0)
                epiFiles.Add(line);
        }

        var parser = new EpiParser();
        foreach (var epiFile in epiFiles)
        {
            var scenario = parser.Parse(CampaignDirectory + epiFile);
            if (scenario != null)
                CurrentCampaign.AddScenario(scenario);
        }

        CurrentScenario = CurrentCampaign.LoadScenario(0);
        GD.Print("CampaignManager: loaded campaign with ", CurrentCampaign.GetScenarioCount(), " scenarios");
    }

    /// <summary>Sets up players, alliances and victory conditions for the current scenario.</summary>
    public Scenario StartScenario(Node2D mainSceneRef, TileMapLayer tileMapRef = null)
    {
        CurrentScenario = CurrentCampaign.GetCurrentScenario();
        if (CurrentScenario == null)
        {
            GD.PrintErr("CampaignManager: no scenario to start");
            return null;
        }

        MainScene = mainSceneRef;
        if (tileMapRef != null)
            TileMap = tileMapRef;

        InitPlayersFromScenario();
        InitAlliancesFromScenario();
        InitVictoryConditions();

        GD.Print($"CampaignManager: starting scenario '{CurrentScenario.ScenarioName}' on map '{CurrentScenario.MapFile}' with {CurrentScenario.Players.Count} players");
        return CurrentScenario;
    }

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
            var unit = unitScene.Instantiate<Unit>();
            unit.SetType(sUnit.UnitType);

            var playerTeam = Teams.GetTeamByPlayerId(sUnit.PlayerId);
            if (playerTeam != null)
            {
                unit.TeamColor = playerTeam.TeamColor;
                var currentTeam = Teams.GetCurrentPlayerTeam();
                if (currentTeam != null)
                {
                    unit.SetOwner(currentTeam.GetTargetRelation(playerTeam.TeamColor) switch
                    {
                        Teams.TeamRelation.Self => UnitCommon.UnitOwner.Player,
                        Teams.TeamRelation.Friend => UnitCommon.UnitOwner.Teammate,
                        _ => UnitCommon.UnitOwner.Enemy,
                    });
                }
            }
            else
            {
                GD.PrintErr("CampaignManager: player team not found for ID: ", sUnit.PlayerId);
                unit.SetOwner(UnitCommon.UnitOwner.Player);
            }

            unit.Position = TileMap.MapToLocal(sUnit.Position);
            MainScene.AddChild(unit);
            spawned++;
            GD.Print($"CampaignManager: spawned {sUnit.UnitType} for player {sUnit.PlayerId} at {sUnit.Position}");
        }
        GD.Print("CampaignManager: spawned ", spawned, " starting units");
    }

    /// <summary>One team per player (colours in order); the first player is the human.</summary>
    private void InitPlayersFromScenario()
    {
        Teams.TeamsInGame.Clear();
        Teams.PlayerMoney.Clear();

        for (var i = 0; i < CurrentScenario.Players.Count; i++)
        {
            var pData = CurrentScenario.Players[i];
            var team = Teams.GetOrAddTeam(PlayerColors[i % PlayerColors.Length]);
            team.PlayerId = pData.Order;
            team.PlayerName = pData.Name;
            team.HasBase = pData.HasBase;
            if (i == 0)
                team.IsCurrentPlayerColor = true;

            // Starting funds go to the first player.
            if (pData.Order == 0)
            {
                team.PlayerMoney = CurrentScenario.Funds;
                Teams.SetPlayerMoney(pData.Order, CurrentScenario.Funds);
                GD.Print($"CampaignManager: player '{pData.Name}' gets funds: {CurrentScenario.Funds}");
            }
        }
        GD.Print("CampaignManager: initialized ", Teams.TeamsInGame.Count, " teams");
    }

    private void InitAlliancesFromScenario()
    {
        foreach (var alliance in CurrentScenario.Alliances)
        {
            var fromTeam = Teams.GetTeamByPlayerId(alliance.FromPlayer);
            var toTeam = Teams.GetTeamByPlayerId(alliance.ToPlayer);
            if (fromTeam != null && toTeam != null)
            {
                fromTeam.SetTargetRelation(toTeam.TeamColor, Teams.TeamRelation.Friend);
                GD.Print($"CampaignManager: alliance from '{fromTeam.PlayerName}' to '{toTeam.PlayerName}'");
            }
            else
            {
                GD.PrintErr($"CampaignManager: alliance team not found: {alliance.FromPlayer} -> {alliance.ToPlayer}");
            }
        }
    }

    private void InitVictoryConditions()
    {
        var victoryManager = new VictoryManager();
        AddChild(victoryManager);
        victoryManager.InitFromScenario(CurrentScenario);
        GD.Print("CampaignManager: initialized victory conditions");
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
