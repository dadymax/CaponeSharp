using Godot;

namespace Capone;

/// <summary>
/// Unit "think" (unit vt+5Ch, sub_42AA86): every few game minutes an idle gangster looks at the enemy units it can see
/// (vision radius; hidden Hit men excluded) and picks the nearest one, preferring a passive target if it is nearer than
/// the nearest one that is fighting (vt+11Ch: attacking, or hit within the last game hour). Then, by stance (+0C0h):
/// it keeps shooting its current target while in range; otherwise shoots the nearest enemy in range unless the stance
/// is 3 (hold); otherwise chases it — stance 2 / 4 always, stance 1 only within 5 cells (flt_4845EC) — and a stance-1
/// unit also chases an enemy that is fighting. Default stance (vt+1B0h): 3 for the Terrorist, 1 otherwise.
/// TODO: the per-order stances (orders 17h/22h/27h/28h use the order's mode), enemy buildings (stance 4),
/// the Terrorist's Cadillac rule, unowned (police) units treating every owned unit as an enemy.
/// </summary>
public partial class Unit
{
    public const int ThinkMinutes = 5;
    public const float ChaseRadius = 5f;

    /// <summary>+0C0h: 1 defensive, 2 aggressive, 3 hold, 4 destroy.</summary>
    public int Stance { get; set; }

    /// <summary>+0CCh: last game minute this unit attacked or was hit.</summary>
    public long LastFoughtMinute { get; set; } = long.MinValue / 2;

    /// <summary>vt+11Ch (sub_42E454): fighting now or within the last game hour.</summary>
    public bool IsFighting => (GameClock.Instance?.Minute ?? 0) - LastFoughtMinute < GameClock.MinutesPerHour;

    public void MarkFighting() => LastFoughtMinute = GameClock.Instance?.Minute ?? 0;

    private long _nextThink;

    /// <summary>vt+1B0h (sub_42A438).</summary>
    public int DefaultStance => UnitType == UnitCommon.UnitType.Terrorist ? 3 : 1;

    private bool ThinksForItself =>
        CanAttack != null && (OwnerTeam != null || IsRogue || IsDeserter) && !IsDead && !IsArrested
        && UnitType is not (UnitCommon.UnitType.Collector or UnitCommon.UnitType.PoliceCar
            or UnitCommon.UnitType.Policeman or UnitCommon.UnitType.Cadillac);

    /// <summary>Called from <see cref="_Process"/>.</summary>
    private void ThinkTick()
    {
        var clock = GameClock.Instance;
        if (clock == null || clock.Minute < _nextThink)
            return;
        _nextThink = clock.Minute + ThinkMinutes;
        if (!ThinksForItself || CurrentOrder != null)
            return;
        if (Stance == 0)
            Stance = DefaultStance;
        Think();
    }

    private bool InRange(Node2D target) => MapTileMath.GetDistanceExact(this, target) <= CanAttack.AttackRange;

    private void Think()
    {
        Unit passive = null, active = null;
        float passiveDist = float.MaxValue, activeDist = float.MaxValue;
        var vision = _unitInstance?.VisionRadius ?? 10;
        foreach (var node in GetTree().GetNodesInGroup(GroupName))
        {
            if (node is not Unit other || other == this || other.IsDead || other.IsArrested)
                continue;
            if (!IsHostileTo(other))
                continue;
            if (other.CanAttack != null && !other.CanAttack.CheckStealth(TeamColor))
                continue;
            var d = MapTileMath.GetDistanceExact(this, other);
            if (d > vision)
                continue;
            if (other.IsFighting)
            {
                if (d < activeDist) { activeDist = d; active = other; }
            }
            else if (d < passiveDist) { passiveDist = d; passive = other; }
        }
        var nearest = passiveDist < activeDist ? passive : active;
        var nearestDist = Mathf.Min(passiveDist, activeDist);

        // Keep shooting the current target while it is in range.
        if (CanAttack.TargetParent is Unit { IsDead: false } current && InRange(current))
            return;
        if (nearest != null && InRange(nearest) && Stance != 3)
        {
            CanAttack.Target = nearest.CanBeAttacked;
            return;
        }
        if (IsMoving)
            return;
        if (nearest != null && (Stance is 2 or 4 || (Stance == 1 && nearestDist <= ChaseRadius)))
        {
            Chase(nearest);
            return;
        }
        if (active != null && Stance == 1)
            Chase(active);
    }

    /// <summary>
    /// Enemy players' units; a rogue unit and everyone around it are enemies to each other; a deserter and every
    /// player's unit are enemies (deserters leave each other and the police alone).
    /// </summary>
    private bool IsHostileTo(Unit other)
    {
        if (IsRogue || other.IsRogue)
            return true;
        if (IsDeserter)
            return other.OwnerTeam != null;
        if (other.IsDeserter)
            return OwnerTeam != null;
        return OwnerTeam != null && other.OwnerTeam != null
               && OwnerTeam.GetTargetRelation(other.TeamColor) == Teams.TeamRelation.Enemy;
    }

    /// <summary>vt+164h: go after <paramref name="target"/> and attack it.</summary>
    private void Chase(Unit target)
    {
        if (CanAttack == null)
            return;
        CanAttack.Target = target.CanBeAttacked;
        WalkTo(target);
    }
}
