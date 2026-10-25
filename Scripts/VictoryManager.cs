using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Tracks the current scenario's victory conditions.</summary>
public partial class VictoryManager : Node
{
    public static VictoryManager Instance { get; private set; }

    public List<VictoryCondition> Conditions { get; } = new();
    public Scenario CurrentScenario { get; private set; }
    public bool AllCompleted { get; private set; }

    public override void _Ready() => Instance = this;

    public void InitFromScenario(Scenario scenario)
    {
        Conditions.Clear();
        AllCompleted = false;
        CurrentScenario = scenario;
        if (scenario == null)
            return;

        foreach (var vc in scenario.VictoryConditions)
        {
            if (VictoryCondition.FromEpiType(vc.ConditionType) == null)
            {
                GD.PrintErr("VictoryManager: unknown VCOND type ", vc.ConditionType);
                continue;
            }
            Conditions.Add(new VictoryCondition(vc));
        }
        GD.Print("VictoryManager: initialized ", Conditions.Count, " victory conditions");
    }

    /// <summary>Returns the winning player's ID, or -1.</summary>
    public int CheckAll()
    {
        foreach (var vc in Conditions)
        {
            if (vc.Check() && CheckPlayerAllConditions(vc.TargetPlayer))
            {
                AllCompleted = true;
                GD.Print("VictoryManager: player ", vc.TargetPlayer, " WINS!");
                return vc.TargetPlayer;
            }
        }
        return -1;
    }

    private bool CheckPlayerAllConditions(int playerId)
    {
        var any = false;
        foreach (var vc in Conditions)
        {
            if (vc.TargetPlayer != playerId)
                continue;
            any = true;
            if (!vc.IsCompleted)
                return false;
        }
        return any;
    }

    public void UpdateMoneyProgress(int playerId, long amount)
    {
        foreach (var vc in Conditions)
            if (vc.TargetPlayer == playerId)
                vc.UpdateMoneyProgress(amount);
    }

    public void UpdateKillProgress(UnitCommon.UnitType killedUnitType, int killedPlayerId)
    {
        foreach (var vc in Conditions)
            vc.UpdateKillProgress(killedUnitType, killedPlayerId);
    }

    public List<VictoryCondition> GetConditionsForPlayer(int playerId) =>
        Conditions.FindAll(vc => vc.TargetPlayer == playerId);

    public bool IsCampaignComplete() => AllCompleted;
}
