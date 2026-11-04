using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// A player's finances: the original HQ object (class 6, vtable off_488858, constructor sub_4305A0).
/// <list type="bullet">
/// <item><b>Cash</b> (+10h). Spending is allowed down to −200 000 (vt+70h); debt costs 10 % every 1008 minutes (event 102).</item>
/// <item><b>NetIncome</b> (+14h, "Доходы"): weekly income of the player's companies minus unit salaries, recomputed every
/// game hour (UpdateStats, vt+5Ch). VCOND 1 compares this value.</item>
/// <item>Money brought by collectors goes through <see cref="Deposit"/> (vt+68h): the bribe shares go to the authorities,
/// the rest ("savings") to cash.</item>
/// <item>Per business type: income, count, market share and the income multiplier used by companies (+10Ch).</item>
/// </list>
/// </summary>
public partial class Organisation : IScheduled
{
    public string SaveId => $"org:{Team.TeamColor.ToHtml()}";

    public void OnClockEvent(string kind, long arg)
    {
        var clock = GameClock.Instance;
        switch (kind)
        {
            case "hour": HourTick(clock); break;
            case "interest": InterestTick(clock); break;
            default: OnServiceEvent(kind, arg); break;
        }
    }

    /// <summary>Clock events of the services part (none yet; hook for timed services).</summary>
    partial void OnServiceEvent(string kind, long arg);

    /// <summary>Lowest allowed cash balance (sub_430951: cash − amount + 200 000 ≥ 0).</summary>
    public const long OverdraftLimit = 200_000;
    /// <summary>Debt interest: −cash / 10 every 1008 minutes (flt_4887A8 = 10, sub_430A58 event 102).</summary>
    public const double InterestDivisor = 10.0;
    public const int InterestPeriodMinutes = GameClock.MinutesPerWeek / 10;
    /// <summary>Market share is computed against at least this total income per type (unk_4C4B40 = 5 000 000).</summary>
    public const long MarketShareFloor = 5_000_000;
    /// <summary>Price of one favour point per authority (dword_488254).</summary>
    public const long FavourPointPrice = 5000;
    public const int TypeCount = 5;

    /// <summary>Market-share bonus: (share %, multiplier) steps of unk_4881F4 (sub_430BE8 takes the last step ≤ share).</summary>
    private static (int Share, float Factor)[] ShareCurve => GameRules.MarketShareSteps;

    /// <summary>Legalization level 0..3 of a business type → income multiplier (flt_485B7C).</summary>
    public static float[] LegalizationFactor => GameRules.LegalizationFactors;

    public Team Team { get; }
    /// <summary>Chosen primary training center (HQ +0CEh, set by company command 12 via vt+90h), or null.</summary>
    public Company PrimaryCenter { get; set; }
    public long Cash { get; private set; }
    /// <summary>Ledger for the scenario checks: starting cash, money kept from deposits, money spent, money bribed.</summary>
    public long StartCash { get; }
    public long TotalIn { get; private set; }
    public long TotalOut { get; private set; }
    public long TotalBribed { get; private set; }
    /// <summary>Weekly net income ("Доходы"), as of the last hourly update.</summary>
    public long NetIncome { get; private set; }
    /// <summary>Weekly income of the player's companies.</summary>
    public long GrossIncome { get; private set; }
    /// <summary>Weekly salaries of the player's units.</summary>
    public long Salaries { get; private set; }

    /// <summary>Gross income per business type (+0D0h).</summary>
    public long[] TypeIncome { get; } = new long[TypeCount];
    /// <summary>Number of companies per business type (+0E4h).</summary>
    public int[] TypeCountOwned { get; } = new int[TypeCount];
    /// <summary>Market share per type, 0..1 (+0F8h).</summary>
    public float[] MarketShare { get; } = new float[TypeCount];
    /// <summary>Income multiplier per business type (+10Ch); legal and troubleshooting are always 1.</summary>
    public float[] TypeFactor { get; } = { 1, 1, 1, 1, 1 };

    /// <summary>Income shares in percent (+90h): [0] savings (100 at start), [1..4] bribes to the authorities.</summary>
    public float[] Shares { get; } = { 100, 0, 0, 0, 0 };
    /// <summary>Money given to each authority (+0A4h).</summary>
    public long[] AuthorityMoney { get; } = new long[TypeCount];
    /// <summary>Favour points with each authority (+0B8h).</summary>
    public float[] Favour { get; } = new float[TypeCount];

    /// <summary>
    /// vt+B8h (sub_4310A0): an authority service (<see cref="AuthorityItems"/>) is available when the money given to its
    /// authority reaches the item's price and the favour points cover its cost.
    /// </summary>
    public bool CanUseItem(int item)
    {
        var it = AuthorityItems.Get(item);
        return it != null && it.Authority is > 0 and < TypeCount
               && AuthorityMoney[it.Authority] >= it.Price && Favour[it.Authority] >= it.Points;
    }

