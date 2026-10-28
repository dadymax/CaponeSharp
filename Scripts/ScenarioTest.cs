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
    public static readonly int[] DefaultEpisodes = { 1, 4, 8, 13, 15 };
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
        foreach (var c in Companies())
        {
            if (c.IsHq)
                _hqOwners[c] = c.OwnerTeam;
            _owners[c] = c.OwnerTeam;
        }
        _clock.TurnPassed += OnTurn;
        var players = string.Join(", ", Teams.TeamsInGame.Values.Where(t => t.Org != null)
            .Select(t => $"{t.PlayerName}{(t.IsCurrentPlayerColor ? " (local)" : t.Ai != null ? " (AI)" : "")}"));
        GD.Print($"--- Episode {_episode}: {players}");
    }

    public override void _ExitTree()
    {
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
        if (_hour == 6)
            MakeDeserter();
        if (_hour == System.Math.Min(24, _days * 12))
            UseAuthorityServices();
        if (_hour >= _days * 24)
            DestroyComputerPlayers();
    }

    /// <summary>The end screen would show (Main.ShowEndScreen): record the result and go to the next episode.</summary>
    public void EpisodeEnded(bool won, string text)
    {
        if (_ended)
            return;
        RunChecks();
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
