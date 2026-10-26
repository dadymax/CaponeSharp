using Godot;

namespace Capone;

/// <summary>
/// Extortion: the order a gangster gets when sent to a company (original vtable off_483D24, step sub_428C11).
/// The gangster walks next to the company; then every game hour the company is asked to pay (company vt+164h,
/// <see cref="Company.Extort"/>). On a refusal the attempt counter grows and the gangster hits the building once
/// (unit vt+104h). The order ends when the company pays, the gangster can't reach it, dies or gets a new order.
/// </summary>
public partial class ExtortOrder : Node
{
    public const int AttemptMinutes = GameClock.MinutesPerHour;

    private readonly Unit _unit;
    private readonly Company _company;
    private int _attempt;
    private bool _walking;
    private bool _finished;

    /// <summary>Needed by Godot; use the (unit, company) constructor in code.</summary>
    public ExtortOrder() { }

    public ExtortOrder(Unit unit, Company company)
    {
        _unit = unit;
        _company = company;
        Name = "ExtortOrder";
    }

    public Company Company => _company;
    public int Attempt => _attempt;

    /// <summary>
    /// Can this unit extort that company: an economic company of another, non-allied player or nobody, and a unit
    /// that fights. TODO (R5): check the original's per-type order list (Thief, Cadillac, Godfather).
    /// </summary>
    public static bool CanExtort(Unit unit, Company company)
    {
        if (!company.IsEconomic || unit.OwnerTeam == null || company.OwnerTeam == unit.OwnerTeam)
            return false;
        if (company.OwnerTeam != null
            && unit.OwnerTeam.GetTargetRelation(company.OwnerTeam.TeamColor) == Teams.TeamRelation.Friend)
            return false;
        return unit.CanAttack != null && unit.UnitType is not (UnitCommon.UnitType.Collector
            or UnitCommon.UnitType.Cadillac or UnitCommon.UnitType.PoliceCar or UnitCommon.UnitType.Policeman);
    }

    public override void _Ready()
    {
        _unit.WalkTo(_company);
        _walking = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_walking || _finished || _unit.IsMoving)
            return;
        _walking = false;
        Step();
    }

    /// <summary>sub_428C11.</summary>
    private void Step()
    {
        if (_finished)
            return;
        if (!IsInstanceValid(_unit) || _unit.IsDead || _unit.IsArrested || !IsInstanceValid(_company)
            || MapTileMath.GetDistanceExact(_unit, _company) > CollectorJob.ReachDistance)
        {
            Finish();
            return;
        }
        if (_company.Extort(_unit, _attempt))
        {
            Finish();
            return;
        }
        _attempt++;
        _unit.CanAttack?.HitOnce(_company);
        _unit.AnimationAttackOnce(_company);
        if (_unit.IsDead || _unit.IsArrested)
        {
            Finish();
            return;
        }
        GameClock.Instance.ScheduleIn(AttemptMinutes, () =>
        {
            if (!_finished && IsInstanceValid(this))
                Step();
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
