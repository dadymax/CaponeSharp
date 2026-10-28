using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Raid: each attack damages every enemy unit within AreaRadius tiles.</summary>
public partial class RaidAbility : Node
{
    private Unit _unitParent;
    private CanAttack _canAttack;

    /// <summary>Area damage radius in tiles.</summary>
    public int AreaRadius { get; set; } = 2;

    public override void _Ready()
    {
        _canAttack = GetParent() as CanAttack;
        _unitParent = GetParent()?.GetParent()?.GetParent() as Unit;
    }

    public bool IsInArea(Node2D target) =>
        _unitParent != null && target != null && MapTileMath.GetDistance(_unitParent, target) <= AreaRadius;

    public List<Unit> GetTargetsInArea()
    {
        var targets = new List<Unit>();
        if (_unitParent == null || !_unitParent.IsInsideTree())
            return targets;

        var myColor = _unitParent.TeamColor;
        foreach (var node in GetTree().GetNodesInGroup(Unit.GroupName))
        {
            if (node is not Unit unit || unit.CanBeAttacked == null || unit.IsArrested || unit.InsideBuilding || !IsInArea(unit))
                continue;
            if (unit.TeamColor == myColor)
                continue;
            var relation = Teams.GetOrAddTeam(myColor).GetTargetRelation(unit.TeamColor);
            if (relation is Teams.TeamRelation.Enemy or Teams.TeamRelation.Neutral)
                targets.Add(unit);
        }
        return targets;
    }

    /// <summary>Damages every enemy in the area; returns how many were hit.</summary>
    public int ApplyAreaDamage(int baseDamage)
    {
        var hitCount = 0;
        foreach (var unit in GetTargetsInArea())
        {
            unit.CanBeAttacked.ReceiveDamage(baseDamage, _canAttack);
            hitCount++;
        }
        return hitCount;
    }
}
