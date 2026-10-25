using Godot;

namespace Capone;

/// <summary>Hitman: invisible to enemies until he attacks, then visible for RevealDuration seconds.</summary>
public partial class HitmanStealth : Node
{
    private Unit _unitParent;
    private CanAttack _canAttack;

    /// <summary>Seconds the hitman stays visible after attacking.</summary>
    public float RevealDuration { get; set; } = 3.0f;
    private float _revealTimer;

    public bool IsStealthed { get; private set; } = true;

    public override void _Ready()
    {
        _canAttack = GetParent() as CanAttack;
        _unitParent = GetParent()?.GetParent()?.GetParent() as Unit;
        IsStealthed = true;
    }

    public override void _Process(double delta)
    {
        if (IsStealthed)
            return;
        _revealTimer -= (float)delta;
        if (_revealTimer <= 0)
            IsStealthed = true;
    }

    public bool IsVisibleToEnemy(Color targetColor)
    {
        if (IsStealthed || _unitParent == null)
            return false;
        var relation = Teams.GetOrAddTeam(_unitParent.TeamColor).GetTargetRelation(targetColor);
        return relation is Teams.TeamRelation.Enemy or Teams.TeamRelation.Neutral;
    }

    public void OnAttackInitiated()
    {
        IsStealthed = false;
        _revealTimer = RevealDuration;
    }

    /// <summary>While stealthed the hitman may only strike an adjacent target.</summary>
    public bool CanAttackTarget(Color targetColor)
    {
        if (!IsStealthed)
            return true;
        if (_canAttack?.TargetParent == null || _unitParent == null)
            return false;
        return MapTileMath.GetDistance(_unitParent, _canAttack.TargetParent) <= 1;
    }

    public void ResetStealth()
    {
        IsStealthed = true;
        _revealTimer = 0f;
    }
}
