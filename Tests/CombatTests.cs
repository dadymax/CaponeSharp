using Godot;

namespace Capone.Tests;

/// <summary>Attack timing from the unit table (Unit::Attack sub_42BD15, vt+1B4h / 1B8h / 1BCh).</summary>
public static class CombatTests
{
    private static float Rate(UnitCommon.UnitType type)
    {
        var attack = new CanAttack();
        attack.ApplyOriginalStats(OriginalUnitTable.Instance.Get((int)type));
        var rate = attack.AttackRate;
        attack.Free();
        return rate;
    }

    [UnitTest]
    public static void ShotsPerGameHourComeFromTheTable()
    {
        Assert.Near(1, Rate(UnitCommon.UnitType.Thug), "Thug: every think");
        Assert.Near(1, Rate(UnitCommon.UnitType.Thompson), "Thompson: rate 6, still one shot per think");
        Assert.Near(4, Rate(UnitCommon.UnitType.Sniper), "Sniper: rate 0.24, aims 4 thinks");
        Assert.Near(5, Rate(UnitCommon.UnitType.Shotgun), "Shotgun: 300 minutes between shots");
        Assert.Equal(4, CanAttack.AimTicks(0.24f), "AimTicks(0.24)");
        Assert.Equal(1, CanAttack.AimTicks(6f), "AimTicks(6)");
    }

    [UnitTest]
    public static void OnlyTheThompsonManShootsFromACar()
    {
        foreach (var type in System.Enum.GetValues<UnitCommon.UnitType>())
        {
            var stats = OriginalUnitTable.Instance.Get((int)type);
            if (stats != null)
                Assert.Equal(type == UnitCommon.UnitType.Thompson, stats.FiresFromCar != 0, $"{type}");
        }
    }
}
