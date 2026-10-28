namespace Capone;

/// <summary>
/// Disguise and exposure (unit fields +0D0h last shot, +0D4h disguise start; constructor sub_42A150 sets them to 0 and
/// −3601). Any own unit but a Collector or one in a car may Disguise (order 0Ah, text 29,10, gate sub_42CCE5): the owner
/// pays authority service 12h (FBI: 1 500 000 bribed, 100 favour points), at most once per 60 game hours (handler
/// sub_42C503). A disguised unit is not "exposed" (vt+19Ch sub_42B3C0) for 60 game hours, unless it fired within the
/// last 30 hours; the Hit man is hidden without the order whenever he hasn't fired for 30 hours. Units only pick
/// exposed enemies by themselves (unit AI); the player may still order an attack. While hidden the Hit man's shot at a
/// unit does the table damage (161–162), once exposed 30 (sub_42C890 / sub_42C960).
/// </summary>
public partial class Unit
{
    public const int ExposedAfterShotMinutes = 30 * GameClock.MinutesPerHour;
    public const int DisguiseMinutes = 60 * GameClock.MinutesPerHour;
    /// <summary>Authority service 12h (table 0x488268): FBI, 1 500 000, 100 points.</summary>
    public const int DisguiseService = 0x12;
    /// <summary>The exposed Hit man's damage against units (sub_42C890 / sub_42C960 return 1Eh).</summary>
    public const int ExposedHitmanDamage = 30;

    /// <summary>+0D0h: game minute of the last shot (Unit::Attack sub_42BD15).</summary>
    public long LastShotMinute { get; set; }

    /// <summary>+0D4h: game minute the last Disguise started.</summary>
    public long DisguiseMinute { get; set; } = -DisguiseMinutes - 1;

    private static long Now => GameClock.Instance?.Minute ?? 0;

    private bool ShotRecently => LastShotMinute + ExposedAfterShotMinutes >= Now;

    /// <summary>vt+19Ch (sub_42B3C0): may other units pick this one as a target by themselves?</summary>
    public bool IsExposed => UnitType == UnitCommon.UnitType.Hitman
        ? LastShotMinute + ExposedAfterShotMinutes > Now
        : Now >= DisguiseMinute + DisguiseMinutes || ShotRecently;

    /// <summary>vt+1D8h (sub_42EC08): disguised by the order; the panel adds " (Disguised)" (5,12).</summary>
    public bool IsDisguised => Now < DisguiseMinute + DisguiseMinutes && !ShotRecently;

    /// <summary>Order 0Ah gate (sub_42CCE5).</summary>
    public bool CanDisguise =>
        !IsDead && UnitType != UnitCommon.UnitType.Collector && OwnerTeam?.Org?.CanUseItem(DisguiseService) == true
        && Now > DisguiseMinute + DisguiseMinutes;

    /// <summary>Order 0Ah (sub_42C503): pay the service and start the disguise.</summary>
    public bool Disguise()
    {
        if (!CanDisguise || OwnerTeam.Org.UseItem(DisguiseService) != true)
            return false;
        DisguiseMinute = Now;
        Godot.GD.Print($"Unit {DisplayName} ({UnitType}) disguised");
        return true;
    }

    /// <summary>Called on every shot (sets +0D0h).</summary>
    public void MarkShot() => LastShotMinute = Now;
}

/// <summary>
/// Commands on another player's unit (menu sub_42D2C1, gate sub_42CCE5, handler sub_42C503). Order 0Bh "Ask cops to
/// arrest gangster" (29,11, slot 0, icon 0xAD8): the local player pays a police service by the gangster's type — Thug
/// and Pistol man 2Ch (500 000 bribed, 30 points), Shotgun / Terrorist / Thompson 31h (2 000 000, 40); any type is
/// covered by 34h (4 000 000, 60 points; only checked, not paid, as in the EXE) — and a police car is sent after him
/// (sub_4460C5 with 1: the owner's lawyers can't help). Order 0Ch "Pay snitch to switch sides ($200.000)" (29,12) needs
/// unit +0D8h = 2 000 000, set by the FBI snitch service, which isn't ported yet.
/// </summary>
public partial class Unit
{
    public const int ArrestServiceLow = 0x2C, ArrestServiceMid = 0x31, ArrestServiceHigh = 0x34;

    private bool LowTierGangster => UnitType is UnitCommon.UnitType.Thug or UnitCommon.UnitType.Pistol;

    private bool MidTierGangster =>
        UnitType is UnitCommon.UnitType.Shotgun or UnitCommon.UnitType.Terrorist or UnitCommon.UnitType.Thompson;

    /// <summary>Order 0Bh gate for <paramref name="team"/> (not this unit's owner).</summary>
    public bool CanBeReportedBy(Team team)
    {
        var org = team?.Org;
        if (org == null || OwnerTeam == team || IsDead || IsArrested || UnitType is UnitCommon.UnitType.Collector
                or UnitCommon.UnitType.PoliceCar or UnitCommon.UnitType.Policeman)
            return false;
        return (LowTierGangster && org.CanUseItem(ArrestServiceLow))
               || (MidTierGangster && org.CanUseItem(ArrestServiceMid))
               || org.CanUseItem(ArrestServiceHigh);
    }

    /// <summary>Order 0Bh: pay the service and send the police after this unit.</summary>
    public bool ReportToPolice(Team team)
    {
        if (!CanBeReportedBy(team))
            return false;
        var org = team.Org;
        var paid = (LowTierGangster && org.UseItem(ArrestServiceLow))
                   || (MidTierGangster && org.UseItem(ArrestServiceMid))
                   || org.CanUseItem(ArrestServiceHigh);
        if (!paid)
            return false;
        Godot.GD.Print($"Unit {DisplayName} ({UnitType}) reported to the police by {team.PlayerName}");
        Police.Instance?.Dispatch(this, lawyersCanHelp: false);
        return true;
    }
}