    /// <summary>vt+BCh (sub_4310F6): uses the service, paying its favour points. Returns whether it was available.</summary>
    public bool UseItem(int item)
    {
        if (!CanUseItem(item))
            return false;
        var it = AuthorityItems.Get(item);
        Favour[it.Authority] -= it.Points;
        return true;
    }

    /// <summary>Raised when a payment can't be made and the net income is negative (original: "You're out of funds.").</summary>
    public event Action<Organisation> OutOfFunds;

    public Organisation(Team team, long funds)
    {
        Team = team;
        Cash = StartCash = funds;
        NetIncome = funds; // the constructor stores the funds in +14h too
    }

    /// <summary>Starts the hourly statistics update and the debt-interest event.</summary>
    public void Start(GameClock clock)
    {
        clock.ScheduleIn(GameClock.MinutesPerHour, this, "hour");
        clock.ScheduleIn(InterestPeriodMinutes, this, "interest");
    }

    private void HourTick(GameClock clock)
    {
        if (Eliminated)
            return; // the HQ object is removed from the world (sub_43AB50), so its events stop
        UpdateStats();
        if (HasNothingLeft())
        {
            Eliminate();
            return;
        }
        InformerTick();
        // sub_430A58: while the FBI visits the local player, "FBI \"visits\" you ..." (20,2) is shown (sub_408C10).
        if (Team.IsCurrentPlayerColor && (FbiVisitActive || _fbiShown))
        {
            _fbiShown = FbiVisitActive;
            PanelInfo.StatusText = FbiVisitActive ? $"{OriginalTexts.Instance.Get(20, 2)} {(int)(FbiVisitProgress * 100)}%" : "";
        }
        clock.ScheduleIn(GameClock.MinutesPerHour, this, "hour");
    }

    private void InterestTick(GameClock clock)
    {
        if (Eliminated)
            return;
        if (Cash < 0)
            Spend((long)(-Cash / InterestDivisor));
        clock.ScheduleIn(InterestPeriodMinutes, this, "interest");
    }

    /// <summary>vt+70h: can the player pay <paramref name="amount"/> without going below the overdraft limit?</summary>
    public bool CanAfford(long amount) => Cash - amount + OverdraftLimit >= 0;

    /// <summary>
    /// vt+6Ch (sub_430860): pay <paramref name="amount"/>. Allowed when affordable, or (as in the original) while the net
    /// income is not negative. Otherwise nothing is paid, the local player gets "You're out of funds." (text 20,0,
    /// sound 24) and <see cref="OutOfFunds"/> is raised. Returns whether it was paid.
    /// </summary>
    public bool Spend(long amount)
    {
        if (Eliminated)
            return false;
        // sub_430860: pay when affordable, or the weekly net income isn't negative, or the player still has gangsters
        // (vt+D8h, sub_4308F0: any own unit but a Collector) — the debt simply grows.
        if (CanAfford(amount) || NetIncome >= 0 || HasGangsters())
        {
            Cash -= amount;
            TotalOut += amount;
            return true;
        }
        GD.Print($"Organisation: {Team.PlayerName} is out of funds and eliminated");
        if (Team.IsCurrentPlayerColor)
        {
            Messages.Show(OriginalTexts.Instance.Get(20, 0));
            Sound.Play("OutOfFunds.wav");
        }
        OutOfFunds?.Invoke(this);
        Eliminate();
        return false;
    }

    /// <summary>+1D2h (vt+9Ch / vt+0A0h): 1 from the constructor, 0 for a player set up without a base.</summary>
    public bool MayExtort { get; set; } = true;

    /// <summary>The player is out of the game (sub_43B677).</summary>
    public bool Eliminated { get; private set; }

