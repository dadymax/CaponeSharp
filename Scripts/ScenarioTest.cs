using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Headless scenario checks (PLAN M13). Plays episodes without anyone at the controls, fast, and checks game rules that
/// must always hold after every game hour. Halfway through it uses the local player's authority services on enemy
/// businesses (raid, demolish, brownout); at the end it destroys every computer player's HQ, which must eliminate them
/// and win the episode for the local player.
/// <para>Run: <c>Godot --headless --fixed-fps 60 --path . -- --scenario-test[=1,4,8,13,15] [--days=N] [--speed=N]</c>.
/// <c>--fixed-fps</c> lets the game run as fast as the CPU allows. Exit code 0 = no failures.</para>
/// </summary>
public partial class ScenarioTest : Node
{
    public static readonly int[] DefaultEpisodes = { 1, 4, 6, 8, 13, 15 };
    public const float DefaultSpeed = 16f;
    public const int DefaultDays = 14;

    public static ScenarioTest Instance { get; private set; }

    private static Queue<int> _queue;
    private static int _days = DefaultDays;
    private static int _failures;
    private static readonly List<string> Summary = new();

    // ---- start / finish ---------------------------------------------------------------------------------------------

    public static bool Requested(string[] args) => args.Any(a => a == "--scenario-test" || a.StartsWith("--scenario-test="));

