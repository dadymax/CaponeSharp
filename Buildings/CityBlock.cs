using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// One placed city block (a .map BLOCKS line): the original block object (vtable off_47CF28, created in
/// fnBuildingPlacementOnMapByNumber). It knows its district, its companies and, per player that owns companies in it,
/// the collector that serves them.
/// <list type="bullet">
/// <item>District (vt+16Ch) = BlockTable[block].Category: 0 slum, 1 middle class, 2 center. Legal companies earn
/// ×1.0 / ×1.25 / ×1.875 by district (sub_420E70).</item>
/// <item>OnCompanyAcquired (vt+178h, sub_41FC15): the first time a player owns a company here, an owner entry is
/// created and a collector is sent from the player's HQ at once.</item>
/// <item>DispatchCollectors (vt+2Ch, sub_41FF60), weekly: a new collector for every owner whose collector is gone,
/// one week after the old one was lost.</item>
/// <item>Collector killed (vt+180h): income of that owner's companies here stops until the next collector's first
/// pickup (vt+184h). Job finished without companies (vt+17Ch): the owner entry is removed.</item>
/// </list>
/// </summary>
public partial class CityBlock : IScheduled
{
    public string SaveId => $"block:{Number}";

    public void OnClockEvent(string kind, long arg)
    {
        if (kind == "week")
            WeekTick(GameClock.Instance);
    }

    public class OwnerEntry
    {
        public Team Team;
        public Unit Collector;
        /// <summary>Entry +0Ch: companies of this owner earn only while this is set (vt+188h).</summary>
        public bool IncomeEnabled = true;
        /// <summary>Entry +10h: when the last collector was lost (game minute).</summary>
        public long LostAt = long.MinValue / 2;
    }

    public static readonly float[] DistrictIncomeFactor = { 1.0f, 1.25f, 1.875f };
    public static IReadOnlyList<CityBlock> All => Blocks;
    private static readonly List<CityBlock> Blocks = new();

    public int Number { get; }
    /// <summary>Block layout number (index into OriginalBlocks).</summary>
    public int Layout { get; }
    /// <summary>0 slum, 1 middle class, 2 center.</summary>
    public int District { get; }
    /// <summary>Map cells of the block in the collector's scan order (row by row, sub_429146).</summary>
    public List<Vector2I> Cells { get; } = new();
    public List<Company> Companies { get; } = new();
    private readonly Dictionary<Vector2I, Company> _companyAt = new();

    public void AddCompany(Vector2I cell, Company company)
    {
        Companies.Add(company);
        _companyAt[cell] = company;
    }

    public Company CompanyAtCell(Vector2I cell) =>
        _companyAt.TryGetValue(cell, out var company) && GodotObject.IsInstanceValid(company) ? company : null;
    public Dictionary<Team, OwnerEntry> Owners { get; } = new();
    /// <summary>Block +60h: no income before this minute (brownouts, raids).</summary>
    public long SuspendedUntil { get; set; }

    public CityBlock(int number, int layout, int district)
    {
        Number = number;
        Layout = layout;
        District = district;
        Blocks.Add(this);
    }

    public static void ClearAll() => Blocks.Clear();

    public bool IsActive => (GameClock.Instance?.Minute ?? 0) >= SuspendedUntil;

    /// <summary>vt+188h(company): 1 unless the company's owner has an entry with income disabled.</summary>
    public bool IncomeEnabledFor(Team owner) =>
        owner == null || !Owners.TryGetValue(owner, out var entry) || entry.IncomeEnabled;

    public void Start(GameClock clock) => clock.ScheduleIn(GameClock.MinutesPerWeek, this, "week");

    private void WeekTick(GameClock clock)
    {
        DispatchCollectors();
        clock.ScheduleIn(GameClock.MinutesPerWeek, this, "week");
    }

    /// <summary>Weekly: replace lost collectors (one week after the loss).</summary>
    public void DispatchCollectors()
    {
        var now = GameClock.Instance?.Minute ?? 0;
        foreach (var entry in Owners.Values.ToList())
        {
            if (entry.Team.HqCompany == null || !GodotObject.IsInstanceValid(entry.Team.HqCompany))
                continue;
            if (entry.Collector != null && GodotObject.IsInstanceValid(entry.Collector) && !entry.Collector.IsDead)
                continue;
            if (now < entry.LostAt + GameClock.MinutesPerWeek)
                continue;
            SendCollector(entry);
        }
    }

    /// <summary>vt+178h: a company of this block got a new owner.</summary>
    public void OnCompanyAcquired(Company company)
    {
        var owner = company.OwnerTeam;
        if (owner == null || owner.HqCompany == null || Owners.ContainsKey(owner))
            return;
        var entry = new OwnerEntry { Team = owner };
        Owners[owner] = entry;
        SendCollector(entry);
    }

    private void SendCollector(OwnerEntry entry)
    {
        var collector = CampaignManager.Instance?.SpawnCollector(entry.Team);
        if (collector == null)
            return;
        entry.Collector = collector;
        var job = new CollectorJob(this, collector);
        collector.SetOrder(job);
        job.Start();
        GD.Print($"CityBlock {Number}: collector sent for {entry.Team.PlayerName}");
    }

    /// <summary>vt+180h: the collector died → this owner's companies here stop earning.</summary>
    public void OnCollectorDied(Unit collector)
    {
        foreach (var entry in Owners.Values)
            if (entry.Collector == collector)
            {
                entry.IncomeEnabled = false;
                entry.LostAt = GameClock.Instance?.Minute ?? 0;
                entry.Collector = null;
            }
    }

    /// <summary>vt+184h: the collector's first pickup → income enabled again.</summary>
    public void OnCollectorPickup(Unit collector)
    {
        foreach (var entry in Owners.Values)
            if (entry.Collector == collector)
                entry.IncomeEnabled = true;
    }

    /// <summary>vt+17Ch: the owner has nothing left to collect here.</summary>
    public void RemoveOwner(Team owner) => Owners.Remove(owner);

    /// <summary>The owner's companies in scan order that the collector visits (catalogue income ≠ 0).</summary>
    public IEnumerable<Company> CollectableCompanies(Team owner) =>
        Companies.Where(c => GodotObject.IsInstanceValid(c) && c.OwnerTeam == owner && c.IsEconomic && c.Entry.Income != 0);
}
