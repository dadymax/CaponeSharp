using System.Linq;
using System;
using Godot;

namespace Capone;

/// <summary>
/// One victory condition for one player, from a VCOND line. Field layouts follow the EXE's per-type readers
/// (sub_4670A5, 0x4678EA…0x467BFA); the meaning of some values is still a guess and marked so.
/// </summary>
public class VictoryCondition
{
    public enum ConditionType
    {
        Money = 1,            // VCOND 1 p money
        CompanyCount = 2,     // VCOND 2 p count businessType (sub_463A13: +8 count, +0Ch type)
        UnitKill = 3,         // VCOND 3 p unitType (12 = the enemy boss)
        UnitCreate = 4,       // VCOND 4 p unitType
        MarketShare = 5,      // VCOND 5 p share companyType
        CompanyCountType = 6, // VCOND 6 p district: own a business in that district (sub_4638D0)
        UnitReach = 7,        // VCOND 7 p x y size unitType
        CaptureArea = 8,      // VCOND 8 p x1 y1 x2 y2 companyType count
        Unknown9 = 9,         // VCOND 9 p a b (not used by the episodes)
    }

    public static ConditionType? FromEpiType(int epiType) =>
        Enum.IsDefined(typeof(ConditionType), epiType) ? (ConditionType)epiType : null;

    public ConditionType Type { get; }
    /// <summary>Player the condition applies to.</summary>
    public int TargetPlayer { get; }
    public Scenario.EpiVictoryCondition Source { get; }

    public bool IsCompleted { get; private set; }
    public long CurrentProgress { get; private set; }
    public long RequiredProgress { get; private set; }

    public long MoneyTarget { get; private set; }
    /// <summary>Raw company/business type number (meaning not decoded yet).</summary>
    public int CompanyType { get; private set; }
    /// <summary>Raw unit type number; see UnitType for the decoded value.</summary>
    public int OriginalUnitType { get; private set; } = -1;
    /// <summary>Null when the number is not a unit type (VCOND 7 uses 13, probably "any unit").</summary>
    public UnitCommon.UnitType? UnitType { get; private set; }
    /// <summary>Original map coordinates.</summary>
    public Vector2I TopLeft { get; private set; }
    public Vector2I BottomRight { get; private set; }
    public Vector2I TargetCoords { get; private set; }
    /// <summary>VCOND 7 third value (13, 5 or 3 seen): size of the target area, probably.</summary>
    public int TargetSize { get; private set; }
    public float TargetShare { get; private set; }
    /// <summary>VCOND 6: district 0 slum, 1 middle class, 2 center.</summary>
    public int District { get; private set; }

    /// <summary>Called once when the condition completes.</summary>
    public Action OnUpdate { get; set; }

    public VictoryCondition(Scenario.EpiVictoryCondition source)
    {
        Source = source;
        Type = (ConditionType)source.ConditionType;
        TargetPlayer = source.TargetPlayer;
        ParseValues(source);
    }

    private void ParseValues(Scenario.EpiVictoryCondition s)
    {
        int V(int i) => i < s.Values.Count ? (int)s.Values[i] : 0;
        switch (Type)
        {
            case ConditionType.Money:
                MoneyTarget = s.Values.Count > 0 ? s.Values[0] : 0;
                RequiredProgress = MoneyTarget;
                break;
            case ConditionType.CompanyCount:
                RequiredProgress = V(0);
                CompanyType = V(1);
                break;
            case ConditionType.UnitKill:
            case ConditionType.UnitCreate:
                SetUnitType(V(0));
                RequiredProgress = 1;
                break;
            case ConditionType.MarketShare:
                TargetShare = s.Share;
                CompanyType = V(0);
                break;
            case ConditionType.CompanyCountType:
                District = V(0);
                RequiredProgress = 1;
                break;
            case ConditionType.UnitReach:
                TargetCoords = new Vector2I(V(0), V(1));
                TargetSize = V(2);
                SetUnitType(V(3));
                RequiredProgress = 1;
                break;
            case ConditionType.CaptureArea:
                TopLeft = new Vector2I(V(0), V(1));
                BottomRight = new Vector2I(V(2), V(3));
                CompanyType = V(4);
                RequiredProgress = V(5);
                break;
        }
    }

