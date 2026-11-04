using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Episode statistics for the end screen chart (object made by sub_460B36, sampled by sub_460BE4). Players: the local
/// one first, then every other player (HQ objects). For each, three series of 31 slots: weekly net income (HQ vt+64h,
/// +0BA4h), cash (vt+60h, +1F4h) and gangsters — own units other than collectors (+1554h). Every 30 s of real time
/// (30 game hours at normal speed; sampled on game time here so the chart does not depend on the game speed) the current
/// slot takes the running average of its samples; a slot holds <see cref="Period"/> samples. When all 31 slots are
/// full, neighbouring pairs are averaged into 15, the last slot moves to slot 15 and the period doubles.
/// </summary>
public partial class Statistics : IScheduled
{
    public string SaveId => "stats";

    public void OnClockEvent(string kind, long arg)
    {
        if (kind == "sample")
            Tick(GameClock.Instance);
    }

    public const int Slots = 31;
    public const int SampleMinutes = 30 * GameClock.MinutesPerHour;
    public const int SeriesCount = 3;
    public const int NetIncome = 0, Cash = 1, Gangsters = 2;

    public static Statistics Current { get; private set; }

    public List<Team> Players { get; } = new();
    /// <summary>[series][player][slot].</summary>
    private readonly List<long[]>[] _series = { new(), new(), new() };
    /// <summary>+1F08h: complete slots.</summary>
    public int Count { get; private set; }
    /// <summary>+1F0Ch: samples per slot.</summary>
    public int Period { get; private set; } = 1;
    /// <summary>+1F10h: samples already in the current slot.</summary>
    private int _counter;
    /// <summary>The episode's starting funds (world +4FEh): where the cash lines start.</summary>
    public long StartFunds { get; }

    public Statistics(IEnumerable<Team> players, long startFunds)
    {
        StartFunds = startFunds;
        foreach (var team in players)
        {
            Players.Add(team);
            foreach (var s in _series)
                s.Add(new long[Slots]);
        }
    }

    /// <summary>Starts the episode's statistics: the local player first, then the others.</summary>
    public static void Start(GameClock clock, long startFunds)
    {
        var teams = Teams.TeamsInGame.Values.Where(t => t.Org != null).ToList();
        var local = teams.FirstOrDefault(t => t.IsCurrentPlayerColor);
        var ordered = local == null ? teams : new[] { local }.Concat(teams.Where(t => t != local)).ToList();
        var stats = new Statistics(ordered, startFunds);
        Current = stats;
        if (clock != null)
            clock.ScheduleIn(SampleMinutes, stats, "sample");
    }

    private void Tick(GameClock clock)
    {
        if (Current != this)
            return;
        Sample(Players.Select(Read).ToList());
        clock.ScheduleIn(SampleMinutes, this, "sample");
    }

    private static (long, long, long) Read(Team team)
    {
        var org = team.Org;
        long gangsters = 0;
        if (Engine.GetMainLoop() is SceneTree tree)
            gangsters = tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
                .Count(u => !u.IsDead && u.OwnerTeam == team && u.UnitType != UnitCommon.UnitType.Collector);
        return (org?.NetIncome ?? 0, org?.Cash ?? 0, gangsters);
    }

    /// <summary>One sample of (net income, cash, gangsters) per player, in <see cref="Players"/> order.</summary>
    public void Sample(IReadOnlyList<(long NetIncome, long Cash, long Gangsters)> values)
    {
        if (Count > Slots - 1)
            Compress();
        for (var p = 0; p < Players.Count && p < values.Count; p++)
        {
            var (net, cash, people) = values[p];
            Put(NetIncome, p, net);
            Put(Cash, p, cash);
            Put(Gangsters, p, people);
        }
        if (++_counter >= Period)
        {
            Count++;
            _counter = 0;
        }
    }

    private void Put(int series, int player, long value)
    {
        var slots = _series[series][player];
        // Running average of the slot's samples (integer division as idiv).
        slots[Count] = _counter == 0 ? value : (_counter * slots[Count] + value) / (_counter + 1);
    }

    private void Compress()
    {
        foreach (var series in _series)
            foreach (var slots in series)
            {
                for (var k = 0; k < 15; k++)
                    slots[k] = (slots[2 * k] + slots[2 * k + 1]) / 2;
                slots[15] = slots[30];
                for (var k = 16; k < Slots; k++)
                    slots[k] = 0;
            }
        _counter = Period; // slot 15 already holds a whole old period
        Period *= 2;
        Count = 15;
    }

    public long Value(int series, int player, int slot) => _series[series][player][slot];
}
