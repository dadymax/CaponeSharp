using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Sends police cars after gangsters. Original: a police order object (sub_4460C5, vtable off_48DA88, class 7) is
/// created by a business owner who calls the cops during extortion (sub_422FB4) and by informers (sub_42C503).
/// </summary>
public partial class Police : Node
{
    public static Police Instance { get; private set; }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>A business owner called the cops on <paramref name="gangster"/> (sub_422FB4 → sub_4460C5(world, unit, 0)).</summary>
    public void Respond(Company company, Unit gangster) => Dispatch(gangster, lawyersCanHelp: true);

    /// <summary>
    /// Sends a car from the police station nearest to the gangster (sub_446123). <paramref name="lawyersCanHelp"/>
    /// is the order's +68h flag: 0 lets the owner's lawyers try to get the gangster released.
    /// </summary>
    public void Dispatch(Unit gangster, bool lawyersCanHelp)
    {
        if (gangster == null || gangster.IsDead || gangster.IsArrested)
            return;
        var station = NearestStation(gangster);
        if (station == null)
        {
            GD.Print("Police: the map has no police station");
            return;
        }
        var car = CampaignManager.Instance?.SpawnPoliceCar(station);
        if (car == null)
            return;
        car.SetOrder(new PoliceJob(car, gangster, station, lawyersCanHelp));
        GD.Print($"Police: car sent from {station.Cell} after {gangster.DisplayName} at {gangster.Cell}");
    }

    public Company NearestStation(Node2D to) =>
        GetTree().GetNodesInGroup(Company.GroupName).OfType<Company>()
            .Where(c => c.IsPoliceStation)
            .OrderBy(c => MapTileMath.GetDistanceExact(to, c))
            .FirstOrDefault();
}

/// <summary>
/// One police car's job (start sub_4461A4, step sub_4462B4). States as in the original (+70h):
/// <list type="bullet">
/// <item>10: drive (siren on) to the cell where the gangster was when the cops were called. On arrival, a gangster within
/// 5 cells is chased; otherwise the car goes back.</item>
/// <item>11: chase for up to 12 game hours; within 1 cell the gangster is arrested (sounds 27/28 for the local
/// player) and taken along.</item>
/// <item>12: back at the station the gangster is jailed (unit vt+1A0h), unless the owner's lawyers get him out
/// (items 0x29 / 0x27 / 0x24: 70 / 50 / 30 %; text 28,1 "Cops release the gangster."). The car disappears.</item>
/// </list>
/// </summary>
public partial class PoliceJob : Node
{
    private enum State { Driving = 10, Chasing = 11, Returning = 12 }

    /// <summary>flt_48D924: on arrival the gangster must be this close to be chased.</summary>
    public const float ChaseDistance = 5f;
    /// <summary>The original arrests within distance 1; diagonal neighbours count here as well.</summary>
    public const float ArrestDistance = 1.5f;
    /// <summary>The chase is given up 12 game hours after the car arrived.</summary>
    public const int ChaseMinutes = 12 * GameClock.MinutesPerHour;

    /// <summary>HQ items that can get an arrested gangster out, best first: (item, release chance %).</summary>
    private static readonly (int Item, int Chance)[] LawyerItems = { (0x29, 70), (0x27, 50), (0x24, 30) };

    private readonly Unit _car;
    private readonly Unit _gangster;
    private readonly Company _station;
    private readonly bool _lawyersCanHelp;
    private State _state = State.Driving;
    private bool _walking;
    private bool _arrested;
    private bool _finished;
    private long _chaseStarted;
    private Vector2I _chaseCell;

    /// <summary>Needed by Godot; use the full constructor in code.</summary>
    public PoliceJob() { }

    public PoliceJob(Unit car, Unit gangster, Company station, bool lawyersCanHelp)
    {
        _car = car;
        _gangster = gangster;
        _station = station;
        _lawyersCanHelp = lawyersCanHelp;
        Name = "PoliceJob";
    }

    public override void _Ready()
    {
        Sound.Play("Police_s.wav");
        _car.WalkToCell(_gangster.Cell);
        _walking = true;
    }

    private bool GangsterAvailable => IsInstanceValid(_gangster) && !_gangster.IsDead && !_gangster.IsArrested;

    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
            return;
        if (_state == State.Chasing)
        {
            Chase();
            return;
        }
        if (!_walking || _car.IsMoving)
            return;
        _walking = false;
        Step();
    }

    private void Chase()
    {
        if (!GangsterAvailable)
        {
            ReturnToStation();
            return;
        }
        if (MapTileMath.GetDistanceExact(_car, _gangster) <= ArrestDistance)
        {
            Arrest();
            ReturnToStation();
            return;
        }
        if (GameClock.Instance.Minute > _chaseStarted + ChaseMinutes)
        {
            ReturnToStation();
            return;
        }
        // Follow the gangster: re-plan when he has moved on or the car has stopped.
        if (_gangster.Cell != _chaseCell || !_car.IsMoving)
        {
            _chaseCell = _gangster.Cell;
            _car.WalkToCell(_chaseCell);
        }
    }

    /// <summary>sub_4462B4 for the driving and returning states.</summary>
    private void Step()
    {
        switch (_state)
        {
            case State.Driving:
                if (GangsterAvailable && MapTileMath.GetDistanceExact(_car, _gangster) <= ChaseDistance)
                {
                    _state = State.Chasing;
                    _chaseStarted = GameClock.Instance.Minute;
                    _chaseCell = _gangster.Cell;
                    _car.WalkToCell(_chaseCell);
                }
                else
                    ReturnToStation();
                break;
            case State.Returning:
                AtStation();
                break;
        }
    }

    private void Arrest()
    {
        if (_gangster.OwnerTeam?.IsCurrentPlayerColor == true)
            Sound.Play(GD.Randi() % 2 == 0 ? "PoliceArrest1.wav" : "PoliceArrest2.wav");
        _gangster.Arrest();
        _arrested = true;
    }

    private void ReturnToStation()
    {
        _state = State.Returning;
        if (IsInstanceValid(_station))
            _car.DriveTo(_station);
        _walking = true;
    }

    private void AtStation()
    {
        if (_arrested && IsInstanceValid(_gangster))
        {
            var released = false;
            var org = _gangster.OwnerTeam?.Org;
            if (_lawyersCanHelp && org != null)
                foreach (var (item, chance) in LawyerItems)
                    if (org.UseItem(item))
                    {
                        released = GD.RandRange(0, 99) <= chance;
                        break;
                    }
            if (released)
            {
                _gangster.Release(_car.Cell);
                if (_gangster.OwnerTeam?.IsCurrentPlayerColor == true)
                    Messages.Show(OriginalTexts.Instance.Get(28, 1));
            }
            else
                _gangster.Jail();
        }
        _finished = true;
        _car.Leave();
    }
}