    /// <summary>vt+D8h (sub_4308F0): does the player own a unit other than a Collector?</summary>
    public bool HasGangsters()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return false;
        foreach (var node in tree.GetNodesInGroup(Unit.GroupName))
            if (node is Unit { IsDead: false } unit && unit.OwnerTeam == Team && unit.UnitType != UnitCommon.UnitType.Collector)
                return true;
        return false;
    }

    /// <summary>
    /// User's rule (playtest 2026-11-02), not found in the EXE (its eliminations: HQ lost sub_4151C5 / sub_415F20,
    /// bankruptcy in Spend sub_430860): a player without an HQ (episodes that start without a base, e.g. 4) whose
    /// last unit is gone — none alive, jailed ones count as alive — and who trains nobody is defeated.
    /// </summary>
    private bool HasNothingLeft()
    {
        if (Team.HqCompany != null && GodotObject.IsInstanceValid(Team.HqCompany))
            return false;
        if (Training.Current(Team) != null)
            return false;
        if (Engine.GetMainLoop() is not SceneTree tree)
            return false;
        foreach (var node in tree.GetNodesInGroup(Unit.GroupName))
            if (node is Unit { IsDead: false } unit && GodotObject.IsInstanceValid(unit) && unit.OwnerTeam == Team)
                return false;
        return true;
    }

    /// <summary>
    /// sub_43B677: the local player gets "Game over. You have been defeated!" (19,1, a loss message), others are
    /// announced as "%s has been eliminated." (19,2). TODO (sub_43B711): removing an AI player's objects and the
    /// last-player-standing win ("You have won! The city belongs to you", 19,4).
    /// </summary>
    public void Eliminate()
    {
        if (Eliminated)
            return;
        Eliminated = true;
        GD.Print($"Organisation: {Team.PlayerName} eliminated");
        // Assets first: the defeat opens the end screen, which must find the player owning nothing.
        ReleaseAssets();
        if (Team.IsCurrentPlayerColor)
            CampaignManager.Instance?.OnPlayerDefeated(Team, OriginalTexts.Instance.Get(19, 1));
        else
            Messages.Show(PanelInfo.CFormat(OriginalTexts.Instance.Get(19, 2), Team.PlayerName));
        CheckLastPlayers();
    }

    /// <summary>
    /// What an eliminated player's units do: false (default) — they disappear; true — they stay as neutral units that
    /// fight everyone, each other included. A future game setting.
    /// </summary>
    public static bool RogueUnits { get; set; }

    /// <summary>
    /// The eliminated player's units disappear (or go rogue) and every company and business they own becomes an
    /// unowned legal company (user, from the original game).
    /// </summary>
    private void ReleaseAssets()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        foreach (var unit in tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => !u.IsDead && u.OwnerTeam == Team).ToList())
        {
            if (RogueUnits && unit.UnitType != UnitCommon.UnitType.Collector)
                unit.GoRogue();
            else
                unit.Vanish();
        }
        foreach (var company in tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(c => c.OwnerTeam == Team).ToList())
        {
            if (company.IsHq)
                company.RemoveHq();
            else
                company.ReleaseToNeutral();
        }
        Team.HqCompany = null;
        if (PrimaryCenter != null)
            PrimaryCenter = null;
    }

    /// <summary>
    /// Alliance victory (World +4EAh, a game-setup option copied from the loader's +7870h by sub_404B50). Its value
    /// for the campaign is not known yet, so it is off.
    /// </summary>
    public static bool AllianceVictory { get; set; }

    /// <summary>
    /// sub_43B711, after a player is removed: with fewer than two players left the local player, if still in, wins
    /// ("You have won ! The city belongs to your gang !", 19,3). With alliance victory on, players that are all
    /// allies (no pair with relation 1, enemy) also end the game ("… belongs to your alliance !", 19,4).
    /// </summary>
    private static void CheckLastPlayers()
    {
        var alive = Teams.TeamsInGame.Values.Where(t => t.Org is { Eliminated: false }).ToList();
        var human = alive.FirstOrDefault(t => t.IsCurrentPlayerColor);
        if (alive.Count < 2)
        {
            if (human != null)
                CampaignManager.Instance?.OnLastPlayerWon(human, OriginalTexts.Instance.Get(19, 3));
            return;
        }
        if (!AllianceVictory)
            return;
        var enemies = alive.Any(a => alive.Any(b => a != b && a.GetTargetRelation(b.TeamColor) == Teams.TeamRelation.Enemy));
        if (!enemies && human != null)
            CampaignManager.Instance?.OnLastPlayerWon(human, OriginalTexts.Instance.Get(19, 4));
    }

    /// <summary>
    /// vt+68h (sub_430790): income brought in by collectors. When the net income is positive, the profit part of the
    /// amount (amount × net / gross) is split by <see cref="Shares"/>: bribes go to the authorities (money and favour
    /// points at 5000 per point), the rest stays. Everything not bribed goes to cash. Returns what went to cash.
    /// </summary>
    public long Deposit(long amount)
    {
        var kept = amount;
        if (NetIncome > 0 && GrossIncome > 0)
        {
            var profit = (long)((double)NetIncome / GrossIncome * amount);
            var remaining = profit;
            for (var i = 1; i < TypeCount; i++)
            {
                var bribe = (long)(Shares[i] * profit / 100.0);
                AuthorityMoney[i] += bribe;
                Favour[i] += (float)(Shares[i] * profit / 100.0 / FavourPointPrice);
                remaining -= bribe;
            }
            kept = amount - profit + remaining;
        }
        Cash += kept;
        TotalIn += kept;
        TotalBribed += amount - kept;
        return kept;
    }

    /// <summary>
    /// HQ::UpdateStats (vt+5Ch, sub_430C30), every game hour: salaries, company incomes per type, net income,
    /// market shares and the per-type income multipliers.
    /// </summary>
    public void UpdateStats()
    {
        Array.Clear(TypeIncome);
        Array.Clear(TypeCountOwned);
        long net = 0, salaries = 0, gross = 0;

        var tree = (Engine.GetMainLoop() as SceneTree);
        if (tree != null)
        {
            foreach (var node in tree.GetNodesInGroup(Unit.GroupName))
                if (node is Unit { IsDead: false } unit && unit.OwnerTeam == Team)
                {
                    salaries += unit.Salary;
                    net -= unit.Salary;
                }
            foreach (var node in tree.GetNodesInGroup(Company.GroupName))
            {
                if (node is not Company company || company.OwnerTeam != Team || !company.IsEconomic)
                    continue;
                // sub_430C30: only companies with income enabled (+0CCh), an active block and no job running (vt+0A0h).
                if (!company.CachedIncomeEnabled || company.Block is { IsActive: false } || company.Busy)
                    continue;
                var income = company.Income();
                net += income;
                gross += income;
                var type = (int)company.BusinessType;
                if (type is >= 0 and < TypeCount)
                {
                    TypeIncome[type] += company.GrossIncome();
                    TypeCountOwned[type]++;
                }
            }
        }
        if (Cash < 0)
            net += Cash; // the original adds a negative balance to the net income

        NetIncome = net;
        GrossIncome = gross;
        Salaries = salaries;
        UpdateMarketShares();
    }

    private void UpdateMarketShares()
    {
        var totals = new long[TypeCount];
        foreach (var org in Teams.TeamsInGame.Values.Select(t => t.Org).Where(o => o != null))
            for (var i = 0; i < TypeCount; i++)
                totals[i] += org.TypeIncome[i];

        for (var i = 0; i < TypeCount; i++)
        {
            MarketShare[i] = ShareOf(TypeIncome[i], totals[i]);
            TypeFactor[i] = MarketFactor((int)(MarketShare[i] * 100));
        }
        // Legal and troubleshooting don't get the market bonus (+10Ch and +11Ch are reset to 1).
        TypeFactor[(int)OriginalBusinessTable.BusinessType.Legal] = 1f;
        TypeFactor[(int)OriginalBusinessTable.BusinessType.Troubleshooting] = 1f;
        for (var i = 1; i < TypeCount; i++)
        {
            // sub_430C30: an evil rumour (vt+FCh) ×0.7, an investigation (vt+100h) ×0.85 (flt_4887AC / flt_4887B0).
            if (RumourActive)
                TypeFactor[i] *= RumourFactor;
            if (InvestigationActive)
                TypeFactor[i] *= InvestigationFactor;
            var level = World.LegalizationLevel(i);
            TypeFactor[i] *= LegalizationFactor[Math.Clamp(level, 0, LegalizationFactor.Length - 1)];
        }
    }

    /// <summary>
    /// Market share (+0F8h, sub_430C30): the player's weekly income of a type over everyone's, counted against at least
    /// <see cref="MarketShareFloor"/>. Pricier businesses earn more and so weigh more; while a rival earns anything
    /// from the type, nobody reaches 1.
    /// </summary>
    public static float ShareOf(long own, long total) => (float)own / Math.Max(total, MarketShareFloor);

    /// <summary>sub_430BE8: income multiplier for a market share in percent.</summary>
    public static float MarketFactor(int sharePercent) => StepFactor(ShareCurve, sharePercent);

    /// <summary>Unit-test hook: the hourly statistics without a scene.</summary>
    internal void SetIncomeForTest(long net, long gross)
    {
        NetIncome = net;
        GrossIncome = gross;
    }

    private static float StepFactor((int Key, float Factor)[] table, int x)
    {
        var value = table[0].Factor;
        foreach (var (key, factor) in table)
        {
            if (key > x)
                break;
            value = factor;
        }
        return value;
    }

    public override string ToString() =>
        $"{Team.PlayerName}: cash {Cash}, net income {NetIncome} (companies {GrossIncome}, salaries {Salaries})";
}

/// <summary>World-wide state of the original World object that isn't per player (legalization per business type, World+4F2h).</summary>
public static class World
{
    private static readonly int[] Legalization = new int[Organisation.TypeCount];

    public static int LegalizationLevel(int businessType) =>
        businessType >= 0 && businessType < Legalization.Length ? Legalization[businessType] : 0;

    public static void SetLegalization(int businessType, int level)
    {
        if (businessType >= 0 && businessType < Legalization.Length)
            Legalization[businessType] = level;
    }

    public static void Reset() => Array.Clear(Legalization);
}
