using Godot;

namespace Capone;

/// <summary>Sniper: attack range equals vision; farther targets take more damage (up to 2x).</summary>
public partial class SniperAbility : Node
{
    private const float MaxRangeMultiplier = 2.0f;

    private Unit _unitParent;
    private CanAttack _canAttack;

    /// <summary>Base vision radius of the unit.</summary>
    public int BaseVisionRadius { get; set; } = 10;

    public override void _Ready()
    {
        _canAttack = GetParent() as CanAttack;
        _unitParent = GetParent()?.GetParent()?.GetParent() as Unit;
        if (_unitParent?.UnitData != null)
            BaseVisionRadius = _unitParent.UnitData.VisionRadius;
    }

    private bool HasTarget => _canAttack?.Target != null && _canAttack.TargetParent != null && _unitParent != null;

    /// <summary>Current attack range: vision radius if the target is within it, 0 otherwise.</summary>
    public float UpdateAttackRange()
    {
        if (!HasTarget)
            return BaseVisionRadius;
        var distance = MapTileMath.GetDistance(_unitParent, _canAttack.TargetParent);
        return distance <= BaseVisionRadius ? BaseVisionRadius : 0f;
    }

    public float GetDamageMultiplier()
    {
        if (!HasTarget)
            return 1.0f;
        var distance = MapTileMath.GetDistance(_unitParent, _canAttack.TargetParent);
        if (distance <= 0)
            return 1.0f;
        var ratio = (float)distance / BaseVisionRadius;
        return 1.0f + ratio * (MaxRangeMultiplier - 1.0f);
    }

    /// <summary>Farther targets reload a bit slower.</summary>
    public float GetReloadTime()
    {
        if (_canAttack == null)
            return 3.0f;
        var distance = HasTarget ? MapTileMath.GetDistance(_unitParent, _canAttack.TargetParent) : 0;
        if (distance <= 0)
            return _canAttack.AttackRate;
        var ratio = (float)distance / BaseVisionRadius;
        return _canAttack.AttackRate * (1.0f + ratio * 0.5f);
    }

    public void ApplyDamage(CanBeAttacked target, int baseDamage)
    {
        if (target == null || _canAttack == null)
            return;
        target.ReceiveDamage((int)(baseDamage * GetDamageMultiplier()), _canAttack);
    }
}
