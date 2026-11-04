using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Computer opponent: the AI HQ object (sub_461510, vtable off_4911BC, 0x244 bytes), made for every PLAYER whose kind
/// is not 0. Three game events repeat every 3 hours (event handler sub_462590), first fired 1, 2 and 3 hours in
/// (sub_462480): 44Dh unit tasking (sub_461CA8), 44Eh recruiting and business conversion (sub_461F24), 44Fh strategy
/// (sub_4620D7). Event 450h, 3 hours in, sets the bribe shares from the parameters (sub_4623E4, share job sub_4287F0).
/// The personality is a 13-value parameter table (+210h, setter vt+15Ch) filled by the constructor and by the PLAYER
/// kind (vt+160h, sub_4626B5); see <see cref="Parameters"/>.
/// </summary>
public class ComputerPlayer : IScheduled
{
    public string SaveId => $"ai:{Team.TeamColor.ToHtml()}";

    public void OnClockEvent(string kind, long arg)
    {
        var clock = GameClock.Instance;
        switch (kind)
        {
            case "units": Repeat(clock, TaskUnits, kind); break;
            case "recruit": Repeat(clock, RecruitAndConvert, kind); break;
            case "strategy": Repeat(clock, Strategy, kind); break;
            case "shares": SetShares(); break;
            case "seen": RepeatHourly(clock); break;
            default: GD.PushError($"ComputerPlayer: unknown clock event {kind}"); break;
        }
    }

    public const int CycleMinutes = 3 * GameClock.MinutesPerHour;
    /// <summary>flt_491180: a fighter farther than this from its own training center walks home.</summary>
    public const float HomeRadius = 10f;
    /// <summary>flt_4911A4: an enemy unit this close to the AI's training center is an intruder.</summary>
    public const float IntruderRadius = 30f;
    /// <summary>flt_4911A8: parameter 3 is a percentage of the companies' income.</summary>
    public const float PercentDivisor = 100f;
    /// <summary>sub_461D40: a row may cost up to cash + 100 000.</summary>
    public const long ConversionCashMargin = 100_000;
    /// <summary>sub_4620D7: below −60 000 cash an intruder no longer justifies recruiting (0FFFF15A0h).</summary>
    public const long IntruderCashFloor = -60_000;
    /// <summary>sub_4620D7: a weekly net income above this lets the AI recruit when it out-earns the human.</summary>
    public const long RichIncome = 2_000_000;
    /// <summary>sub_461781: rings searched around a unit for a company to extort.</summary>
    public const int ExtortSearchRings = 500;

    /// <summary>word_491188: fighting strength of each unit type (sub_461F88).</summary>
    public static readonly int[] StrengthWeight = { 1, 4, 16, 6, 5, 14, 0, 0, 8, 10, 0, 0, 0 };

    private static readonly Random Rng = new();

    public Team Team { get; }
    private Organisation Org => Team.Org;

    /// <summary>
    /// +210h: [0] convert legal companies, [1] minimum Thugs, [2] recruit when the human is stronger, [3] salary budget
    /// (% of company income), [4] weekly net income below which fighters go extorting, [5..9] bribe shares in percent
    /// (savings, army, FBI, politician, police), [10] attack the human HQ when strong, [11] cash reserve (may go below
    /// zero), [12] unit type left alone by the tasking (the Godfather).
    /// </summary>
    public int[] Parameters { get; } = { 1, 5, 1, 25, 400_000, 60, 30, 0, 0, 10, 0, 0, 12 };

    // Decisions of the last strategy cycle (+1D4h…+1F4h).
    private bool _recruitChosen, _recruitThug, _convert, _weaker, _attackIntruder, _attackTarget, _attackHuman;
    private Unit _intruder;
    private Team _target;
    /// <summary>+1F8h: business type 1–3 (bootlegging, entertainment, gambling) this AI converts to, chosen at random.</summary>
    private readonly OriginalBusinessTable.BusinessType _style;

    public ComputerPlayer(Team team)
    {
        Team = team;
        _style = (OriginalBusinessTable.BusinessType)(Rng.Next(3) + 1);
        ApplyKind(team.Kind);
    }

