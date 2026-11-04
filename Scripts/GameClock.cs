using System;
using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// An object that receives named clock events (save / load step 1, TODO item 20): an event is stored as data — the
/// target's <see cref="SaveId"/>, an event kind and one number — so the pending events can be written to a save
/// file and rebuilt. <see cref="OnClockEvent"/> runs the event.
/// </summary>
public interface IScheduled
{
    /// <summary>Stable id of the object in a save file ("company:12", "unit:7", "org:User"…).</summary>
    string SaveId { get; }

    void OnClockEvent(string kind, long arg);
}

/// <summary>Unique save ids for objects that don't have a natural one (units, jobs).</summary>
public static class SaveIds
{
    private static long _next;

    public static string Next(string prefix) => $"{prefix}:{++_next}";

    /// <summary>New episode: ids start again (a loaded game sets the counter past its highest id).</summary>
    public static void Reset(long next = 0) => _next = next;

    public static long Current => _next;
}

/// <summary>
/// Game clock and event scheduler, modelled on the original (see PLAN.md R1):
/// <list type="bullet">
/// <item>Game time is counted in <b>minutes</b> (World +166h). Objects schedule events at a game minute
/// (World::Schedule <c>sub_43AF10</c>); the world runs them in time order (<c>sub_43ABD0</c>).</item>
/// <item>The game advances in 60-minute turns (<c>sub_416790</c> = 60, <c>sub_429870</c>); a turn may start at most
/// every 1000 ms of real time (World +16Ah). So at normal speed <b>one game hour = one real second</b>,
/// a day = 24 s, a week (10080 min, <c>sub_402CD0</c>) = 168 s, and a 60-minutes-per-tile unit walks 1 tile/s.</item>
/// </list>
/// <see cref="Speed"/> multiplies everything (it also sets <see cref="Engine.TimeScale"/> so movement, attack timers and
/// animations follow). Set it with <c>-- --speed=N</c>, from the MCP tools (<c>/root/GameClock</c> property <c>Speed</c>),
/// or with the <c>-</c> / <c>=</c> keys.
/// </summary>
public partial class GameClock : Node
{
    public const int MinutesPerHour = 60;
    public const int MinutesPerDay = 24 * MinutesPerHour;
    /// <summary>One week, the collector dispatch interval (sub_402CD0).</summary>
    public const int MinutesPerWeek = 7 * MinutesPerDay;
    /// <summary>Length of a turn in game minutes.</summary>
    public const int TurnMinutes = 60;
    /// <summary>Game minutes per real second at speed 1 (one 60-minute turn per 1000 ms).</summary>
    public const double MinutesPerRealSecond = 60.0;

    public const float MinSpeed = 0.25f;
    public const float MaxSpeed = 64f;

    public static GameClock Instance { get; private set; }

    [Signal] public delegate void TurnPassedEventHandler(long turn);
    [Signal] public delegate void SpeedChangedEventHandler(float speed);

    private float _speed = 1f;

    /// <summary>Game speed multiplier (1 = original speed).</summary>
    [Export]
    public float Speed
    {
        get => _speed;
        set
        {
            _speed = Mathf.Clamp(value, MinSpeed, MaxSpeed);
            Engine.TimeScale = _speed;
            EmitSignal(SignalName.SpeedChanged, _speed);
        }
    }

    /// <summary>When true the clock stops (scheduled events wait). Units keep their own pause state.</summary>
    [Export] public bool Paused { get; set; }

    /// <summary>Exact game time in minutes since the start of the scenario.</summary>
    public double Minutes { get; private set; }

    /// <summary>Current game minute (what the original keeps in World +166h).</summary>
    public long Minute => (long)Minutes;

    /// <summary>Number of whole turns (hours) since the start.</summary>
    public long Turn => Minute / TurnMinutes;

    /// <summary>Scheduled events that threw (checked by the scenario test).</summary>
    public static int EventErrors { get; private set; }

    /// <summary>A pending event: a named one (target + kind + arg, saveable) or, in tests only, a closure.</summary>
    private readonly struct Pending
    {
        public readonly IScheduled Target;
        public readonly string Kind;
        public readonly long Arg;
        public readonly Action Action;

        public Pending(IScheduled target, string kind, long arg)
        {
            Target = target;
            Kind = kind;
            Arg = arg;
            Action = null;
        }

        public Pending(Action action)
        {
            Target = null;
            Kind = null;
            Arg = 0;
            Action = action;
        }
    }

