using Godot;

namespace Capone;

/// <summary>Bomb: suicide attack. Deals its damage once and removes the unit. Cannot capture companies.</summary>
public partial class BombAbility : Node
{
    [Signal] public delegate void BombExplodedEventHandler(Node2D target);

    private Unit _unitParent;
    private CanAttack _canAttack;

    public bool HasExploded { get; private set; }

    public override void _Ready()
    {
        _canAttack = GetParent() as CanAttack;
        _unitParent = GetParent()?.GetParent()?.GetParent() as Unit;
        if (_canAttack != null)
            _canAttack.CanCaptureNeutral = false;
    }

    public bool CanAttack() => !HasExploded;

    public void ApplySuicideDamage(CanBeAttacked target, int baseDamage)
    {
        if (target == null || HasExploded)
            return;

        HasExploded = true;
        var targetParent = _canAttack?.TargetParent;
        target.ReceiveDamage(baseDamage, _canAttack);
        EmitSignal(SignalName.BombExploded, targetParent);
        _unitParent?.QueueFree();
    }

    public void Reset() => HasExploded = false;
}
