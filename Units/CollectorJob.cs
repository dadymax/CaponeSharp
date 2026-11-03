using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// A collector's autopilot for one city block: the original collector job (vtable off_483DC0, constructor sub_428CF0,
/// step sub_428D62). The step runs whenever the collector arrives (or can't get any further):
/// <list type="bullet">
/// <item>The collector goes inside the company or HQ while it waits there (hidden, user from the original game).</item>
/// <item>At the owner's company: take its uncollected money (the first pickup re-enables the block's income for this
/// owner), wait an hour, walk to the owner's next company in the block, or home when there is none left.</item>
/// <item>At the HQ or a cash-out business (catalogue +28h = 3): deposit the money, heal, and start the next round
/// a quarter week (2520 min) after the previous one began, at the earliest an hour from now.</item>
/// <item>Owner has no companies left in the block: the job ends, the collector leaves and the block forgets the owner.</item>
/// <item>Collector killed: the block stops this owner's income until a new collector's first pickup.</item>
/// </list>
/// Companies are visited in the block's row order (sub_429146); only those whose catalogue income is not 0.
/// </summary>
public partial class CollectorJob : Node
{
    /// <summary>Wait at every stop (sub_416790).</summary>
    public const int WaitMinutes = GameClock.MinutesPerHour;
    /// <summary>Rounds start at least this far apart (week / 4).</summary>
    public const int RoundMinutes = GameClock.MinutesPerWeek / 4;
    /// <summary>The original needs distance ≤ 1; diagonal neighbours count here as well.</summary>
    public const float ReachDistance = 1.5f;

    private readonly CityBlock _block;
    private readonly Unit _collector;
    private Node2D _target;
    /// <summary>+64h: next cell of the block to look at.</summary>
    private int _scanIndex;
    /// <summary>+68h: when the current round started.</summary>
    private long _roundStart;
    /// <summary>+6Ch: the block was told about the first pickup.</summary>
    private bool _pickedUp;
    private bool _walking;
    private bool _finished;

    /// <summary>Needed by Godot; use the (block, collector) constructor in code.</summary>
    public CollectorJob() { }

    public CollectorJob(CityBlock block, Unit collector)
    {
        _block = block;
        _collector = collector;
        Name = "CollectorJob";
    }

    public CityBlock Block => _block;
    public Node2D CurrentTarget => _target;
    private static GameClock Clock => GameClock.Instance;

    public void Start()
    {
        _roundStart = Clock.Minute;
        _collector.Died += OnCollectorDied;
        Step();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_walking || _finished)
            return;
        if (_collector.IsMoving)
            return;
        _walking = false;
        Step();
    }

    private void OnCollectorDied(Unit collector)
    {
        if (_finished)
            return;
        _block.OnCollectorDied(collector);
        Finish();
    }

    /// <summary>sub_428D62.</summary>
    private void Step()
    {
        if (_finished)
            return;
        if (!IsInstanceValid(_collector) || _collector.IsDead || _collector.IsArrested)
        {
            _block.OnCollectorDied(_collector);
            Finish();
            return;
        }

        var now = Clock.Minute;
        var owner = _collector.OwnerTeam;
        if (_target != null && IsInstanceValid(_target) && Reached(_target))
        {
            if (IsCashPoint(_target))
            {
                Deliver(owner);
                _collector.EnterBuilding(); // stays inside until the next round
                _scanIndex = 0;
                _target = NextCompany(owner);
                if (_target == null)
                {
                    EndService(owner);
                    return;
                }
                var next = now >= _roundStart + RoundMinutes ? now + WaitMinutes : _roundStart + RoundMinutes;
                _roundStart = next;
                WalkAt(next);
                return;
            }
            if (_target is Company company && company.OwnerTeam == owner)
            {
                _collector.CarriedMoney += company.TakeUncollected();
                _collector.EnterBuilding(); // inside for the hour's wait
                if (!_pickedUp)
                {
                    _pickedUp = true;
                    _block.OnCollectorPickup(_collector);
                }
                _target = NextCompany(owner) ?? NearestCashPoint(owner);
                WalkAt(now + WaitMinutes);
                return;
            }
        }

        // No target yet, the target was not reached, or it changed hands: the next company, wrapping around once.
        _target = NextCompany(owner);
        if (_target == null)
        {
            _scanIndex = 0;
            _target = NextCompany(owner);
            if (_target == null)
            {
                EndService(owner);
                return;
            }
        }
        WalkAt(now + WaitMinutes);
    }

    private void WalkAt(long minute) => Clock.ScheduleAt(minute, () =>
    {
        if (_finished || !IsInstanceValid(this))
            return;
        if (!_collector.IsDead)
            _collector.ExitBuilding();
        if (_target == null || !IsInstanceValid(_target) || _collector.IsDead)
        {
            Step();
            return;
        }
        _collector.WalkTo(_target);
        _walking = true;
    });

    private bool Reached(Node2D target) => MapTileMath.GetDistanceExact(_collector, target) <= ReachDistance;

    /// <summary>The HQ (class 9) or a cash-out business (catalogue +28h = 3).</summary>
    private static bool IsCashPoint(Node2D target) =>
        target is Company { IsHq: true } || target is Company { Entry.Value28: 3 };

    /// <summary>Raised when a collector hands money in (owner, amount carried).</summary>
    public static event System.Action<Team, long> Delivered;

    private void Deliver(Team owner)
    {
        var amount = _collector.CarriedMoney;
        _collector.CarriedMoney = 0;
        if (amount > 0 && owner?.Org != null)
        {
            var kept = owner.Org.Deposit(amount);
            GD.Print($"Collector of {owner.PlayerName} delivered {amount} from block {_block.Number} ({kept} to cash): {owner.Org}");
            Delivered?.Invoke(owner, amount);
        }
        if (_collector.CanBeAttacked is { } health)
            health.HP = health.MaxHP;
    }

    /// <summary>sub_429146: the owner's next company in the block (catalogue income ≠ 0), continuing the scan.</summary>
    private Company NextCompany(Team owner)
    {
        var cells = _block.Cells;
        while (_scanIndex < cells.Count)
        {
            var company = _block.CompanyAtCell(cells[_scanIndex++]);
            if (company != null && company.OwnerTeam == owner && company.IsEconomic && company.Entry.Income != 0)
                return company;
        }
        return null;
    }

    /// <summary>sub_429221: the owner's nearest HQ or cash-out business.</summary>
    private Company NearestCashPoint(Team owner) =>
        GetTree().GetNodesInGroup(Company.GroupName).OfType<Company>()
            .Where(c => c.OwnerTeam == owner && IsCashPoint(c))
            .OrderBy(c => MapTileMath.GetDistanceExact(_collector, c))
            .FirstOrDefault();

    /// <summary>Nothing left to collect: the block forgets the owner and the collector leaves the map.</summary>
    private void EndService(Team owner)
    {
        GD.Print($"CityBlock {_block.Number}: collector of {owner?.PlayerName} has nothing to collect and leaves");
        if (owner != null)
            _block.RemoveOwner(owner);
        Finish();
        _collector.Died -= OnCollectorDied;
        _collector.Leave();
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        _walking = false;
        if (IsInstanceValid(_collector))
            _collector.OrderFinished(this);
    }
}