    /// <summary>The next episode to play, or 0 when all are done.</summary>
    public static int NextEpisode(string[] args)
    {
        if (_queue == null)
        {
            _queue = new Queue<int>(DefaultEpisodes);
            foreach (var arg in args)
            {
                if (arg.StartsWith("--scenario-test="))
                    _queue = new Queue<int>(arg["--scenario-test=".Length..].Split(',')
                        .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0));
                if (arg.StartsWith("--days=") && int.TryParse(arg["--days=".Length..], out var d) && d > 0)
                    _days = d;
            }
            GD.Print($"=== Scenario test: episodes {string.Join(", ", _queue)}, {_days} game days each ===");
        }
        return _queue.Count > 0 ? _queue.Dequeue() : 0;
    }

    /// <summary>Prints the summary; returns the exit code.</summary>
    public static int Finish()
    {
        GD.Print("=== Scenario test summary ===");
        foreach (var line in Summary)
            GD.Print(line);
        GD.Print($"=== Scenario test done: {_failures} failure(s) ===");
        return _failures == 0 ? 0 : 1;
    }

    // ---- one episode ------------------------------------------------------------------------------------------------

    private readonly int _episode;
    private GameClock _clock;
    private bool _ended;
    private int _errorsAtStart;
    private int _hour;
    private readonly HashSet<string> _failed = new();
    private readonly HashSet<string> _checked = new();
    private readonly List<string> _notes = new();
    private readonly Dictionary<Company, Team> _hqOwners = new();
    private readonly Dictionary<Company, Team> _owners = new();
    private readonly Dictionary<Unit, Vector2I> _firstCell = new();
    private readonly Dictionary<Company, (long BuildStart, long Uncollected)> _building = new();
    private readonly Dictionary<Team, int> _thugStarvedHours = new();
    private readonly Dictionary<Team, int> _unitCounts = new();
    /// <summary>Delayed checks: deadline hour, name, condition, failure detail.</summary>
    private readonly List<(int Hour, string Name, System.Func<bool> Check, System.Func<string> Detail)> _pending = new();

    public ScenarioTest() { }

    public ScenarioTest(int episode)
    {
        _episode = episode;
        Name = "ScenarioTest";
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        Instance = this;
        _clock = GameClock.Instance;
        if (!OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--speed=")))
            _clock.Speed = DefaultSpeed;
        _errorsAtStart = GameClock.EventErrors;
        CollectorJob.Delivered += OnDelivered;
        ComputerPlayer.StrategyDecided += OnStrategy;
        ComputerPlayer.ConversionTried += OnConversion;
        ComputerPlayer.AttackOrdered += OnAttackOrdered;
        foreach (var c in Companies())
        {
            if (c.IsHq)
                _hqOwners[c] = c.OwnerTeam;
            _owners[c] = c.OwnerTeam;
            // sub_420ED5: no income while a conversion job runs (vt+0A0h).
            if (c.UnderConstruction)
            {
                if (_building.TryGetValue(c, out var job) && job.BuildStart == c.BuildStart)
                    Check("a business under construction earns nothing", c.Uncollected <= job.Uncollected,
                        $"{c}: uncollected {job.Uncollected} -> {c.Uncollected}");
                _building[c] = (c.BuildStart, c.Uncollected);
            }
            else
                _building.Remove(c);
        }
        _clock.TurnPassed += OnTurn;
        var players = string.Join(", ", Teams.TeamsInGame.Values.Where(t => t.Org != null)
            .Select(t => $"{t.PlayerName}{(t.IsCurrentPlayerColor ? " (local)" : t.Ai != null ? " (AI)" : "")}"));
        GD.Print($"--- Episode {_episode}: {players}");
    }

    private long _delivered;
    private bool _aiAttackDecided;
    private int _aiAttackOrders;

    private void OnStrategy(Team team, bool convert, bool attackHq) => _aiAttackDecided |= attackHq;

    private void OnAttackOrdered(Team team, Company center) => _aiAttackOrders++;

    /// <summary>A conversion a computer player starts (sub_461F24 → job sub_427050) must end as that business.</summary>
    private void OnConversion(Team team, Company company, Company.ConversionResult? result)
    {
        if (result != Company.ConversionResult.Started || company == null)
            return;
        var row = company.Building;
        var hours = (row?.Value1C ?? 0) / GameClock.MinutesPerHour + 2;
        _pending.Add((_hour + hours, "a computer player's conversion becomes a business", () =>
                !IsInstanceValid(company) || company.OwnerTeam != team || row == null || company.Entry == row
                || (row.Flag2C != 0 && company.Building == null), // +2Ch rows also need the politician at the end
            () => $"{team.PlayerName}: {company} (row {row?.Name})"));
    }
    private void OnDelivered(Team owner, long amount) => _delivered += amount;

    public override void _ExitTree()
    {
        CollectorJob.Delivered -= OnDelivered;
        ComputerPlayer.StrategyDecided -= OnStrategy;
        ComputerPlayer.ConversionTried -= OnConversion;
        ComputerPlayer.AttackOrdered -= OnAttackOrdered;
        if (_clock != null && IsInstanceValid(_clock))
            _clock.TurnPassed -= OnTurn;
        if (Instance == this)
            Instance = null;
    }

    private void OnTurn(long turn)
    {
        if (_ended)
            return;
        _hour = (int)turn;
        RunChecks();
        // A delayed check passes as soon as its condition holds and fails at its deadline.
        foreach (var p in _pending.ToList())
        {
            var ok = p.Check();
            if (!ok && p.Hour > _hour)
                continue;
            _pending.Remove(p);
            Check(p.Name, ok, p.Detail?.Invoke() ?? "");
        }
        if (_hour == 1)
            foreach (var block in CityBlock.All.Where(b => b.District is 1 or 2))
            {
                var layout = OriginalBlocks.Instance.Blocks.ElementAtOrDefault(block.Layout);
                var candidates = layout?.BlockLines.Sum(l => l.Count(ch => "BCMH".IndexOf(ch) >= 0)) ?? 0;
                if (candidates > 0)
                    Check("middle-class and center blocks have a business (user)",
                        block.Companies.Any(c => IsInstanceValid(c) && (c.IsEconomic || c.IsHq)),
                        $"block {block.Number} (layout {block.Layout}, district {block.District}): {block.Companies.Count} companies");
            }
        if (_hour == 1)
            foreach (var car in Units().Where(u => u.IsCar && u.OwnerTeam != null))
                Check("a starting Cadillac carries four Thompson men",
                    car.Passengers.Count() == Unit.CarSeats && car.Passengers.All(p => p.UnitType == UnitCommon.UnitType.Thompson),
                    $"{car.OwnerTeam.PlayerName}'s car: {string.Join(", ", car.Passengers.Select(p => p.UnitType))}");
        if (_hour == 1 && Teams.GetCurrentPlayerTeam() is { HasBase: false } baseless)
        {
            // Gate sub_42CCE5: without a base (HQ +1D2h = 0) the player's units may not extort.
            var unit = Units().FirstOrDefault(u => u.OwnerTeam == baseless && u.HasWeapon);
            var company = Companies().FirstOrDefault(c => c.IsEconomic && c.OwnerTeam == null);
            if (unit != null && company != null)
                Check("a player without a base can't extort", !ExtortOrder.CanExtort(unit, company), $"{unit.UnitType} vs {company}");
        }
        if (_hour == 1)
            foreach (var team in Teams.TeamsInGame.Values.Where(t => t.HasBase))
                // Bases go per PLAYER entry (episode 6 gives two players id 1).
                Check("every player with a base has its own HQ",
                    team.HqCompany != null && team.HqCompany.OwnerTeam == team,
                    $"{team.PlayerName} (id {team.PlayerId}): HQ {team.HqCompany?.Cell.ToString() ?? "none"}");
        if (_hour == 3 && VictoryManager.Instance is { } victory)
            foreach (var vc in victory.Conditions.Where(c => c.Type == VictoryCondition.ConditionType.Money && !c.IsCompleted))
            {
                // VCOND 1 (sub_463610) follows the player's weekly net income every game hour.
                var team = Teams.GetTeamByPlayerId(vc.TargetPlayer);
                if (team?.Org != null)
                    Check("the money objective follows the weekly net income", vc.CurrentProgress == team.Org.NetIncome,
                        $"progress {vc.CurrentProgress}, net income {team.Org.NetIncome}, target {vc.MoneyTarget}");
            }
        if (_hour == 6)
            MakeDeserter();
        if (_hour == 8)
            TestCadillac();
        if (_hour == 10)
            TestConstruction();
        if (_hour == 12)
            TestSpreadOut();
        if (_hour == 14)
            TestFight();
        if (_hour == 16)
            TestPolice();
        if (_hour == 18)
            TestHitman();
        if (_hour == 20)
            TestFortification();
        if (_hour == _days * 24 - 7 && _aiAttackDecided)
            Check("a computer player that decides to attack sends units at the human's center", _aiAttackOrders > 0,
                $"{_aiAttackOrders} attack orders");
        if (_hour == _days * 24 - 7)
        {
            // Collectors (sub_428D62) bring the uncollected money of their block home once a week of company income
            // has built up; any player with an HQ and companies should have had a delivery by now.
            var owners = Companies().Where(c => c.IsEconomic && c.OwnerTeam?.HqCompany != null).Select(c => c.OwnerTeam).Distinct().ToList();
            if (owners.Count == 0)
                Note("no player with an HQ owns companies: no collector check");
            else
                Check("collectors bring money to the HQ", _delivered > 0,
                    $"delivered {_delivered}; owners {string.Join(", ", owners.Select(t => t.PlayerName))}");
        }
        if (_hour == System.Math.Min(24, _days * 12))
            UseAuthorityServices();
        if (_hour == System.Math.Min(30, _days * 12 + 6))
        {
            SendThief();
            KillACollector();
        }
        if (_hour == _days * 24 - 6)
            WipeOutBaselessPlayer();
        if (_hour >= _days * 24)
            DestroyComputerPlayers();
    }

    /// <summary>The end screen would show (Main.ShowEndScreen): record the result and go to the next episode.</summary>
    public void EpisodeEnded(bool won, string text)
    {
        if (_ended)
            return;
        RunChecks();
        if (_expectDefeat)
        {
            _expectDefeat = false;
            Check("a player without an HQ who lost every unit is defeated (user)", !won, $"won {won}: {text}");
            _pending.RemoveAll(p => p.Name.StartsWith("a player without an HQ"));
        }
        var day = _clock.Minute / GameClock.MinutesPerDay + 1;
        End($"{(won ? "won" : "lost")} on day {day}: {text?.Replace('\n', ' ')}");
    }

    private void End(string outcome)
    {
        _ended = true;
        foreach (var (_, name, _, _) in _pending)
            Note($"not checked (episode ended first): {name}");
        var passed = _checked.Where(n => !_failed.Contains(n)).OrderBy(n => n).ToList();
        Summary.Add($"Episode {_episode}: {outcome}");
        Summary.Add($"  passed: {string.Join("; ", passed)}");
        if (_failed.Count > 0)
            Summary.Add($"  FAILED: {string.Join("; ", _failed.OrderBy(n => n))}");
        foreach (var note in _notes)
            Summary.Add($"  note: {note}");
        GD.Print($"--- Episode {_episode} {outcome}");
        CallDeferred(MethodName.NextScene);
    }

    private void NextScene()
    {
        _clock.Paused = false;
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
    }

    // ---- invariants ---------------------------------------------------------------------------------------------------

    private void RunChecks()
    {
        var teams = Teams.TeamsInGame.Values.Where(t => t.Org != null).ToList();
        var units = Units().ToList();
        var companies = Companies().ToList();

        Check("no failed game events", GameClock.EventErrors == _errorsAtStart, $"{GameClock.EventErrors - _errorsAtStart} event(s) threw");

        foreach (var t in teams)
        {
            var o = t.Org;
            Check("cash ledger adds up", o.Cash == o.StartCash + o.TotalIn - o.TotalOut,
                $"{t.PlayerName}: cash {o.Cash} vs start {o.StartCash} + in {o.TotalIn} - out {o.TotalOut}");
            for (var i = 1; i < Organisation.TypeCount; i++)
                Check("bribe money and favour not negative", o.AuthorityMoney[i] >= 0 && o.Favour[i] > -0.001f,
                    $"{t.PlayerName}: authority {i} money {o.AuthorityMoney[i]}, favour {o.Favour[i]}");
            if (!o.Eliminated)
                continue;
            Check("eliminated player owns nothing",
                t.HqCompany == null && companies.All(c => c.OwnerTeam != t) && units.All(u => u.OwnerTeam != t),
                $"{t.PlayerName}: HQ {t.HqCompany != null}, companies {companies.Count(c => c.OwnerTeam == t)}, units {units.Count(u => u.OwnerTeam == t)}");
        }

        foreach (var c in companies)
        {
            Check("company HP within 0..max", c.Hp >= 0 && c.Hp <= c.MaxHp, $"{c}");
            Check("uncollected money not negative", c.Uncollected >= 0, $"{c}");
            if (c.IsHq)
                Check("HQ keeps its owner and 1300 HP",
                    c.OwnerTeam != null && c.OwnerTeam.HqCompany == c && c.MaxHp == Company.HqMaxHp
                    && _hqOwners.TryGetValue(c, out var first) && first == c.OwnerTeam,
                    $"{c}, first owner {(_hqOwners.TryGetValue(c, out var f) ? f?.PlayerName : "unknown")}");
            if (_owners.TryGetValue(c, out var before) && before != null && c.OwnerTeam == null && c.IsEconomic)
                Check("a released business becomes a legal company",
                    c.BusinessType == OriginalBusinessTable.BusinessType.Legal, $"{c}, was {before.PlayerName}'s");
            _owners[c] = c.OwnerTeam;
        }

        var rect = Main.Instance?.Map?.GetUsedRect() ?? new Rect2I();
        foreach (var u in units)
        {
            var hp = u.CanBeAttacked?.HP ?? 1;
            var max = u.CanBeAttacked?.MaxHP ?? 1;
            Check("unit HP within 1..max", hp > 0 && hp <= max, $"{u.UnitType} of {u.OwnerTeam?.PlayerName ?? "-"}: HP {hp}/{max}");
            if (!u.IsArrested)
                Check("unit stays on the map", rect.HasPoint(u.Cell), $"{u.UnitType} of {u.OwnerTeam?.PlayerName ?? "-"} at {u.Cell}, map {rect}");
            // sub_432E80: cars keep off the pavement (an episode may place one there at the start).
            if (u.RoadOnly && u.Vehicle == null)
            {
                if (!_firstCell.TryGetValue(u, out var start))
                    _firstCell[u] = start = u.Cell;
                if (u.Cell != start)
                    Check("cars keep off the pavement", !MoveMap.IsPavement(u.Cell), $"{u.UnitType} of {u.OwnerTeam?.PlayerName ?? "police"} at {u.Cell}");
            }
        }

        foreach (var d in units.Where(u => u.IsDeserter || u.IsRogue))
            foreach (var u in units.Where(u => u.OwnerTeam != null))
                Check("deserters and players are hostile to each other", d.IsHostileTo(u) && u.IsHostileTo(d),
                    $"{d.UnitType} deserter vs {u.UnitType} of {u.OwnerTeam.PlayerName}");

        foreach (var t in teams.Where(t => t.Ai != null && !t.Org.Eliminated))
        {
            var count = units.Count(u => u.OwnerTeam == t);
            var thugs = units.Count(u => u.OwnerTeam == t && u.UnitType == UnitCommon.UnitType.Thug);
            var p = t.Ai.Parameters;
            var starved = thugs < p[1] && t.Org.Cash > p[11] + 200_000;
            var grew = _unitCounts.TryGetValue(t, out var last) && count > last;
            _unitCounts[t] = count;
            _thugStarvedHours[t] = starved && !grew ? _thugStarvedHours.GetValueOrDefault(t) + 1 : 0;
            Check("a rich computer player recruits up to its minimum Thugs", _thugStarvedHours[t] < 48,
                $"{t.PlayerName}: {thugs} Thugs < {p[1]}, cash {t.Org.Cash}, no recruit for 48 h");
        }
    }

    // ---- scripted actions -------------------------------------------------------------------------------------------

    /// <summary>One of the local player's gangsters quits as if unpaid.</summary>
    private void MakeDeserter()
    {
        var human = Teams.GetCurrentPlayerTeam();
        var gangsters = Units().Where(u => u.OwnerTeam == human && u.UnitType != UnitCommon.UnitType.Collector).ToList();
        if (gangsters.Count < 2)
        {
            Note("no deserter made (fewer than two gangsters)");
            return;
        }
        var unit = gangsters[^1];
        unit.Resign();
        Check("a resigned unit has no owner", unit.OwnerTeam == null && unit.IsDeserter, $"{unit.UnitType}");
    }

    /// <summary>Raid, demolish and brownout on enemy companies, paid with authority favour given for the test.</summary>
    private void UseAuthorityServices()
    {
        var human = Teams.GetCurrentPlayerTeam();
        if (human?.Org == null || human.Org.Eliminated)
            return;
        for (var a = 1; a < Organisation.TypeCount; a++)
        {
            human.Org.Favour[a] += 10_000;
            human.Org.AuthorityMoney[a] += 100_000_000;
        }
        var targets = Companies().Where(c => c.OwnerTeam != null && c.OwnerTeam != human && c.IsEconomic).ToList();

        var raid = Pick(targets, human, 5);
        if (raid.Company != null)
        {
            var c = raid.Company;
            Check("raid ordered", raid.Action.Run(), $"{c}");
            _pending.Add((_hour + 72, "a raided business becomes an unowned legal company", () =>
                IsInstanceValid(c) && c.OwnerTeam == null && c.BusinessType == OriginalBusinessTable.BusinessType.Legal,
                () => $"{c}; {string.Join("; ", GetTree().Root.FindChildren("PoliceRaid*", "", true, false).OfType<PoliceRaid>().Select(r => r.Describe()))}"));
        }
        else
            Note("no enemy business to raid");

        var demolish = Pick(targets.Where(c => c != raid.Company), human, 4);
        if (demolish.Company != null)
        {
            var c = demolish.Company;
            Check("demolition ordered", demolish.Action.Run(), $"{c}");
            _pending.Add((_hour + 1, "a demolished company is gone", () => !IsInstanceValid(c) || !c.IsInGroup(Company.GroupName), null));
        }
        else
            Note("no enemy slum business to demolish");

        var brownout = Pick(targets.Where(c => c != raid.Company && c != demolish.Company), human, 3);
        if (brownout.Company != null)
        {
            var block = brownout.Company.Block;
            long Uncollected() => Companies().Where(c => c.Block == block).Sum(c => c.Uncollected);
            Check("brownout ordered", brownout.Action.Run(), $"{brownout.Company}");
            Check("brownout cuts the block's power", !block.IsActive, $"block {block.Number}");
            var before = Uncollected();
            var due = _hour + 6;
            _pending.Add((due, "a blacked-out block earns nothing", () => _hour >= due && (block.IsActive || Uncollected() <= before),
                () => $"uncollected {Uncollected()} > {before}"));
        }
        else
            Note("no enemy company for a brownout");
    }

    /// <summary>A Thief for the local player steals from an enemy company (or HQ) and brings the money home.</summary>
    private void SendThief()
    {
        var human = Teams.GetCurrentPlayerTeam();
        if (human?.HqCompany == null || human.Org.Eliminated)
        {
            Note("no thief sent (no local HQ)");
            return;
        }
        var target = Companies().Where(c => c.OwnerTeam != null && c.OwnerTeam != human && c.IsEconomic && c.Uncollected > 0)
                         .OrderByDescending(c => c.Uncollected).FirstOrDefault()
                     ?? Companies().FirstOrDefault(c => c.IsHq && c.OwnerTeam != human && c.OwnerTeam?.Org?.Cash > 0);
        if (target == null)
        {
            Note("no enemy company with money for the thief");
            return;
        }
        // Created next to the target, so enemy gangsters rarely get him first; a dead thief is only noted.
        var thief = CampaignManager.Instance.CreateUnitNear(UnitCommon.UnitType.Thief, human, target.Cell);
        thief.TargetStructure = target;
        long stolen = 0, before = 0;
        var died = false;
        bool Dead()
        {
            if (died || (IsInstanceValid(thief) && !thief.IsDead))
                return died;
            died = true;
            Note($"the thief died before finishing (stolen {stolen})");
            return true;
        }
        _pending.Add((_hour + 24, "a thief steals from an enemy", () =>
        {
            if (Dead())
                return true;
            if (thief.CarriedMoney <= 0)
                return false;
            stolen = thief.CarriedMoney;
            before = human.Org.TotalIn + human.Org.TotalBribed;
            // Skip the walk home through enemy ground: the check is the delivery itself.
            var hqCell = human.HqCompany.Cell;
            var door = Enumerable.Range(0, 4).Select(d => MoveMap.Step(hqCell, d)).FirstOrDefault(c => MoveMap.Walkable(c, false), hqCell);
            thief.PlaceAt(thief.Map.MapToLocal(door)); // an edge neighbour: the original reaches buildings edge to edge
            thief.TargetStructure = human.HqCompany;
            _pending.Add((_hour + 96, "a thief delivers at his HQ", () => Dead()
                || (thief.CarriedMoney == 0 && human.Org.TotalIn + human.Org.TotalBribed == before + stolen),
                () => $"thief at {thief.Cell} carrying {thief.CarriedMoney} of {stolen}"));
            return true;
        }, () => $"thief at {thief.Cell} carrying {thief.CarriedMoney}, target {target}"));
    }

    /// <summary>
    /// A Cadillac for the local player: a Thug boards it, rides along, gets out on Unload, boards again, and the car is
    /// destroyed with him inside (he is hurt and gets out alive or dies).
    /// </summary>
    private bool _expectDefeat;

    /// <summary>A local player without an HQ (episode 4) loses every unit: the episode must end in defeat.</summary>
    private void WipeOutBaselessPlayer()
    {
        var human = Teams.GetCurrentPlayerTeam();
        if (human?.Org == null || human.Org.Eliminated || human.HqCompany != null)
            return;
        foreach (var u in Units().Where(u => u.OwnerTeam == human && !u.IsDead).ToList())
            u.CanBeAttacked?.ReceiveDamage(u.CanBeAttacked.HP, null);
        _expectDefeat = true;
        _pending.Add((_hour + 3, "a player without an HQ who lost every unit is defeated (user)", () => false,
            () => "no defeat within 3 hours"));
    }

    /// <summary>Two idle units sent to one cell spread out to a cell each (unit think → sub_42B500).</summary>
    private void TestSpreadOut()
    {
        var human = Teams.GetCurrentPlayerTeam();
        var idle = Units().Where(u => u.OwnerTeam == human && !u.IsDead && !u.IsCar && u.Vehicle == null
                                      && !u.InsideBuilding && u.HasNoJob && MoveMap.IsRoadOrPavement(u.Cell)).ToList();
        // Two that stand close together, so the walk is short.
        idle = idle.SelectMany(x => idle.Where(y => y != x).Select(y => (x, y)))
            .Where(p => MapTileMath.GetDistance(p.x, p.y) <= 4)
            .Select(p => new List<Unit> { p.x, p.y }).FirstOrDefault() ?? new List<Unit>();
        if (idle.Count < 2)
        {
            Note("no spread-out tested (no two idle units close together on the road)");
            return;
        }
        var (a, b) = (idle[0], idle[1]);
        var bStart = b.Cell;
        b.MoveTo(b.Map.MapToLocal(a.Cell));
        var met = false;
        _pending.Add((_hour + 16, "units sent to one cell spread out", () =>
        {
            if (!IsInstanceValid(a) || !IsInstanceValid(b) || a.IsDead || b.IsDead)
                return true; // killed meanwhile: nothing to check
            var idleBoth = a.HasNoJob && b.HasNoJob;
            if (idleBoth && a.Cell == b.Cell)
                met = true;
            // The meeting is sampled hourly and may be missed (arrive and spread within the hour): b must have walked
            // over and both stand idle on different cells.
            return idleBoth && a.Cell != b.Cell && b.Cell != bStart && MapTileMath.GetDistance(a, b) <= 2;
        }, () => $"{a.UnitType} at {a.Cell}, {b.UnitType} at {b.Cell}, met {met}, road/pavement neighbours "
                 + string.Join(" ", Enumerable.Range(0, 4).Select(d => MoveMap.Step(a.Cell, d))
                     .Select(c => $"{c}:{MoveMap.IsRoadOrPavement(c)}/{MoveMap.Walkable(c, false)}"))
                 + $", a job {!a.HasNoJob} target {a.CanAttack?.TargetParent?.Name}, b target {b.CanAttack?.TargetParent?.Name}"));
    }

    /// <summary>A company being converted earns nothing until the job ends (sub_420ED5, vt+0A0h).</summary>
    private void TestConstruction()
    {
        var pick = Companies()
            .Where(c => c.OwnerTeam?.Org != null && !c.OwnerTeam.Org.Eliminated && c.IsEconomic && !c.IsHq && !c.Busy
                        && c.Entry?.Income > 0 && c.Income() > 0)
            .SelectMany(c => System.Enum.GetValues<OriginalBusinessTable.BusinessType>()
                .SelectMany(type => BusinessRules.Offers(c, type)).Select(row => (Company: c, Row: row)))
            .Where(x => x.Row.Value1C >= 3 * GameClock.MinutesPerHour)
            .OrderByDescending(x => x.Row.Value1C)
            .FirstOrDefault();
        if (pick.Company == null)
        {
            Note("no conversion tested (no earning company with a long enough build)");
            return;
        }
        var (company, row) = pick;
        var org = company.OwnerTeam.Org;
        for (var a = 1; a < Organisation.TypeCount; a++)
            org.Favour[a] += 10_000;
        org.Spend(-row.Price); // pays for the build
        var result = company.StartConversion(row);
        Check("conversion started", result == Company.ConversionResult.Started, $"{company}: {result}");
        if (result != Company.ConversionResult.Started)
            return;
        var before = company.Uncollected;
        _pending.Add((_hour + 2, "a business under construction earns nothing", () =>
            !IsInstanceValid(company) || !company.UnderConstruction || company.Uncollected <= before,
            () => $"{company}: uncollected {before} -> {company.Uncollected}"));
    }

    private void TestCadillac()
    {
        var human = Teams.GetCurrentPlayerTeam();
        if (human?.HqCompany == null || human.Org.Eliminated)
        {
            Note("no Cadillac test (no local HQ)");
            return;
        }
        var cm = CampaignManager.Instance;
        var car = cm.CreateUnitNear(UnitCommon.UnitType.Cadillac, human, human.HqCompany.Cell);
        var thug = cm.CreateUnitNear(UnitCommon.UnitType.Thug, human, car.Cell);
        thug.SetOrder(new BoardOrder(thug, car));
        var start = car.Cell;
        var stage = 0;
        var hpBefore = 0;
        _pending.Add((_hour + 48, "a gangster boards a Cadillac, rides along and gets out", () =>
        {
            if (!IsInstanceValid(car) || car.IsDead || !IsInstanceValid(thug) || thug.IsDead)
            {
                if (stage < 2)
                    Note("the Cadillac or its gangster was killed before the ride test finished");
                stage = 9; // nothing more to check
                return true;
            }
            switch (stage)
            {
                case 0 when thug.Vehicle == car && car.HasPassengers && !thug.Visible:
                    stage = 1;
                    // A road cell: a Cadillac can't drive on pavement, so a free cell there may be out of its reach
                    // (seen once in episode 15 after the map was unmirrored: the car never arrived).
                    var goal = MoveMap.NearestRoad(start + new Vector2I(3, 3)) is { } road && road != start
                        ? road : cm.FreeCellNear(start + new Vector2I(3, 3));
                    car.GoTo(car.Map.MapToLocal(goal), false);
                    return false;
                case 1 when !car.IsMoving && car.Cell != start:
                    if (thug.Position != car.Position)
                        return false;
                    car.Unload();
                    stage = 2;
                    return thug.Vehicle == null && !thug.InsideBuilding && thug.Cell == car.Cell && !car.HasPassengers;
                default:
                    return false;
            }
        }, () => $"stage {stage}, thug in car {thug.Vehicle == car}, car at {car.Cell} moving {car.IsMoving}, thug at {thug.Cell}"));
        _pending.Add((_hour + 72, "a destroyed Cadillac hurts its passengers and lets the survivors out", () =>
        {
            if (stage == 9)
                return true; // the ride test ended early (noted)
            if (stage < 2 || !IsInstanceValid(car) || car.IsDead || !IsInstanceValid(thug) || thug.IsDead)
                return false;
            if (stage == 2)
            {
                if (!car.Board(thug))
                    return false;
                stage = 3;
                hpBefore = thug.CanBeAttacked.HP;
                for (var i = 0; i < 50 && !car.IsDead; i++)
                    car.CanBeAttacked.ReceiveDamage(car.CanBeAttacked.HP, null);
            }
            return car.IsDead && (thug.IsDead || (thug.Vehicle == null && thug.CanBeAttacked.HP < hpBefore));
        }, () => $"stage {stage}, car dead {car.IsDead}, thug dead {thug.IsDead}, HP {thug.CanBeAttacked?.HP} of {hpBefore}"));
    }

    /// <summary>An enemy collector carrying money is killed by the local player: the money goes to the killer.</summary>
    /// <summary>
    /// A scripted fight: a Thompson man of the local player is ordered to attack an enemy Pistol man created next to
    /// him. Shots must land (the target loses HP) and the fight must end with a death within a game day.
    /// </summary>
    private void TestFight()
    {
        var human = Teams.GetCurrentPlayerTeam();
        var enemy = Teams.TeamsInGame.Values.FirstOrDefault(t => t != human && t.Org is { Eliminated: false }
                                                                && human?.GetTargetRelation(t.TeamColor) == Teams.TeamRelation.Enemy);
        // Away from the other checks' units: the company farthest from every unit of the local player.
        var own = Units().Where(u => u.OwnerTeam == human).Select(u => u.Cell).ToList();
        Vector2I? place = own.Count == 0 ? null : Companies().Where(c => c.IsEconomic)
            .OrderByDescending(c => own.Min(o => Mathf.Abs(o.X - c.Cell.X) + Mathf.Abs(o.Y - c.Cell.Y)))
            .Select(c => (Vector2I?)c.Cell).FirstOrDefault();
        if (enemy == null || place == null)
        {
            Note("no enemy player or place for the scripted fight");
            return;
        }
        var shooter = CampaignManager.Instance.CreateUnitNear(UnitCommon.UnitType.Thompson, human, place.Value);
        var target = CampaignManager.Instance.CreateUnitNear(UnitCommon.UnitType.Pistol, enemy, shooter.Cell);
        if (shooter == null || target == null)
        {
            Note("scripted fight: units not created");
            return;
        }
        var full = target.CanBeAttacked.HP;
        var hurt = false;
        shooter.AttackOrder(target, false);
        _pending.Add((_hour + 24, "a scripted fight hurts and kills", () =>
        {
            var targetGone = !IsInstanceValid(target) || target.IsDead;
            hurt |= targetGone || target.CanBeAttacked.HP < full;
            return hurt && (targetGone || !IsInstanceValid(shooter) || shooter.IsDead);
        }, () => $"target HP {(IsInstanceValid(target) ? target.CanBeAttacked.HP : 0)} of {full}, shooter "
                 + (IsInstanceValid(shooter) ? $"{shooter.CanBeAttacked.HP} HP at {shooter.Cell}" : "gone")));
    }

    /// <summary>
    /// The cops are called on a Pistol man of the local player standing by a police station (Police.Dispatch, no
    /// lawyers): the car must arrest him and take him to jail (he leaves the game) within two game days.
    /// </summary>
    private void TestPolice()
    {
        var human = Teams.GetCurrentPlayerTeam();
        var station = Companies().FirstOrDefault(c => c.IsPoliceStation);
        if (human?.Org == null || human.Org.Eliminated || station == null || Police.Instance == null)
        {
            Note("no local player or police station for the arrest check");
            return;
        }
        var gangster = CampaignManager.Instance.CreateUnitNear(UnitCommon.UnitType.Pistol, human, station.Cell);
        if (gangster == null)
        {
            Note("arrest check: unit not created");
            return;
        }
        bool arrested = false, jailed = false, killed = false;
        // Jail (unit vt+1A0h) takes the arrested unit out of the game. Another gang may shoot him first (episode 15).
        gangster.Died += _ => { jailed = gangster.IsArrested; arrested |= jailed; killed = !jailed; };
        Police.Instance.Dispatch(gangster, lawyersCanHelp: false);
        _pending.Add((_hour + 48, "the police arrest and jail a gangster", () =>
        {
            arrested |= IsInstanceValid(gangster) && gangster.IsArrested;
            if (killed && !arrested)
            {
                Note("arrest check: the gangster was killed before the police came");
                return true;
            }
            return jailed;
        }, () => $"arrested {arrested}, jailed {jailed}, gangster "
                 + (IsInstanceValid(gangster) ? $"at {gangster.Cell}, dead {gangster.IsDead}" : "gone")
                 + $"; station {station.Cell}; cars " + string.Join(", ", Units().Where(u => u.UnitType == UnitCommon.UnitType.PoliceCar)
                     .Select(c => $"{c.Cell} moving {c.IsMoving} job {c.CurrentOrder?.Name}"))));
    }

    /// <summary>
    /// A Hit man who hasn't fired for 30 hours does his table damage to a unit (161–162, enough for a Pistol man);
    /// right after a shot he is exposed and does 20–30 (sub_42C890 / sub_42C960; the shot is stamped after the damage).
    /// </summary>
    private void TestHitman()
    {
        var human = Teams.GetCurrentPlayerTeam();
        var mine = Units().FirstOrDefault(u => u.OwnerTeam == human);
        var enemy = Teams.TeamsInGame.Values.FirstOrDefault(t => t != human && t.Org is { Eliminated: false });
        if (mine == null || enemy == null)
        {
            Note("no Hit man check (no own unit or enemy player)");
            return;
        }
        var hitman = CampaignManager.Instance.CreateUnitNear(UnitCommon.UnitType.Hitman, human, mine.Cell);
        var victim = CampaignManager.Instance.CreateUnitNear(UnitCommon.UnitType.Pistol, enemy, hitman.Cell);
        hitman.LastShotMinute = (GameClock.Instance?.Minute ?? 0) - Unit.ExposedAfterShotMinutes - 1;
        var hidden = hitman.CanAttack.DamageRange(victim);
        hitman.MarkShot();
        var exposed = hitman.CanAttack.DamageRange(victim);
        Check("a hidden Hit man kills with one shot, an exposed one does 20-30",
            hidden.Min >= victim.CanBeAttacked.MaxHP && exposed == (Unit.ExposedHitmanDamageMin, Unit.ExposedHitmanDamageMax),
            $"hidden {hidden}, exposed {exposed}, Pistol man HP {victim.CanBeAttacked.MaxHP}");
        hitman.Vanish();
        victim.Vanish();
    }

    /// <summary>
    /// A fortification (catalogue row 48 / 49, +28h = 7) shoots like a Pistol man (Company::Think sub_423500): an enemy
    /// Pistol man placed by it must lose hit points within a few game hours.
    /// </summary>
    private void TestFortification()
    {
        var human = Teams.GetCurrentPlayerTeam();
        var enemy = Teams.TeamsInGame.Values.FirstOrDefault(t => t != human && t.Org is { Eliminated: false });
        var row = OriginalBusinessTable.Instance.Entries.FirstOrDefault(r => r.Value28 == 7);
        var company = Companies().Where(c => c.IsEconomic && c.OwnerTeam == null && !c.Busy && c.Block != null)
            .OrderBy(c => c.Cell.X).FirstOrDefault();
        if (human?.Org == null || enemy == null || row == null || company == null)
        {
            Note("no fortification check");
            return;
        }
        company.SetOwnerTeam(human);
        company.SetRow(row);
        var target = CampaignManager.Instance.CreateUnitNear(UnitCommon.UnitType.Pistol, enemy, company.Cell);
        if (target == null)
        {
            Note("fortification check: unit not created");
            return;
        }
        // On a cell the fortification can see (sight radius 4, buildings block the view).
        var grid = Vision.Build(company.Cell, Company.FortificationRange);
        var spot = Enumerable.Range(-3, 7).SelectMany(dx => Enumerable.Range(-3, 7).Select(dy => company.Cell + new Vector2I(dx, dy)))
            .Where(c => MoveMap.Walkable(c, false) && Vision.Visible(grid, company.Cell, Company.FortificationRange, c))
            .OrderBy(c => (c - company.Cell).LengthSquared()).Cast<Vector2I?>().FirstOrDefault();
        if (spot is { } cell)
            target.PlaceAt(target.Map.MapToLocal(cell));
        var full = target.CanBeAttacked.HP;
        _pending.Add((_hour + 6, "a fortification shoots enemy units", () =>
            !IsInstanceValid(target) || target.IsDead || target.CanBeAttacked.HP < full,
            () =>
            {
                var detail = $"{company} fortification {company.IsFortification}, target at "
                             + (IsInstanceValid(target) ? $"{target.Cell} HP {target.CanBeAttacked.HP}/{full}" : "gone");
                // Done: back to an unowned legal company, so it doesn't shoot other checks' units (collectors).
                if (IsInstanceValid(company) && company.OwnerTeam != null)
                    company.ReleaseToNeutral();
                return detail;
            }));
    }

    private void KillACollector()
    {
        var human = Teams.GetCurrentPlayerTeam();
        var collector = Units().FirstOrDefault(u => u.UnitType == UnitCommon.UnitType.Collector && u.CarriedMoney > 0
                                                     && u.OwnerTeam != null && u.OwnerTeam != human && !u.InsideBuilding);
        var attacker = Units().FirstOrDefault(u => u.OwnerTeam == human && u.CanAttack != null)?.CanAttack;
        if (collector == null || attacker == null || human?.Org == null)
        {
            Note("no enemy collector with money to kill");
            return;
        }
        var carried = collector.CarriedMoney;
        var before = human.Org.TotalIn + human.Org.TotalBribed;
        for (var i = 0; i < 50 && !collector.IsDead; i++)
            collector.CanBeAttacked.ReceiveDamage(collector.CanBeAttacked.HP, attacker);
        Check("a killed collector's money goes to the killer",
            collector.IsDead && human.Org.TotalIn + human.Org.TotalBribed == before + carried,
            $"carried {carried}, killer got {human.Org.TotalIn + human.Org.TotalBribed - before}, dead {collector.IsDead}");
    }

    private static (Company Company, HqServices.MenuAction Action) Pick(IEnumerable<Company> targets, Team human, int slot)
    {
        foreach (var c in targets)
            if (CompanyServices.Menu(human, c).FirstOrDefault(m => m.Slot == slot) is { } action)
                return (c, action);
        return (null, null);
    }

    /// <summary>Destroys every computer player's HQ (or eliminates one without an HQ); the local player must win.</summary>
    private void DestroyComputerPlayers()
    {
        var human = Teams.GetCurrentPlayerTeam();
        var others = Teams.TeamsInGame.Values.Where(t => t != human && t.Org is { Eliminated: false }).ToList();
        if (others.Count == 0 || human?.Org == null || human.Org.Eliminated)
        {
            End($"time up on day {_days} (no opponent to destroy)");
            return;
        }
        var attacker = Units().FirstOrDefault(u => u.OwnerTeam == human && u.CanAttack != null)?.CanAttack;
        foreach (var t in others)
        {
            if (_ended)
                return;
            var hq = t.HqCompany;
            if (hq?.CanBeAttacked != null && IsInstanceValid(hq))
                // Repeat: any hit has a 1 % "lucky miss".
                for (var i = 0; i < 20 && !t.Org.Eliminated && IsInstanceValid(hq) && hq.Hp > 0; i++)
                    hq.CanBeAttacked.ReceiveDamage(hq.Hp, attacker);
            else
                t.Org.Eliminate();
            Check("a destroyed HQ eliminates its owner", t.Org.Eliminated, t.PlayerName);
            RunChecks();
        }
        if (!_ended)
        {
            Check("the last player standing wins", false, "no win after every opponent was eliminated");
            End($"day {_days}: no win after destroying the opponents");
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private IEnumerable<Unit> Units() =>
        GetTree().GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => IsInstanceValid(u) && !u.IsDead);

    private IEnumerable<Company> Companies() =>
        GetTree().GetNodesInGroup(Company.GroupName).OfType<Company>().Where(IsInstanceValid);

    private void Check(string name, bool ok, string detail)
    {
        _checked.Add(name);
        if (ok || !_failed.Add(name))
            return;
        _failures++;
        GD.PrintErr($"FAIL episode {_episode}, {GameClock.Format(_clock.Minute)}: {name}: {detail}");
    }

    private void Note(string text)
    {
        _notes.Add(text);
        GD.Print($"note: episode {_episode}: {text}");
    }
}
