using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Capone;

/// <summary>One victory condition for one player.</summary>
public class VictoryCondition
{
    public enum ConditionType
    {
        Money,            // VCOND 1: earn N money
        CompanyCount,     // VCOND 2: create N companies of a type
        UnitType,         // VCOND 4: create a unit of a type
        CaptureArea,      // VCOND 8: capture N companies in a rectangle
        MarketShare,      // VCOND 5: capture X % of the market
        CompanyCountType, // VCOND 6: capture N companies of a type
        UnitReach,        // VCOND 7: a unit reaches a point
        UnitKill,         // VCOND 3: kill N units of a type
    }

    /// <summary>Maps the VCOND number from the .epi file to ConditionType.</summary>
    public static ConditionType? FromEpiType(int epiType) => epiType switch
    {
        1 => ConditionType.Money,
        2 => ConditionType.CompanyCount,
        3 => ConditionType.UnitKill,
        4 => ConditionType.UnitType,
        5 => ConditionType.MarketShare,
        6 => ConditionType.CompanyCountType,
        7 => ConditionType.UnitReach,
        8 => ConditionType.CaptureArea,
        _ => null,
    };

    public ConditionType Type { get; }
    /// <summary>Player the condition applies to.</summary>
    public int TargetPlayer { get; }
    /// <summary>Raw parameters from the .epi file.</summary>
    public List<string> Parameters { get; }

    public bool IsCompleted { get; private set; }

    public int CurrentProgress { get; private set; }
    public int RequiredProgress { get; private set; }

    public int MoneyTarget { get; private set; }
    public CompanyCommon.CompanyType CompanyType { get; private set; } = CompanyCommon.CompanyType.Free;
    public UnitCommon.UnitType UnitType { get; private set; } = UnitCommon.UnitType.Bat;
    public Vector2I TopLeft { get; private set; }
    public Vector2I BottomRight { get; private set; }
    public float TargetPercentage { get; private set; }
    public Vector2I TargetCoords { get; private set; }
    public UnitCommon.UnitType TargetUnitType { get; private set; } = UnitCommon.UnitType.Bat;
    public int RequiredKills { get; private set; } = 1;
    public int CurrentKills { get; private set; }

    /// <summary>Called once when the condition completes.</summary>
    public Action OnUpdate { get; set; }

    public VictoryCondition(ConditionType type, int targetPlayer, List<string> parameters)
    {
        Type = type;
        TargetPlayer = targetPlayer;
        Parameters = parameters ?? new List<string>();
        ParseParameters();
    }

    private void ParseParameters()
    {
        var p = Parameters;
        if (p.Count == 0)
            return;

        switch (Type)
        {
            case ConditionType.Money: // VCOND 1 player amount
                MoneyTarget = ToInt(p[0]);
                RequiredProgress = MoneyTarget;
                break;
            case ConditionType.CompanyCount: // VCOND 2 player companyType count
            case ConditionType.MarketShare: // VCOND 5 player companyType percentage
            case ConditionType.CompanyCountType: // VCOND 6 player companyType count
                if (p.Count >= 2)
                    RequiredProgress = ToInt(p[1]);
                break;
            case ConditionType.UnitType: // VCOND 4 player unitType
                if (Enum.TryParse<UnitCommon.UnitType>(p[0], out var unitType))
                    UnitType = unitType;
                break;
            case ConditionType.CaptureArea: // VCOND 8 player count x1 y1 x2 y2
                if (p.Count >= 5)
                {
                    RequiredProgress = ToInt(p[0]);
                    TopLeft = new Vector2I(ToInt(p[1]), ToInt(p[2]));
                    BottomRight = new Vector2I(ToInt(p[3]), ToInt(p[4]));
                }
                break;
            case ConditionType.UnitReach: // VCOND 7 player unitType x y
                if (p.Count >= 3)
                {
                    if (Enum.TryParse<UnitCommon.UnitType>(p[0], out var reachType))
                        UnitType = reachType;
                    TargetCoords = new Vector2I(ToInt(p[1]), ToInt(p[2]));
                }
                break;
            case ConditionType.UnitKill: // VCOND 3 player unitType [count]
                if (Enum.TryParse<UnitCommon.UnitType>(p[0], out var killType))
                    TargetUnitType = killType;
                RequiredKills = p.Count >= 2 ? ToInt(p[1]) : 1;
                break;
        }
    }

    /// <summary>
    /// True if completed. Progress is pushed in through the Update* methods;
    /// the per-type checks below are still stubs (see plan.md, phase 8).
    /// </summary>
    public bool Check() => IsCompleted;

    public void UpdateMoneyProgress(int currentMoney)
    {
        if (Type != ConditionType.Money)
            return;
        CurrentProgress = currentMoney;
        if (currentMoney >= MoneyTarget)
            Complete();
    }

    public void UpdateCompanyCountProgress(int count)
    {
        if (Type != ConditionType.CompanyCount)
            return;
        CurrentProgress = count;
        if (count >= RequiredProgress)
            Complete();
    }

    public void UpdateKillProgress(UnitCommon.UnitType killedUnitType, int killedPlayerId)
    {
        if (Type != ConditionType.UnitKill || killedUnitType != TargetUnitType)
            return;
        CurrentKills++;
        CurrentProgress = CurrentKills;
        if (CurrentKills >= RequiredKills)
            Complete();
    }

    private void Complete()
    {
        if (IsCompleted)
            return;
        IsCompleted = true;
        CurrentProgress = RequiredProgress;
        GD.Print("VictoryCondition: condition ", Type, " completed for player ", TargetPlayer);
        OnUpdate?.Invoke();
    }

    public string GetDescription() => Type switch
    {
        ConditionType.Money => $"Заработать {MoneyTarget} денег",
        ConditionType.CompanyCount => $"Создать {RequiredProgress} компаний",
        ConditionType.UnitType => $"Создать юнита {UnitType}",
        ConditionType.CaptureArea => $"Захватить {RequiredProgress} компаний в области",
        ConditionType.MarketShare => $"Захватить {RequiredProgress}% рынка",
        ConditionType.CompanyCountType => $"Захватить {RequiredProgress} компаний",
        ConditionType.UnitReach => $"Довести {UnitType} до точки",
        ConditionType.UnitKill => $"Убить {RequiredKills} {TargetUnitType}",
        _ => "Unknown condition",
    };

    /// <summary>Progress in percent (0-100).</summary>
    public float GetProgressPercent() =>
        RequiredProgress <= 0 ? 100.0f : Mathf.Clamp((float)CurrentProgress / RequiredProgress * 100.0f, 0.0f, 100.0f);

    private static int ToInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
