using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// The Cadillac (class 5 unit with vtable off_48563C, constructor sub_42EDF0) and its passengers:
/// <list type="bullet">
/// <item>Four seats (+0FCh). vt+1F8h sub_42EE30: a unit of the same player, not a Cadillac, may board while a seat is
/// free; vt+1FCh sub_42EE90 takes it off the map into the car (its container, vt+130h).</item>
/// <item>A passenger keeps thinking: Unit::Think (sub_42AA86) moves it to the car's position first, so passengers shoot
/// from the car; the car itself has no weapon (stats 0 damage) and counts as fighting while a passenger does
/// (vt+11Ch sub_42F4E0). Passengers are not seen or hit — the car is.</item>
/// <item>Unload (action 9 "Unload", 29,9; vt+200h sub_42F3C2): every living passenger is put back on the map at the
/// car's position.</item>
/// <item>Car destroyed (vt+FCh sub_42F270): the passengers take 1000 damage when a Terrorist did it, else 20–99
/// (vt+1F4h sub_42F370), and the survivors get out.</item>
/// <item>The car only drives (vt+C0h sub_42F138: order 0 only); a unit sent onto its own player's Cadillac boards it
/// (Unit::GiveTargetedOrder sub_42C1F6 → job sub_42FC30, <see cref="BoardOrder"/>).</item>
/// </list>
/// </summary>
public partial class Unit
{
    public const int CarSeats = 4;
    private readonly Unit[] _seats = new Unit[CarSeats];

    /// <summary>The Cadillac this unit rides in.</summary>
    public Unit Vehicle { get; private set; }

    public bool IsCar => UnitType == UnitCommon.UnitType.Cadillac;

    public IEnumerable<Unit> Passengers =>
        _seats.Where(p => p != null && IsInstanceValid(p) && !p.IsDead && p.Vehicle == this);

    public bool HasPassengers => Passengers.Any();

    private int FreeSeat()
    {
        for (var i = 0; i < CarSeats; i++)
            if (_seats[i] == null || !IsInstanceValid(_seats[i]) || _seats[i].IsDead || _seats[i].Vehicle != this)
                return i;
        return -1;
    }

    /// <summary>vt+1F8h sub_42EE30.</summary>
    public bool CanBoard(Unit unit) =>
        IsCar && !IsDead && unit != null && unit != this && !unit.IsDead && !unit.IsCar && unit.Vehicle == null
        && unit.OwnerTeam != null && unit.OwnerTeam == OwnerTeam && FreeSeat() >= 0;

    /// <summary>vt+1FCh sub_42EE90.</summary>
    public bool Board(Unit unit)
    {
        if (!CanBoard(unit))
            return false;
        _seats[FreeSeat()] = unit;
        unit.CancelOrder();
        unit.StopMoving();
        unit.EnterBuilding();
        unit.Vehicle = this;
        unit.Position = Position;
        return true;
    }

    /// <summary>vt+200h sub_42F3C2: every living passenger gets out at the car's position.</summary>
    public void Unload()
    {
        for (var i = 0; i < CarSeats; i++)
        {
            var p = _seats[i];
            _seats[i] = null;
            if (p != null && IsInstanceValid(p) && !p.IsDead && p.Vehicle == this)
                p.LeaveVehicle();
        }
    }

    /// <summary>Gets out of the car where it stands.</summary>
    public void LeaveVehicle()
    {
        var car = Vehicle;
        if (car == null)
            return;
        Vehicle = null;
        if (IsInstanceValid(car))
            Position = car.Position;
        ExitBuilding();
    }

    /// <summary>vt+1F4h sub_42F370: every passenger loses <paramref name="hp"/> hit points.</summary>
    public void DamagePassengers(int hp)
    {
        foreach (var p in Passengers.ToList())
        {
            var health = p.CanBeAttacked;
            if (health == null)
                continue;
            health.HP = Mathf.Max(0, health.HP - hp);
            if (health.HP == 0)
                p.Die();
        }
    }

    /// <summary>sub_42F270: the car is destroyed — its passengers are hurt, the survivors get out.</summary>
    private void CarDestroyed()
    {
        if (!IsCar)
            return;
        var killer = CanBeAttacked?.LastAttacker;
        DamagePassengers(killer is { UnitType: UnitCommon.UnitType.Terrorist } ? 1000 : 20 + (int)(GD.Randi() % 80));
        Unload();
    }

    /// <summary>Unit::Think: a passenger sits at its car's position.</summary>
    private bool FollowVehicle()
    {
        if (Vehicle == null)
            return false;
        if (!IsInstanceValid(Vehicle) || Vehicle.IsDead)
        {
            LeaveVehicle();
            return false;
        }
        Position = Vehicle.Position;
        return true;
    }
}

/// <summary>Job sub_42FC30: walk to the player's Cadillac and get in.</summary>
public partial class BoardOrder : Node
{
    private const int MaxTries = 5;
    private readonly Unit _unit;
    private readonly Unit _car;
    private bool _walking;
    private bool _finished;
    private int _tries;

    public BoardOrder() { }

    public BoardOrder(Unit unit, Unit car)
    {
        _unit = unit;
        _car = car;
        Name = "BoardOrder";
    }

    public override void _Ready() => Walk();

    private void Walk()
    {
        _tries++;
        _unit.WalkTo(_car);
        _walking = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_walking || _finished || _unit.IsMoving)
            return;
        _walking = false;
        if (!IsInstanceValid(_car) || _car.IsDead || !_car.CanBoard(_unit))
        {
            Finish();
            return;
        }
        if (MapTileMath.GetDistanceExact(_unit, _car) <= CollectorJob.ReachDistance)
        {
            Finish();
            _car.Board(_unit);
            return;
        }
        if (_tries < MaxTries)
            Walk(); // the car moved
        else
            Finish();
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
