using System.Collections.Generic;
using Godot;

namespace Capone.Tests;

/// <summary>The end screen statistics (sub_460BE4 sampling, sub_43CCD0 chart, sub_43DB50 tabs).</summary>
public static class StatisticsTests
{
    private static Statistics Two() =>
        new(new[] { new Team(Colors.Red) { PlayerName = "me" }, new Team(Colors.Blue) { PlayerName = "ai" } }, 200_000);

    private static void Sample(Statistics s, long a, long b) =>
        s.Sample(new List<(long, long, long)> { (a, a * 10, a), (b, b * 10, b) });

    [UnitTest]
    public static void OneSamplePerSlotAtFirst()
    {
        var s = Two();
        Sample(s, 1, 100);
        Sample(s, 2, 200);
        Assert.Equal(2, s.Count, "complete slots");
        Assert.Equal(2L, s.Value(Statistics.NetIncome, 0, 1), "player 0 slot 1");
        Assert.Equal(2000L, s.Value(Statistics.Cash, 1, 1), "player 1 cash slot 1");
    }

    [UnitTest]
    public static void FullSlotsAreHalvedAndThePeriodDoubles()
    {
        var s = Two();
        for (var i = 0; i < Statistics.Slots; i++)
            Sample(s, i * 10, 0);
        Assert.Equal(31, s.Count, "all slots full");
        Sample(s, 1000, 0); // compresses first, then starts slot 15 again with the old period's weight
        // Slot 15 already counted the old slot 30 as one period (1 sample), so this sample completes it.
        Assert.Equal(16, s.Count, "slots after compression and the new sample");
        Assert.Equal(2, s.Period, "period doubled");
        Assert.Equal(5L, s.Value(Statistics.NetIncome, 0, 0), "(0 + 10) / 2");
        Assert.Equal(285L, s.Value(Statistics.NetIncome, 0, 14), "(280 + 290) / 2");
        Assert.Equal((300L + 1000) / 2, s.Value(Statistics.NetIncome, 0, 15), "old slot 30 averaged with the new sample");
        Sample(s, 0, 0);
        Assert.Equal(16, s.Count, "slot 16 needs two samples now");
    }

    [UnitTest]
    public static void SlotsAverageTheirSamples()
    {
        var s = Two();
        for (var i = 0; i < Statistics.Slots + 1; i++)
            Sample(s, 0, 0);
        // Period 2 now: slot 16 takes two samples.
        Sample(s, 10, 0);
        Assert.Equal(16, s.Count, "slot 16 not complete after one sample");
        Sample(s, 21, 0);
        Assert.Equal(17, s.Count, "complete after two");
        Assert.Equal(15L, s.Value(Statistics.NetIncome, 0, 16), "(10 + 21) / 2");
    }

    [UnitTest]
    public static void AxisRangeStartsFromTheOriginalDefaults()
    {
        var s = Two();
        Assert.Equal((-90_000L, 0L), EndScreen.Range(s, 0), "income");
        Assert.Equal((0L, 200_000L), EndScreen.Range(s, 1), "money: starting funds");
        Assert.Equal((0L, 5L), EndScreen.Range(s, 2), "people");
        Sample(s, 7, 300_000);
        Assert.Equal((0L, 3_000_000L), EndScreen.Range(s, 1), "widened by a sample");
        Assert.Equal((0L, 300_000L), EndScreen.Range(s, 2), "people widened");
        Assert.Equal((0L, 5L), EndScreen.Range(null, 2), "no statistics");
    }

    [UnitTest]
    public static void ChartYIsClamped()
    {
        Assert.Near(465, EndScreen.Y(0, 0, 100), "min at the bottom");
        Assert.Near(185, EndScreen.Y(100, 0, 100), "max at the top");
        Assert.Near(325, EndScreen.Y(50, 0, 100), "middle");
        Assert.Near(185, EndScreen.Y(500, 0, 100), "above: clamped");
        Assert.Near(465, EndScreen.Y(-500, 0, 100), "below: clamped");
    }

    [UnitTest]
    public static void TabStripHitTest()
    {
        Assert.Equal(1, EndScreen.TabAt(0, 150, 150), "income -> money");
        Assert.Equal(2, EndScreen.TabAt(0, 250, 150), "income -> people");
        Assert.Equal(0, EndScreen.TabAt(1, 80, 150), "money -> income");
        Assert.Equal(1, EndScreen.TabAt(2, 150, 150), "people -> money");
        Assert.Equal(0, EndScreen.TabAt(0, 250, 140), "above the strip");
        Assert.Equal(2, EndScreen.TabAt(2, 45, 150), "x 45 is outside people's income range (50..122)");
    }
}
