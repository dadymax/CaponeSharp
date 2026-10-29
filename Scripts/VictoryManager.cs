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

    /// <summary>
    /// Returns the winning player's ID, or -1. sub_43C1B2: the first condition met ends the episode — a player with
    /// several conditions (episode 8: either of two places) needs only one of them.
    /// </summary>
    public int CheckAll()
    {
        foreach (var vc in Conditions)
        {
            if (vc.Check())
            {
                AllCompleted = true;
                GD.Print("VictoryManager: player ", vc.TargetPlayer, " WINS!");
                return vc.TargetPlayer;
            }
        }
        return -1;
    }


    /// <summary>Raised once with the winning player's id.</summary>
    public event System.Action<int> PlayerWon;

    /// <summary>Checks the conditions every game hour, after the players' hourly statistics update.</summary>
    public void Start(GameClock clock) => clock.ScheduleIn(GameClock.MinutesPerHour, () => HourTick(clock));

    private void HourTick(GameClock clock)
    {
        if (!IsInstanceValid(this) || AllCompleted)
            return;
        foreach (var vc in Conditions)
        {
            var team = Teams.GetTeamByPlayerId(vc.TargetPlayer);
            if (team?.Org == null)
                continue;
            switch (vc.Type)
            {
                case VictoryCondition.ConditionType.Money:
                    // VCOND 1 compares the weekly net income (HQ +14h), not the cash.
                    vc.UpdateMoneyProgress(team.Org.NetIncome);
                    break;
                default:
                    vc.Evaluate(team);
                    break;
            }
        }
        var winner = CheckAll();
        if (winner >= 0)
        {
            PlayerWon?.Invoke(winner);
            CampaignManager.Instance?.OnPlayerWon(winner);
            return;
        }
        clock.ScheduleIn(GameClock.MinutesPerHour, () => HourTick(clock));
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