    /// <summary>vt+160h (sub_4626B5): the PLAYER kind's personality. Kind 4 keeps the constructor defaults.</summary>
    private void ApplyKind(int kind)
    {
        var p = Parameters;
        switch (kind)
        {
            case 1: // passive: saves everything, no conversions, no extra recruits
                Set(5, 100, 0, 0, 0, 0);
                p[0] = 0; p[1] = 2; p[2] = 0; p[3] = 10;
                break;
            case 2:
                Set(5, 70, 30, 0, 0, 0);
                p[0] = 0; p[1] = 2; p[2] = 0; p[3] = 20;
                break;
            case 3:
                Set(5, 70, 30, 0, 0, 0);
                p[0] = 1; p[1] = 5; p[2] = 1; p[3] = 25;
                break;
            case 5:
                p[11] = -20_000;
                break;
            case 6: // aggressive: like 3, plus attacks on the human HQ
                Set(5, 70, 30, 0, 0, 0);
                p[0] = 1; p[1] = 5; p[2] = 1; p[3] = 25; p[10] = 1; p[11] = -20_000;
                break;
        }

        void Set(int from, params int[] values)
        {
            for (var i = 0; i < values.Length; i++)
                p[from + i] = values[i];
        }
    }

    /// <summary>vt+24h (sub_462480): schedules the three cycles and the share setup.</summary>
    public void Start(GameClock clock)
    {
        clock.ScheduleIn(GameClock.MinutesPerHour, this, "units");
        clock.ScheduleIn(2 * GameClock.MinutesPerHour, this, "recruit");
        clock.ScheduleIn(3 * GameClock.MinutesPerHour, this, "strategy");
        clock.ScheduleIn(3 * GameClock.MinutesPerHour, this, "shares");
        clock.ScheduleIn(GameClock.MinutesPerHour, this, "seen");
    }

    private void Repeat(GameClock clock, Action cycle, string kind)
    {
        if (Org == null || Org.Eliminated || !Teams.TeamsInGame.ContainsValue(Team))
            return;
        cycle();
        clock.ScheduleIn(CycleMinutes, this, kind);
    }

    private void RepeatHourly(GameClock clock)
    {
        if (Org == null || Org.Eliminated || !Teams.TeamsInGame.ContainsValue(Team))
            return;
        UpdateSeen();
        clock.ScheduleIn(GameClock.MinutesPerHour, this, "seen");
    }

    private static SceneTree Tree => Engine.GetMainLoop() as SceneTree;

    #region Businesses seen (TODO item 17)

    /// <summary>
    /// Companies this computer player has seen (user's rule, TODO item 17): like the human, who can only order things
    /// against businesses seen through the fog of war, it may harm only businesses its own or its allies' units and
    /// buildings have seen. Updated every game hour with the fog's sight rules (<see cref="Vision"/>, buildings see
    /// <see cref="FogOfWar.BuildingSight"/> cells).
    /// </summary>
    private readonly HashSet<Company> _seen = new();

    private readonly Dictionary<Company, (Vector2I Cell, bool[] Grid)> _buildingGrids = new();

    public bool HasSeen(Company company) => _seen.Contains(company);

    public int SeenCount => _seen.Count;

    /// <summary>Test hook.</summary>
    public void MarkSeen(Company company) => _seen.Add(company);

    private void UpdateSeen()
    {
        var companies = AllCompanies().Where(c => c.IsEconomic && !_seen.Contains(c)).ToList();
        if (companies.Count == 0)
            return;
        var grids = new List<(bool[] Grid, Vector2I Center, int R)>();
        foreach (var unit in AllUnits().Where(u => FogOfWar.IsFriendly(Team, u.OwnerTeam) && !u.InsideBuilding))
        {
            var grid = unit.SightGrid(out var center, out var r);
            grids.Add((grid, center, r));
        }
        foreach (var own in AllCompanies().Where(c => FogOfWar.IsFriendly(Team, c.OwnerTeam)))
        {
            if (!_buildingGrids.TryGetValue(own, out var cached) || cached.Cell != own.Cell)
                _buildingGrids[own] = cached = (own.Cell, Vision.Build(own.Cell, FogOfWar.BuildingSight));
            grids.Add((cached.Grid, own.Cell, FogOfWar.BuildingSight));
        }
        foreach (var company in companies)
            if (grids.Any(g => Vision.Visible(g.Grid, g.Center, g.R, company.Cell)))
                _seen.Add(company);
    }

    #endregion