    private void SetUnitType(int original)
    {
        OriginalUnitType = original;
        UnitType = UnitCommon.FromOriginalId(original);
    }

    /// <summary>True if completed. Progress is pushed in through the Update* methods (checks for most types are not written yet).</summary>
    public bool Check() => IsCompleted;

    /// <summary>
    /// The IsMet virtuals of the VCOND classes (checked every game hour):
    /// 2 sub_463A40 — the player owns at least <c>count</c> companies of business type <c>type</c>;
    /// 3 sub_463B04 — no living unit of the type exists (anyone's);
    /// 4 sub_463774 — a unit of the type exists (anyone's; the original doesn't check the owner);
    /// 5 sub_4639B0 — the market share of the type is above the target (the original reads the first HQ);
    /// 6 sub_4638D0 — the player owns a business (not legal, not support) in the district.
    /// </summary>
    public void Evaluate(Team team)
    {
        if (IsCompleted || Engine.GetMainLoop() is not SceneTree tree)
            return;
        var companies = tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(c => c.IsEconomic);
        var units = tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => !u.IsDead);
        switch (Type)
        {
            case ConditionType.CompanyCount:
                CurrentProgress = companies.Count(c => c.OwnerTeam == team && (int)c.BusinessType == CompanyType);
                if (CurrentProgress >= RequiredProgress)
                    Complete();
                break;
            case ConditionType.UnitKill:
                if (!units.Any(u => (int)u.UnitType == OriginalUnitType))
                    Complete();
                break;
            case ConditionType.UnitCreate:
                if (units.Any(u => (int)u.UnitType == OriginalUnitType))
                    Complete();
                break;
            case ConditionType.MarketShare:
                if (team.Org != null && CompanyType is >= 0 and < Organisation.TypeCount && team.Org.MarketShare[CompanyType] > TargetShare)
                    Complete();
                break;
            case ConditionType.CompanyCountType:
                if (companies.Any(c => c.OwnerTeam == team && (int)c.BusinessType is not (0 or 5) && c.Block?.District == District))
                    Complete();
                break;
        }
    }

    public void UpdateMoneyProgress(long currentMoney)
    {
        if (Type != ConditionType.Money)
            return;
        CurrentProgress = currentMoney;
        if (currentMoney > MoneyTarget)
            Complete();
    }

    public void UpdateCompanyCountProgress(int count)
    {
        if (Type is not (ConditionType.CompanyCount or ConditionType.CompanyCountType))
            return;
        CurrentProgress = count;
        if (count >= RequiredProgress)
            Complete();
    }

    public void UpdateKillProgress(UnitCommon.UnitType killedUnitType, int killedPlayerId)
    {
        if (Type != ConditionType.UnitKill || killedUnitType != UnitType)
            return;
        CurrentProgress++;
        if (CurrentProgress >= RequiredProgress)
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

    /// <summary>Debug description. The player-facing objective is the episode's OBJECTIVETEXT.</summary>
    public string GetDescription() => Type switch
    {
        ConditionType.Money => $"Money {MoneyTarget}",
        ConditionType.CompanyCount => $"{RequiredProgress} companies of type {CompanyType}",
        ConditionType.UnitKill => $"Kill {UnitType?.ToString() ?? OriginalUnitType.ToString()}",
        ConditionType.UnitCreate => $"Create {UnitType?.ToString() ?? OriginalUnitType.ToString()}",
        ConditionType.MarketShare => $"{TargetShare:P0} market share of type {CompanyType}",
        ConditionType.CompanyCountType => $"{RequiredProgress} companies",
        ConditionType.UnitReach => $"{UnitType?.ToString() ?? OriginalUnitType.ToString()} reaches {TargetCoords} (size {TargetSize})",
        ConditionType.CaptureArea => $"{RequiredProgress} companies of type {CompanyType} in {TopLeft}-{BottomRight}",
        _ => $"VCOND {(int)Type}",
    };

    public float GetProgressPercent() =>
        RequiredProgress <= 0 ? 100.0f : Mathf.Clamp((float)CurrentProgress / RequiredProgress * 100.0f, 0.0f, 100.0f);
}
