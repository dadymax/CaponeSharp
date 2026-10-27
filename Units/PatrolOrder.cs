using Godot;

namespace Capone;

/// <summary>
/// Patrol (order 8; object sub_4292E0, vtable off_483E54): the two ends are the unit's cell and the clicked cell
/// (+60h / +64h). The unit walks to one end (shared order step sub_428500 → move order sub_4279E7); when the leg is
/// done (vt+88h, sub_4293C7) it waits one game hour (event 3EAh) and heads for the other end. It ends when the unit
/// dies or gets another order. TODO: the stance it walks with (unit vt+194h by the order's mode) — fighting on the way.
/// </summary>
public partial class PatrolOrder : Node
{
    public const int WaitMinutes = GameClock.MinutesPerHour;

    private readonly Unit _unit;
    private readonly Vector2I _a;
    private readonly Vector2I _b;
    private Vector2I _target;
    private bool _walking;
    private bool _finished;

    /// <summary>Needed by Godot; use the (unit, from, to) constructor in code.</summary>
    public PatrolOrder() { }

    public PatrolOrder(Unit unit, Vector2I from, Vector2I to)
    {
        _unit = unit;
        _a = from;
        _b = to;
        _target = to;
        Name = "PatrolOrder";
    }

    public Vector2I Target => _target;

    public override void _Ready() => Walk();

    private void Walk()
    {
        if (_finished || !IsInstanceValid(_unit) || _unit.IsDead || _unit.IsArrested)
        {
            Finish();
            return;
        }
        _unit.WalkToCell(_target);
        _walking = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_walking || _finished || !IsInstanceValid(_unit) || _unit.IsMoving)
            return;
        _walking = false;
        LegDone();
    }

    /// <summary>sub_4293C7: switch ends and start again after an hour.</summary>
    private void LegDone()
    {
        if (!IsInstanceValid(_unit) || _unit.IsDead)
        {
            Finish();
            return;
        }
        _target = _target == _b ? _a : _b;
        GameClock.Instance?.ScheduleIn(WaitMinutes, () =>
        {
            if (!_finished && IsInstanceValid(this))
                Walk();
        });
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        if (IsInstanceValid(_unit))
            _unit.OrderFinished(this);
    }
}