    private readonly PriorityQueue<Pending, (long minute, long seq)> _events = new();
    private long _sequence;
    private long _lastTurn;

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Pausable;
    }

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--speed=") && float.TryParse(arg["--speed=".Length..],
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
                Speed = s;
        if (Engine.TimeScale != _speed)
            Engine.TimeScale = _speed;
    }

    /// <summary>Restart the clock at minute 0 and drop all scheduled events (new scenario). The end of the previous
    /// episode paused the clock; the new one starts running.</summary>
    public void Reset()
    {
        Paused = false;
        Minutes = 0;
        _lastTurn = 0;
        _events.Clear();
        _sequence = 0;
    }

    /// <summary>
    /// Run <paramref name="action"/> when the clock reaches <paramref name="minute"/>. Closures can't be saved: game
    /// code uses <see cref="Schedule"/>; this is for tests and debug commands (<see cref="ClosureEvents"/> counts them).
    /// </summary>
    public void ScheduleAt(long minute, Action action) => _events.Enqueue(new Pending(action), (Math.Max(minute, Minute), _sequence++));

    /// <summary>Run <paramref name="action"/> after <paramref name="minutes"/> game minutes (tests / debug only).</summary>
    public void ScheduleIn(long minutes, Action action) => ScheduleAt(Minute + Math.Max(0, minutes), action);

    /// <summary>Named event: <paramref name="target"/>.OnClockEvent(kind, arg) at <paramref name="minute"/> (now if it passed).</summary>
    public void Schedule(long minute, IScheduled target, string kind, long arg = 0) =>
        _events.Enqueue(new Pending(target, kind, arg), (Math.Max(minute, Minute), _sequence++));

    /// <summary>Named event after <paramref name="minutes"/> game minutes.</summary>
    public void ScheduleIn(long minutes, IScheduled target, string kind, long arg = 0) =>
        Schedule(Minute + Math.Max(0, minutes), target, kind, arg);

    /// <summary>Pending closure events (should be none in a game: they can't be saved).</summary>
    public int ClosureEvents
    {
        get
        {
            var n = 0;
            foreach (var (pending, _) in _events.UnorderedItems)
                if (pending.Action != null)
                    n++;
            return n;
        }
    }

    /// <summary>The pending named events in time order, for a save file (targets that are gone are left out).</summary>
    public List<(long Minute, long Seq, string Target, string Kind, long Arg)> SnapshotEvents()
    {
        var list = new List<(long, long, string, string, long)>();
        foreach (var (pending, when) in _events.UnorderedItems)
            if (pending.Target != null && TargetAlive(pending.Target))
                list.Add((when.minute, when.seq, pending.Target.SaveId, pending.Kind, pending.Arg));
        list.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.CompareTo(b.Item2));
        return list;
    }

    /// <summary>Kinds of pending named events per target type (for the scenario test and debugging).</summary>
    public Dictionary<string, int> EventCounts()
    {
        var counts = new Dictionary<string, int>();
        foreach (var (pending, _) in _events.UnorderedItems)
        {
            var key = pending.Target == null ? "closure" : $"{pending.Target.GetType().Name}.{pending.Kind}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts;
    }

    private static bool TargetAlive(IScheduled target) => target is not GodotObject o || IsInstanceValid(o);

    /// <summary>Real seconds (at the current speed) that <paramref name="minutes"/> game minutes take.</summary>
    public double RealSecondsFor(double minutes) => minutes / MinutesPerRealSecond / _speed;

    public override void _Process(double delta)
    {
        if (Paused)
            return;
        // delta is already multiplied by Engine.TimeScale (= Speed).
        Minutes += delta * MinutesPerRealSecond;

        // Events in time order. An event may pause the clock (scenario won), which stops the rest.
        var now = Minute;
        while (!Paused && _events.TryPeek(out _, out var when) && when.minute <= now)
        {
            var pending = _events.Dequeue();
            try
            {
                if (pending.Action != null)
                    pending.Action();
                else if (TargetAlive(pending.Target))
                    pending.Target.OnClockEvent(pending.Kind, pending.Arg);
            }
            catch (Exception e)
            {
                EventErrors++;
                GD.PushError($"GameClock: event at minute {when.minute} failed: {e}");
            }
        }

        var turn = Turn;
        while (_lastTurn < turn)
            EmitSignal(SignalName.TurnPassed, ++_lastTurn);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.Keycode)
        {
            case Key.Equal or Key.KpAdd:
                Speed *= 2;
                GD.Print($"GameClock: speed x{Speed}");
                break;
            case Key.Minus or Key.KpSubtract:
                Speed /= 2;
                GD.Print($"GameClock: speed x{Speed}");
                break;
        }
    }

    /// <summary>"day D, HH:MM" for logs and debug UI.</summary>
    public static string Format(long minute) => $"day {minute / MinutesPerDay + 1}, {minute % MinutesPerDay / 60:00}:{minute % 60:00}";
}
