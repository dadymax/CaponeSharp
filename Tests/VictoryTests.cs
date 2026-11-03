using System.Linq;

namespace Capone.Tests;

/// <summary>Episode objectives (VCOND, read by sub_4670A5; IsMet virtuals sub_463610 …).</summary>
public static class VictoryTests
{
    private static VictoryCondition Make(int type, params long[] values)
    {
        var source = new Scenario.EpiVictoryCondition { ConditionType = type, TargetPlayer = 1 };
        source.Values.AddRange(values);
        return new VictoryCondition(source);
    }

    [UnitTest]
    public static void MoneyObjectiveIsStrictlyGreater()
    {
        // sub_463610: weekly net income (+14h) > target, strictly.
        var vc = Make(1, 300_000);
        vc.UpdateMoneyProgress(300_000);
        Assert.True(!vc.IsCompleted, "equal to the target: not yet");
        vc.UpdateMoneyProgress(300_001);
        Assert.True(vc.IsCompleted, "above the target: met");
    }

    [UnitTest]
    public static void ObjectiveValuesAreRead()
    {
        var count = Make(2, 3, 2);
        Assert.Equal(3L, count.RequiredProgress, "VCOND 2 count");
        Assert.Equal(2, count.CompanyType, "VCOND 2 business type");
        var reach = Make(7, 40, 50, 5, VictoryCondition.AnyUnitType);
        Assert.Equal(new Godot.Vector2I(40, 50), reach.TargetCoords, "VCOND 7 cell");
        Assert.Equal(5, reach.TargetSize, "VCOND 7 radius");
        Assert.Equal(VictoryCondition.AnyUnitType, reach.OriginalUnitType, "VCOND 7 any unit");
        var area = Make(8, 10, 20, 30, 40, VictoryCondition.AnyBusinessType, 4);
        Assert.Equal(new Godot.Vector2I(10, 20), area.TopLeft, "VCOND 8 corner");
        Assert.Equal(new Godot.Vector2I(30, 40), area.BottomRight, "VCOND 8 corner");
        Assert.Equal(4L, area.RequiredProgress, "VCOND 8 count");
        var district = Make(6, 2);
        district.UpdateCompanyCountProgress(1);
        Assert.True(district.IsCompleted, "VCOND 6: one business in the district");
    }

    [UnitTest]
    public static void EveryEpisodeObjectiveIsWellFormed()
    {
        var parser = new EpiParser();
        var files = CampaignManager.FindEpisodeFiles();
        Assert.True(files.Count == 21, $"21 episodes ({files.Count})");
        foreach (var path in files)
        {
            var scenario = parser.Parse(path);
            var name = System.IO.Path.GetFileName(path);
            // Without a VCOND (ep 7) the episode is won as the last player standing (sub_43B711): it needs an opponent.
            Assert.True(scenario.VictoryConditions.Count > 0 || scenario.Players.Count > 1, $"{name}: has an objective or an opponent");
            foreach (var source in scenario.VictoryConditions)
            {
                var vc = new VictoryCondition(source);
                Assert.True(source.ConditionType is >= 1 and <= 8, $"{name}: VCOND {source.ConditionType} is used by the EXE");
                Assert.True(scenario.Players.Any(p => p.Id == vc.TargetPlayer), $"{name}: VCOND {source.ConditionType} player {vc.TargetPlayer}");
                switch (vc.Type)
                {
                    case VictoryCondition.ConditionType.Money:
                        Assert.True(vc.MoneyTarget > 0, $"{name}: money target {vc.MoneyTarget}");
                        break;
                    case VictoryCondition.ConditionType.UnitKill or VictoryCondition.ConditionType.UnitCreate:
                        Assert.True(vc.UnitType != null, $"{name}: unit type {vc.OriginalUnitType}");
                        break;
                    case VictoryCondition.ConditionType.UnitReach:
                        Assert.True(vc.UnitType != null || vc.OriginalUnitType == VictoryCondition.AnyUnitType,
                            $"{name}: VCOND 7 unit type {vc.OriginalUnitType}");
                        break;
                    case VictoryCondition.ConditionType.CompanyCountType:
                        Assert.True(vc.District is >= 0 and <= 2, $"{name}: district {vc.District}");
                        break;
                    case VictoryCondition.ConditionType.CaptureArea:
                        Assert.True(vc.TopLeft.X <= vc.BottomRight.X && vc.TopLeft.Y <= vc.BottomRight.Y && vc.RequiredProgress > 0,
                            $"{name}: VCOND 8 {vc.TopLeft}–{vc.BottomRight} × {vc.RequiredProgress}");
                        break;
                }
            }
        }
    }
}
