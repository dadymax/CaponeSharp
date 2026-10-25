using Godot;

namespace Capone;

/// <summary>Thompson: piercing shots that partly ignore the target's evasion.</summary>
public partial class ThompsonAbility : Node
{
    private const float MaxEvasion = 15.0f;

    private CanAttack _canAttack;

    /// <summary>Base pierce chance (0..1).</summary>
    public float BasePierceChance { get; set; } = 0.5f;
    /// <summary>Lowest pierce chance, even against very evasive targets.</summary>
    public float MinPierceChance { get; set; } = 0.1f;

    public override void _Ready() => _canAttack = GetParent() as CanAttack;

    public bool CheckPierce(CanBeAttacked target)
    {
        if (target == null)
            return false;
        if (target.EvasionFactor <= 0)
            return true;
        return GD.Randf() < GetEffectivePierceChance(target);
    }

    /// <summary>Higher target evasion lowers the pierce chance by up to 70 %, never below MinPierceChance.</summary>
    public float GetEffectivePierceChance(CanBeAttacked target)
    {
        if (target == null || target.EvasionFactor <= 0)
            return BasePierceChance;
        var ratio = Mathf.Min(target.EvasionFactor / MaxEvasion, 1.0f);
        var effective = BasePierceChance * (1.0f - ratio * 0.7f);
        return Mathf.Max(effective, MinPierceChance);
    }

    public void ApplyPiercingDamage(CanBeAttacked target, int baseDamage)
    {
        if (target == null)
            return;
        // TODO: a pierced shot should bypass evasion; both paths currently deal normal damage (as in the GDScript version).
        CheckPierce(target);
        target.ReceiveDamage(baseDamage, _canAttack);
    }
}
