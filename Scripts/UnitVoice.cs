namespace Capone;

/// <summary>
/// Order acknowledgements (unit vt sub_42CEF8): kind 1 = attack ("…ClickEnemy"), 2 = go ("…Order", forced
/// "…OrderHurry"), otherwise selection ("…Click"); only for the local player's units, not collectors. Mappings by
/// unit type from the switch tables (the Hit man answers an attack with the Bombman voice; the Cadillac has its own
/// CarGo / CarClick; anything else falls back to the Thug).
/// </summary>
public static class UnitVoice
{
    public enum Kind { Select, Go, GoHurry, Attack }

    public static void Play(Unit unit, Kind kind)
    {
        if (unit == null || unit.UnitType == UnitCommon.UnitType.Collector || unit.OwnerTeam?.IsCurrentPlayerColor != true)
            return;
        Sound.Play(File(unit.UnitType, kind));
    }

    public static string File(UnitCommon.UnitType type, Kind kind) => kind switch
    {
        Kind.Attack => type switch
        {
            UnitCommon.UnitType.Pistol => "PistolClickEnemy.wav",
            UnitCommon.UnitType.Shotgun => "ShotgunClickEnemy.wav",
            UnitCommon.UnitType.Sniper => "SniperClickEnemy.wav",
            UnitCommon.UnitType.Thompson => "ThompsonClickEnemy.wav",
            UnitCommon.UnitType.Terrorist or UnitCommon.UnitType.Hitman => "BombmanClickEnemy.wav",
            UnitCommon.UnitType.Godfather => "GodfatherClickEnemy.wav",
            UnitCommon.UnitType.Thief => "ThiefClickEnemy.wav",
            _ => "ThugClickEnemy.wav",
        },
        Kind.GoHurry => type switch
        {
            UnitCommon.UnitType.Sniper => "SniperOrderHurry.wav",
            UnitCommon.UnitType.Hitman => "HitmanOrderHurry.wav",
            UnitCommon.UnitType.Terrorist => "BombmanOrderHurry.wav",
            UnitCommon.UnitType.Thompson => "ThompsonOrderHurry.wav",
            UnitCommon.UnitType.Pistol => "PistolOrderHurry.wav",
            UnitCommon.UnitType.Shotgun => "ShotgunOrderHurry.wav",
            UnitCommon.UnitType.Thief => "ThiefOrderHurry.wav",
            _ => "ThugOrderHurry.wav",
        },
        Kind.Go => Named(type, "Order.wav", "CarGo.wav"),
        _ => Named(type, "Click.wav", "CarClick.wav"),
    };

    private static string Named(UnitCommon.UnitType type, string suffix, string car) => type switch
    {
        UnitCommon.UnitType.Terrorist => "Bombman" + suffix,
        UnitCommon.UnitType.Pistol => "Pistol" + suffix,
        UnitCommon.UnitType.Shotgun => "Shotgun" + suffix,
        UnitCommon.UnitType.Sniper => "Sniper" + suffix,
        UnitCommon.UnitType.Hitman => "Hitman" + suffix,
        UnitCommon.UnitType.Thompson => "Thompson" + suffix,
        UnitCommon.UnitType.Cadillac => car,
        UnitCommon.UnitType.Godfather => "Godfather" + suffix,
        UnitCommon.UnitType.Thief => "Thief" + suffix,
        _ => "Thug" + suffix,
    };
}