    private static IEnumerable<Unit> AllUnits() =>
        Tree?.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => GodotObject.IsInstanceValid(u) && !u.IsDead)
        ?? Enumerable.Empty<Unit>();

    private static IEnumerable<Company> AllCompanies() =>
        Tree?.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(GodotObject.IsInstanceValid)
        ?? Enumerable.Empty<Company>();

    private bool IsAlly(Team other) =>
        other != null && Team.GetTargetRelation(other.TeamColor) is Teams.TeamRelation.Friend or Teams.TeamRelation.Self;

    #region Event 450h (sub_4623E4)

    private void SetShares()
    {
        if (Org == null)
            return;
        for (var i = 0; i < Organisation.TypeCount; i++)
            Org.Shares[i] = Parameters[5 + i];
        // Remake rule (user, 2026-11-04): part of the savings goes to politicians so that brownouts can be bought.
        if (!GameConfig.AiBrownouts)
            return;
        var moved = Math.Min(Org.Shares[0], GameConfig.AiPoliticianShare);
        Org.Shares[0] -= moved;
        Org.Shares[PoliticianAuthority] += moved;
    }

    private const int PoliticianAuthority = 3;

    #endregion

    #region Brownouts on the enemy (remake rule, user 2026-11-04)

    /// <summary>Game minute of this player's last brownout order.</summary>
    private long _lastBrownout = long.MinValue / 2;

    public static event Action<Team, Team, Company> BrownoutOrdered;

    /// <summary>
    /// The user saw computer players in the original keep cutting the power of the human's blocks; the EXE's AI code
    /// (sub_461510 family) has no such call, so this is a remake rule: at most once per
    /// <see cref="GameConfig.AiBrownoutDays"/>, in the strategy cycle, the main enemy's best-earning block that still
    /// has power gets a brownout, bought like the player's own (<see cref="CompanyServices.Brownout"/>: politician
    /// services 1Ch / 19h / 15h, needing their bribe money and favour points).
    /// </summary>
    private void TryBrownout()
    {
        var days = GameConfig.AiBrownoutDays;
        var now = GameClock.Instance?.Minute ?? 0;
        if (!GameConfig.AiBrownouts || days <= 0 || now - _lastBrownout < (long)days * GameClock.MinutesPerDay)
            return;
        var enemy = MainEnemy();
        if (enemy == null)
            return;
        var target = BrownoutTarget(enemy, AllCompanies().Where(HasSeen));
        if (target == null || !CompanyServices.Brownout(Team, target))
            return;
        _lastBrownout = now;
        GD.Print($"AI {Team.PlayerName}: brownout on {enemy.PlayerName}'s block {target.Block?.Number}");
        BrownoutOrdered?.Invoke(Team, enemy, target);
    }

    /// <summary>The human unless allied, else the richest other hostile player still in the game.</summary>
    private Team MainEnemy()
    {
        var rivals = Teams.TeamsInGame.Values
            .Where(t => t != Team && t.Org != null && !t.Org.Eliminated && !IsAlly(t)).ToList();
        var human = Teams.GetCurrentPlayerTeam();
        return rivals.Contains(human) ? human : rivals.OrderByDescending(t => t.Org.NetIncome).FirstOrDefault();
    }

    /// <summary>
    /// A company in the block where <paramref name="enemy"/> earns most, among blocks that still have power; only
    /// <paramref name="companies"/> count (the caller passes the businesses it has seen).
    /// </summary>
    public static Company BrownoutTarget(Team enemy, IEnumerable<Company> companies) =>
        companies
            .Where(c => c.OwnerTeam == enemy && c.IsEconomic && c.Block is { IsActive: true })
            .GroupBy(c => c.Block)
            .OrderByDescending(g => g.Sum(c => Math.Max(0, c.Income())))
            .Select(g => g.First())
            .FirstOrDefault();

    #endregion

    #region Event 44Dh: unit tasking (sub_461CA8 / sub_4618D0)

    /// <summary>
    /// Every own unit that is alive, not a Collector, not the left-alone type, available and not fighting gets a task.
    /// Deviation: a unit already extorting keeps its order (re-issuing would restart the attempt).
    /// </summary>
    private void TaskUnits()
    {
        foreach (var unit in AllUnits().Where(u => u.OwnerTeam == Team).ToList())
        {
            if (unit.UnitType == UnitCommon.UnitType.Collector || (int)unit.UnitType == Parameters[12]
                || unit.IsArrested || unit.IsFighting || unit.CurrentOrder is ExtortOrder
                || unit.Vehicle != null) // passengers are off the map's object list
                continue;
            TaskUnit(unit);
        }
    }

    /// <summary>vt+140h (sub_4618D0).</summary>
    private void TaskUnit(Unit unit)
    {
        // vt+1F0h (sub_42FC00, unit +0F8h = the .epi UNIT flag B): such a unit is never given a task (a guard post).
        if (unit.EpiFlagB != 0)
            return;
        if (unit.UnitType == UnitCommon.UnitType.Thug)
        {
            ExtortNear(unit);
            return;
        }
        if (_attackIntruder && _intruder != null && GodotObject.IsInstanceValid(_intruder) && !_intruder.IsDead)
        {
            unit.AttackOrder(_intruder, false);
            return;
        }
        var human = Teams.GetCurrentPlayerTeam();
        if (_attackHuman && human != null && !IsAlly(human))
        {
            AttackCenter(unit, human);
            return;
        }
        if (_attackTarget && _target != null && !IsAlly(_target))
        {
            AttackCenter(unit, _target);
            return;
        }
        if (unit.UnitType == UnitCommon.UnitType.Cadillac)
            return;
        if (Org.NetIncome < Parameters[4])
        {
            ExtortNear(unit);
            return;
        }
        var home = Training.PrimaryCenter(Team);
        if (home != null && MapTileMath.GetDistanceExact(unit, home) >= HomeRadius)
            unit.GoTo(home.Position, false);
    }

    /// <summary>Order 4284B0 mode 3 on the other player's training center (vt+8Ch).</summary>
    /// <summary>For tests: a computer player's strategy (convert, attack the human HQ), conversions and HQ attacks.</summary>
    public static event Action<Team, bool, bool> StrategyDecided;
    public static event Action<Team, Company, Company.ConversionResult?> ConversionTried;
    public static event Action<Team, Company> AttackOrdered;

    private static void AttackCenter(Unit unit, Team enemy)
    {
        var center = Training.PrimaryCenter(enemy);
        if (center == null || unit.CanAttack == null)
            return;
        AttackOrdered?.Invoke(unit.OwnerTeam, center);
        unit.CancelOrder();
        unit.CanAttack.Target = center.CanBeAttacked;
        unit.WalkTo(center);
    }

    /// <summary>
    /// vt+120h (sub_461781 / vt+150h sub_461690): search square rings around the unit, nearest first, for a company of
    /// nobody or a non-ally outside the center district; each try passes with a 50 % chance. Approximation: one try
    /// per company in ring order (the original tries every cell of the ring, so big companies get more chances).
    /// </summary>
    private void ExtortNear(Unit unit)
    {
        var origin = OriginalCoords(unit.Position);
        var candidates = AllCompanies()
            .Where(c => c.IsEconomic && c.OwnerTeam != Team && !IsAlly(c.OwnerTeam) && (c.Block?.District ?? 0) != 2
                        && ExtortOrder.CanExtort(unit, c))
            .Select(c =>
            {
                var d = OriginalCoords(c.Position) - origin;
                return (Company: c, Ring: Mathf.Max(Mathf.Abs(Mathf.RoundToInt(d.X)), Mathf.Abs(Mathf.RoundToInt(d.Y))));
            })
            .Where(x => x.Ring <= ExtortSearchRings)
            .OrderBy(x => x.Ring).ThenBy(_ => Rng.Next());
        foreach (var (company, _) in candidates)
        {
            if (Rng.Next(2) != 0)
                continue;
            unit.SetOrder(new ExtortOrder(unit, company));
            return;
        }
    }

    /// <summary>Position in the original game's map axes (cells along <see cref="MapTileMath.Right"/> / Down).</summary>
    private static Vector2 OriginalCoords(Vector2 local)
    {
        var p = local - (MapTileMath.Tilemap?.MapToLocal(Vector2I.Zero) ?? Vector2.Zero);
        Vector2 r = MapTileMath.Right, d = MapTileMath.Down;
        var det = r.X * d.Y - r.Y * d.X;
        return new Vector2((p.X * d.Y - p.Y * d.X) / det, (r.X * p.Y - r.Y * p.X) / det);
    }

    #endregion

    #region Event 44Eh: recruiting and conversion (sub_461F24)

    private void RecruitAndConvert()
    {
        var center = Training.PrimaryCenter(Team);
        if (center != null && Training.Current(Team) == null)
        {
            if (_recruitChosen)
                Recruit(ChooseType());
            else if (_recruitThug)
                Recruit(UnitCommon.UnitType.Thug);
        }
        if (_convert)
            ConvertOne();
    }

    /// <summary>vt+12Ch / vt+128h: training job sub_4274B5 at the center.</summary>
    private void Recruit(UnitCommon.UnitType type)
    {
        var recruit = Training.Recruits.FirstOrDefault(r => r.Type == type);
        if (recruit != null)
            Training.Start(Team, recruit);
    }

    /// <summary>
    /// vt+158h (sub_461BD7): the best type whose army service is available — Thompson (service 6), Hitman (3),
    /// Shotgun (2), Pistol (0) — else the Thug.
    /// </summary>
    private UnitCommon.UnitType ChooseType()
    {
        if (Org.CanUseItem(6)) return UnitCommon.UnitType.Thompson;
        if (Org.CanUseItem(3)) return UnitCommon.UnitType.Hitman;
        if (Org.CanUseItem(2)) return UnitCommon.UnitType.Shotgun;
        return Org.CanUseItem(0) ? UnitCommon.UnitType.Pistol : UnitCommon.UnitType.Thug;
    }

    /// <summary>
    /// vt+13Ch (sub_461E10): of the own legal companies that are not busy and have a row to become
    /// (<see cref="BestRow"/>), the one nearest the training center starts converting (job sub_427000).
    /// </summary>
    private void ConvertOne()
    {
        var center = Training.PrimaryCenter(Team);
        if (center == null)
            return;
        Company best = null;
        OriginalBusinessTable.Entry bestRow = null;
        var bestDist = float.MaxValue;
        foreach (var company in AllCompanies())
        {
            if (company.OwnerTeam != Team || !company.IsEconomic || company.BusinessType != OriginalBusinessTable.BusinessType.Legal
                || company.Busy)
                continue;
            var d = MapTileMath.GetDistanceExact(company, center);
            if (d >= bestDist)
                continue;
            var row = BestRow(company);
            if (row == null)
                continue;
            best = company;
            bestRow = row;
            bestDist = d;
        }
        var result = best?.StartConversion(bestRow);
        ConversionTried?.Invoke(Team, best, result);
    }

    /// <summary>
    /// vt+144h (sub_461D40): the most expensive catalogue row of the AI's business type, the company's grade, placeable
    /// in its district, available to the AI and costing less than cash + 100 000.
    /// </summary>
    private OriginalBusinessTable.Entry BestRow(Company company)
    {
        var current = company.Entry;
        if (current == null)
            return null;
        var district = company.Block?.District ?? 3;
        OriginalBusinessTable.Entry best = null;
        foreach (var row in OriginalBusinessTable.Instance.Entries)
        {
            if ((row.Value24 != 3 && row.Value24 != district) || row.Grade != current.Grade || row.Type != _style
                || !BusinessRules.IsAvailable(Org, row))
                continue;
            if (Org.Cash + ConversionCashMargin > row.Price && row.Price > (best?.Price ?? 0))
                best = row;
        }
        return best;
    }

    #endregion

    #region Event 44Fh: strategy (sub_4620D7)

    /// <summary>vt+14Ch (sub_461F88): summed strength weights of a player's units, its Thugs and their salaries.</summary>
    public static int Strength(Team team, out int thugs, out long salaries)
    {
        var strength = 0;
        thugs = 0;
        salaries = 0;
        foreach (var unit in AllUnits().Where(u => u.OwnerTeam == team))
        {
            strength += WeightOf(unit.UnitType);
            salaries += unit.Salary;
            if (unit.UnitType == UnitCommon.UnitType.Thug)
                thugs++;
        }
        return strength;
    }

    /// <summary>Strength weight of a unit type (table in sub_461F88); 0 for types without one.</summary>
    public static int WeightOf(UnitCommon.UnitType type) =>
        (int)type is var t && t >= 0 && t < StrengthWeight.Length ? StrengthWeight[t] : 0;

    /// <summary>sub_4620D7: weaker when the human's strength + 5 exceeds ours; the human is a target when we are more than twice as strong.</summary>
    public static (bool Weaker, bool Target) Compare(int mine, int theirs) =>
        theirs + 5 > mine ? (true, false) : (false, mine / 2 > theirs);

    /// <summary>+1ECh (attack the human HQ) keeps its value: on at 12 or more strength beyond the Thugs, off below 7.</summary>
    public static bool AttackHq(bool wasAttacking, int armyStrength) =>
        armyStrength >= 12 || (armyStrength >= 7 && wasAttacking);

    /// <summary>
    /// vt+154h (sub_462028): the last listed unit of another player (not a Collector) near the AI's training center.
    /// Deviation: allies' and unowned (police) units are not counted; the original only skips its own.
    /// </summary>
    private Unit FindIntruder()
    {
        var center = Training.PrimaryCenter(Team);
        if (center == null)
            return null;
        Unit found = null;
        foreach (var unit in AllUnits())
            if (unit.OwnerTeam != null && !IsAlly(unit.OwnerTeam) && unit.UnitType != UnitCommon.UnitType.Collector
                && MapTileMath.GetDistanceExact(unit, center) < IntruderRadius)
                found = unit;
        return found;
    }

    private long Price(UnitCommon.UnitType type) => Training.Price(type);

    private void Strategy()
    {
        _recruitChosen = _recruitThug = _convert = _weaker = _attackIntruder = _attackTarget = false;
        _intruder = null;
        _target = null;
        var p = Parameters;

        var mine = Strength(Team, out var myThugs, out var mySalaries);
        long incomeLead = 1_410_065_408; // 540BE400h: smallest (my net − human net)
        var human = Teams.GetCurrentPlayerTeam();
        if (human?.Org != null && human != Team && !human.Org.Eliminated)
        {
            var (weaker, target) = Compare(mine, Strength(human, out _, out _));
            _weaker = weaker;
            if (target)
                _target = human;
            incomeLead = Math.Min(incomeLead, Org.NetIncome - human.Org.NetIncome);
        }

        _intruder = FindIntruder();
        var cash = Org.Cash;
        var reserve = p[11];
        var chosen = ChooseType();
        if (chosen != UnitCommon.UnitType.Thug && ((_weaker && p[2] != 0) || p[10] != 0)
            && (reserve < cash || (cash > IntruderCashFloor && _intruder != null)))
        {
            _recruitChosen = true;
            cash -= Price(chosen);
        }
        if (myThugs < p[1] && reserve < cash && !_recruitChosen)
        {
            _recruitThug = true;
            cash -= Price(UnitCommon.UnitType.Thug);
        }
        if (!_recruitChosen && !_recruitThug && reserve < cash && mySalaries < p[3] / PercentDivisor * Org.GrossIncome)
        {
            _recruitChosen = true;
            cash -= Price(chosen);
        }
        if (!_recruitChosen && !_recruitThug && Org.NetIncome > RichIncome && reserve < cash && incomeLead / 2 > mySalaries)
        {
            _recruitChosen = true;
            cash -= Price(chosen);
        }
        if (reserve < cash && p[0] != 0)
            _convert = true;

        if (_intruder != null)
            _attackIntruder = true;
        else if (_target != null)
            _attackTarget = true;

        var wasAttacking = _attackHuman;
        if (p[10] != 0)
            _attackHuman = AttackHq(_attackHuman, mine - myThugs);
        StrategyDecided?.Invoke(Team, _convert, _attackHuman);
        TryBrownout();
        if (_recruitChosen || _recruitThug || _convert || _attackIntruder || _attackHuman != wasAttacking)
            GD.Print($"AI {Team.PlayerName}: strength {mine} (thugs {myThugs}), cash {Org.Cash}, recruit {(_recruitChosen ? chosen.ToString() : _recruitThug ? "Thug" : "-")}, "
                 + $"convert {_convert}, intruder {_intruder?.UnitType.ToString() ?? "-"}, target {_target?.PlayerName ?? "-"}, attack HQ {_attackHuman}");
    }

    #endregion

    public string Describe() =>
        $"{Team.PlayerName} kind {Team.Kind} style {_style} params [{string.Join(" ", Parameters)}] weaker {_weaker} "
        + $"recruit {_recruitChosen}/{_recruitThug} convert {_convert} intruder {_intruder?.UnitType.ToString() ?? "-"} "
        + $"target {_target?.PlayerName ?? "-"} attackHQ {_attackHuman}";
}
