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
public class ComputerPlayer
{
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
        clock.ScheduleIn(GameClock.MinutesPerHour, () => Repeat(clock, TaskUnits));
        clock.ScheduleIn(2 * GameClock.MinutesPerHour, () => Repeat(clock, RecruitAndConvert));
        clock.ScheduleIn(3 * GameClock.MinutesPerHour, () => Repeat(clock, Strategy));
        clock.ScheduleIn(3 * GameClock.MinutesPerHour, SetShares);
    }

    private void Repeat(GameClock clock, Action cycle)
    {
        if (Org == null || Org.Eliminated || !Teams.TeamsInGame.ContainsValue(Team))
            return;
        cycle();
        clock.ScheduleIn(CycleMinutes, () => Repeat(clock, cycle));
    }

    private static SceneTree Tree => Engine.GetMainLoop() as SceneTree;

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
    }

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
                || unit.IsArrested || unit.IsFighting || unit.CurrentOrder is ExtortOrder)
                continue;
            TaskUnit(unit);
        }
    }

    /// <summary>vt+140h (sub_4618D0).</summary>
    private void TaskUnit(Unit unit)
    {
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
    private static void AttackCenter(Unit unit, Team enemy)
    {
        var center = Training.PrimaryCenter(enemy);
        if (center == null || unit.CanAttack == null)
            return;
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
        var a = p.X / MapTileMath.Right.X;
        var b = p.Y / MapTileMath.Right.Y;
        return new Vector2((a + b) / 2f, (a - b) / 2f);
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
        best?.StartConversion(bestRow);
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
            var type = (int)unit.UnitType;
            if (type >= 0 && type < StrengthWeight.Length)
                strength += StrengthWeight[type];
            salaries += unit.Salary;
            if (unit.UnitType == UnitCommon.UnitType.Thug)
                thugs++;
        }
        return strength;
    }

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
            var theirs = Strength(human, out _, out _);
            if (theirs + 5 > mine)
                _weaker = true;
            else if (mine / 2 > theirs)
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

        // +1ECh keeps its value between cycles: on at 12 or more strength beyond the Thugs, off below 7.
        var wasAttacking = _attackHuman;
        if (p[10] != 0)
        {
            var army = mine - myThugs;
            if (army >= 12)
                _attackHuman = true;
            else if (army < 7)
                _attackHuman = false;
        }
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
