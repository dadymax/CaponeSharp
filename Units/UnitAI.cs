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
        CanAttack != null && HasWeapon && (OwnerTeam != null || IsRogue || IsDeserter) && !IsDead && !IsArrested
        && UnitType is not (UnitCommon.UnitType.Collector or UnitCommon.UnitType.PoliceCar
            or UnitCommon.UnitType.Policeman or UnitCommon.UnitType.Cadillac);

    /// <summary>Called from <see cref="_Process"/>.</summary>
    private void ThinkTick()
    {
        var clock = GameClock.Instance;
        if (clock == null || clock.Minute < _nextThink)
            return;
        _nextThink = clock.Minute + ThinkMinutes;
        if (MoveMode != null && !IsMoving)
            MoveMode = null; // the move order reached its end
        // The original thinks once per game hour; the spread-out step runs at that rate.
        var spreadDue = clock.Minute >= _nextSpread;
        if (spreadDue)
            _nextSpread = clock.Minute + GameClock.MinutesPerHour;
        // A patrol leg is a move order with mode 3 (sub_4292E0); other orders (extortion…) don't fight.
        if (!ThinksForItself || (CurrentOrder != null && CurrentOrder is not PatrolOrder))
        {
            if (spreadDue)
                SpreadOut();
            return;
        }
        if (Stance == 0)
            Stance = DefaultStance;
        Think();
        // sub_42AA86: the idle branch ends with vt+15Ch when the unit didn't take up a fight.
        if (spreadDue && !(CanAttack.TargetParent is Unit { IsDead: false } && CanAttack.TargetInRange()))
            SpreadOut();
    }

    private long _nextSpread;

    /// <summary>
    /// Mode of the running move / attack order (sub_4284B0's last argument; order vt+90h), null without one:
    /// 0 Force Go (and Go for the Terrorist and Thief) — never fights; 1 Force Attack (and the Terrorist's Attack) —
    /// only the ordered target; 2 Go — the nearest enemy in range; 3 Attack — the target, else the nearest enemy;
    /// 4 enemy buildings (action 4, not on the menu yet). When the unit opens fire the order is dropped (order vt+7Ch)
    /// and the unit goes on with its idle stance.
    /// </summary>
    public int? MoveMode { get; private set; }

    private Unit _orderTarget;

    /// <summary>Go / Force Go to a map point (actions 0 / 5 of sub_42C1F6).</summary>
    public void GoTo(Vector2 localPosition, bool force)
    {
        MoveTo(localPosition);
        _orderTarget = null;
        MoveMode = force || UnitType is UnitCommon.UnitType.Terrorist or UnitCommon.UnitType.Thief ? 0 : 2;
    }

    /// <summary>Attack / Force Attack a unit or building (actions 1 / 6).</summary>
    public void AttackOrder(Node2D target, bool force)
    {
        TargetStructure = target;
        _orderTarget = target as Unit;
        MoveMode = force || UnitType == UnitCommon.UnitType.Terrorist ? 1 : 3;
    }

    /// <summary>Order-mode rules while the order runs (sub_42AA86 switch at 0x42AF52). Returns true if handled.</summary>
    private bool ThinkUnderOrder(Unit nearest)
    {
        var patrol = CurrentOrder is PatrolOrder;
        var orderMode = patrol ? 3 : MoveMode;
        if (orderMode is not { } mode)
            return false;
        if (!IsMoving)
        {
            if (!patrol)
                return false;
            // Waiting at an end of the patrol: shoot what comes in range, don't chase.
            if (nearest != null && InRange(nearest) && CanAttack.TargetParent is not Unit { IsDead: false })
                CanAttack.Target = nearest.CanBeAttacked;
            return true;
        }
        if (patrol)
            _orderTarget = null;
        var target = _orderTarget is { IsDead: false } t && IsInstanceValid(t) ? t : null;
        Unit fireAt = mode switch
        {
            1 => target != null && InRange(target) ? target : null,
            2 => nearest != null && InRange(nearest) ? nearest : null,
            3 => target != null && InRange(target) ? target : nearest != null && InRange(nearest) ? nearest : null,
            _ => null,
        };
        if (fireAt != null)
        {
            StopMoving();
            MoveMode = null;
            Stance = DefaultStance;
            CanAttack.Target = fireAt.CanBeAttacked;
        }
        return true;
    }

    private bool InRange(Node2D target) => Sees(target) && MapTileMath.GetDistanceExact(this, target) <= CanAttack.AttackRange;

    private void Think()
    {
        Unit passive = null, active = null;
        float passiveDist = float.MaxValue, activeDist = float.MaxValue;
        foreach (var node in GetTree().GetNodesInGroup(GroupName))
        {
            if (node is not Unit other || other == this || other.IsDead || other.IsArrested || other.InsideBuilding)
                continue;
            if (!IsHostileTo(other))
                continue;
            if (!other.IsExposed)
                continue; // vt+19Ch: disguised units (and the hidden Hit man) are never picked by themselves
            if (!Sees(other))
                continue;
            var d = MapTileMath.GetDistanceExact(this, other);
            if (other.IsFighting)
            {
                if (d < activeDist) { activeDist = d; active = other; }
            }
            else if (d < passiveDist) { passiveDist = d; passive = other; }
        }
        var nearest = passiveDist < activeDist ? passive : active;
        var nearestDist = Mathf.Min(passiveDist, activeDist);

        if (ThinkUnderOrder(nearest))
            return;
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
    internal bool IsHostileTo(Unit other)
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
