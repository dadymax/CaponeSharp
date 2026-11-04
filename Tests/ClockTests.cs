using System.Collections.Generic;

namespace Capone.Tests;

/// <summary>Named, saveable clock events (save / load step 1).</summary>
public static class SaveableClockTests
{
    private sealed class Probe : IScheduled
    {
        public readonly List<string> Fired = new();
        public string SaveId => "probe";
        public void OnClockEvent(string kind, long arg) => Fired.Add($"{kind}:{arg}");
    }

    [UnitTest]
    public static void NamedEventsRunInOrderAndCanBeListed()
    {
        var clock = new GameClock();
        try
        {
            var probe = new Probe();
            clock.ScheduleIn(120, probe, "b", 2);
            clock.ScheduleIn(60, probe, "a", 1);
            clock.ScheduleIn(60, probe, "a2", 3);
            var pending = clock.SnapshotEvents();
            Assert.Equal(3, pending.Count, "three pending events");
            Assert.Equal("a", pending[0].Kind, "earliest first");
            Assert.Equal("a2", pending[1].Kind, "same minute: in scheduling order");
            Assert.Equal("probe", pending[2].Target, "target saved by id");
            Assert.Equal(0, clock.ClosureEvents, "no closures");
            clock._Process(1.0); // 60 game minutes
            Assert.Equal("a:1,a2:3", string.Join(",", probe.Fired), "the 60-minute events ran with their numbers");
            Assert.Equal(1, clock.SnapshotEvents().Count, "one left");
        }
        finally
        {
            clock.Free();
        }
    }
}
