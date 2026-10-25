using Godot;

namespace Capone;

/// <summary>
/// Resistance when capturing a neutral company:
/// 1. each attack rolls the resistance chance;
/// 2. once resistance has started it grows with every round;
/// 3. it is never absolute, there is always a chance to break it.
/// </summary>
public partial class CompanyResistance : Node
{
    /// <summary>Unit cost at which resistance drops to zero.</summary>
    public const int MaxUnitCost = 150000;

    [Signal] public delegate void ResistanceStartedEventHandler(int level);
    [Signal] public delegate void ResistanceBrokenEventHandler();
    [Signal] public delegate void AttackRepelledEventHandler(Node2D attackerParent);

    /// <summary>Chance at the first level (10 %).</summary>
    [Export] public float ResistChanceBase { get; set; } = 0.1f;
    /// <summary>Multiplier per escalation level.</summary>
    [Export] public float ResistChanceMultiplier { get; set; } = 1.5f;
    [Export] public int MaxResistanceLevel { get; set; } = 5;

    public bool ResistanceActive { get; private set; }
    /// <summary>0 = none, 1+ = escalating.</summary>
    public int ResistanceLevel { get; private set; }

    private Node2D _parentCompany;

    public override void _Ready() => _parentCompany = GetParent() as Node2D;

    /// <summary>
    /// Called on every attack against the neutral company.
    /// Returns true if the attack was repelled, false if the company is captured.
    /// </summary>
    public bool ResistAttack(Node2D attackerParent, int attackerCost = 0)
    {
        if (GD.Randf() < GetResistChance(attackerCost))
        {
            if (!ResistanceActive)
            {
                ResistanceActive = true;
                ResistanceLevel = 1;
                EmitSignal(SignalName.ResistanceStarted, ResistanceLevel);
                GD.Print("[CompanyResistance] Resistance started, level ", ResistanceLevel);
            }
            else
            {
                ResistanceLevel = Mathf.Min(ResistanceLevel + 1, MaxResistanceLevel);
                GD.Print("[CompanyResistance] Resistance grows, level ", ResistanceLevel);
            }
            EmitSignal(SignalName.AttackRepelled, attackerParent);
            return true;
        }

        if (ResistanceActive)
        {
            GD.Print("[CompanyResistance] Resistance broken at level ", ResistanceLevel);
            EmitSignal(SignalName.ResistanceBroken);
        }
        ResistanceActive = false;
        ResistanceLevel = 0;
        return false;
    }

    /// <summary>baseChance * multiplier^level * (1 - attackerCost / MaxUnitCost), clamped to 0..0.95.</summary>
    public float GetResistChance(int attackerCost = 0)
    {
        var baseChance = ResistChanceBase;
        if (ResistanceActive)
            baseChance *= Mathf.Pow(ResistChanceMultiplier, ResistanceLevel);
        var powerMod = (float)attackerCost / MaxUnitCost;
        return Mathf.Clamp(baseChance * (1.0f - powerMod), 0.0f, 0.95f);
    }

    public void ResetResistance()
    {
        ResistanceActive = false;
        ResistanceLevel = 0;
    }

    /// <summary>Resistance state for the UI.</summary>
    public Godot.Collections.Dictionary GetStatus(int attackerCost = 0) => new()
    {
        ["active"] = ResistanceActive,
        ["level"] = ResistanceLevel,
        ["chance"] = GetResistChance(attackerCost),
        ["maxLevel"] = MaxResistanceLevel,
        ["maxUnitCost"] = MaxUnitCost,
    };
}
