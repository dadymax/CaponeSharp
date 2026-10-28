using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Authority services bought through an HQ (HQ building menu sub_4152F0, commands sub_415FF0, HQ object methods
/// vt+FCh…11Ch sub_4325C7…sub_432A61). On another player's HQ: FBI "talk" to the boss (service 11h), Mayor
/// investigations (16h), an evil rumour (1Ah), a snitch rumour (20h), announce the player's death (22h), ask FBI for a
/// snitch (0Eh). On the own HQ: Mayor bribing investigations (17h) and the legalization of business types 1–3 (1Bh,
/// 1Eh, 21h by level).
/// </summary>
public partial class Organisation
{
    public const float RumourFactor = 0.7f, InvestigationFactor = 0.85f;
    /// <summary>Rumours and investigations last two weeks (2 × sub_402CD0 = 2 × 10080 minutes).</summary>
    public const int RumourMinutes = 2 * GameClock.MinutesPerWeek;
    /// <summary>The FBI visit lasts 30 game hours (sub_432650).</summary>
    public const int FbiVisitMinutes = 30 * GameClock.MinutesPerHour;
    /// <summary>dbl_488850: bribing investigations keep 75 % of the money bribed to the army, FBI and police.</summary>
    public const double BribingInvestigationKeeps = 0.75;

    private static long ClockNow => GameClock.Instance?.Minute ?? 0;

    private bool _fbiShown;

    /// <summary>+128h / +124h / +120h: start minutes (never: far in the past).</summary>
    public long RumourStart { get; private set; } = long.MinValue / 2;
    public long InvestigationStart { get; private set; } = long.MinValue / 2;
    public long FbiVisitStart { get; private set; } = long.MinValue / 2;

    /// <summary>vt+FCh (sub_4325C7).</summary>
    public bool RumourActive => ClockNow < RumourStart + RumourMinutes;
    /// <summary>vt+100h (sub_432608).</summary>
    public bool InvestigationActive => ClockNow < InvestigationStart + RumourMinutes;
    /// <summary>vt+104h (sub_432650): while the FBI visits the local player, their input is blocked (+0A40h).</summary>
    public bool FbiVisitActive => ClockNow < FbiVisitStart + FbiVisitMinutes;
    /// <summary>vt+F8h (sub_432691): 0 → 1 over the visit, for the progress bar.</summary>
    public float FbiVisitProgress => Mathf.Clamp((float)(ClockNow - FbiVisitStart) / FbiVisitMinutes, 0f, 1f);

    private static void News(string text)
    {
        Messages.Show(text);
        GD.Print("News: " + text);
    }

    private string Named(int group, int index) => PanelInfo.CFormat(OriginalTexts.Instance.Get(group, index), Team.PlayerName);

    /// <summary>vt+108h (sub_432723): "A rumour : %s blackmails his customers ..." (20,26).</summary>
    public void StartRumour()
    {
        News(Named(20, 26));
        RumourStart = ClockNow;
    }

    /// <summary>vt+10Ch (sub_432793): "Mayor declares that %s's affairs will be investigated ..." (20,27).</summary>
    public void StartInvestigation()
    {
        News(Named(20, 27));
        InvestigationStart = ClockNow;
    }

    /// <summary>vt+118h (sub_432A35).</summary>
    public void StartFbiVisit()
    {
        FbiVisitStart = ClockNow;
        if (Team.IsCurrentPlayerColor)
            News(OriginalTexts.Instance.Get(20, 2));
    }

    private List<Unit> Gangsters() =>
        (Engine.GetMainLoop() as SceneTree)?.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
        .Where(u => !u.IsDead && u.OwnerTeam == Team && u.UnitType != UnitCommon.UnitType.Collector).ToList()
        ?? new List<Unit>();

    /// <summary>
    /// vt+110h (sub_432800): "Former FBI agent reveals that %s used to be a snitch ..." (20,28); each gangster quits
    /// with a 30 % chance.
    /// </summary>
    public void SnitchRumour()
    {
        News(Named(20, 28));
        foreach (var unit in Gangsters().Where(_ => GD.Randi() % 100 < 30))
            unit.Resign();
    }

    /// <summary>
    /// vt+114h (sub_4328E7): "Mayor announces that notorious criminal %s died peacefully …" (20,29); each gangster quits
    /// with a 50 % chance and each company is lost with a 25 % chance.
    /// </summary>
    public void AnnounceDeath()
    {
        News(Named(20, 29));
        foreach (var unit in Gangsters().Where(_ => GD.Randi() % 100 < 50))
            unit.Resign();
        var companies = (Engine.GetMainLoop() as SceneTree)?.GetNodesInGroup(Company.GroupName).OfType<Company>()
            .Where(c => c.OwnerTeam == Team && c.IsEconomic).ToList() ?? new List<Company>();
        foreach (var company in companies)
            if (GD.Randi() % 100 < 25)
                company.ReleaseToNeutral();
    }

    /// <summary>vt+11Ch (sub_432A61): money bribed to every authority but the politician × 0.75.</summary>
    public void BribingInvestigation()
    {
        for (var i = 0; i < TypeCount; i++)
            if (i != 3)
                AuthorityMoney[i] = (long)(AuthorityMoney[i] * BribingInvestigationKeeps);
    }
}

/// <summary>The HQ menus and commands (sub_4152F0 / sub_415FF0).</summary>
public static class HqServices
{
    public const int FbiTalk = 0x11, Investigation = 0x16, EvilRumour = 0x1A, SnitchRumour = 0x20, AnnounceDeath = 0x22,
        FbiSnitch = 0x0E, BribingInvestigations = 0x17;
    /// <summary>Legalization services by current level (0 → 1, 1 → 2, 2 → 3).</summary>
    public static readonly int[] Legalize = { 0x1B, 0x1E, 0x21 };

    public record MenuAction(int Slot, int Icon, string Text, System.Func<bool> Run);

    /// <summary>Text 17,n with the service's points ("%ld … points"), or 17,n+1 for exactly one point.</summary>
    private static string PointsText(int pluralIndex, int service)
    {
        var points = (long)(AuthorityItems.Get(service)?.Points ?? 0);
        return points > 1
            ? PanelInfo.CFormat(OriginalTexts.Instance.Get(17, pluralIndex), points)
            : OriginalTexts.Instance.Get(17, pluralIndex + 1);
    }

    /// <summary>Buttons for <paramref name="player"/> on <paramref name="hq"/> (own or another player's HQ).</summary>
    public static List<MenuAction> Menu(Team player, Company hq)
    {
        var list = new List<MenuAction>();
        var org = player?.Org;
        var target = hq?.OwnerTeam;
        if (org == null || target?.Org == null || hq == null || !hq.IsHq)
            return list;
        if (target == player)
        {
            if (org.CanUseItem(BribingInvestigations))
                list.Add(new MenuAction(0, 0xDB7, PointsText(3, BribingInvestigations), () => Bribing(player)));
            int[] icons = { 0xADC, 0xAE2, 0xAE0 };
            for (var type = 1; type <= 3; type++)
            {
                var level = World.LegalizationLevel(type);
                if (level > 2 || !org.CanUseItem(Legalize[level]))
                    continue;
                var t = type;
                list.Add(new MenuAction(type, icons[type - 1], PointsText(5 + (type - 1) * 6 + level * 2, Legalize[level]),
                    () => Legalization(player, t)));
            }
            return list;
        }
        var victim = target.Org;
        if (org.CanUseItem(FbiTalk) && !victim.FbiVisitActive)
            list.Add(new MenuAction(0, 0xDB5, PointsText(25, FbiTalk), () => Use(org, FbiTalk, victim.StartFbiVisit)));
        if (org.CanUseItem(Investigation) && !victim.InvestigationActive)
            list.Add(new MenuAction(1, 0xDB7, PointsText(27, Investigation), () => Use(org, Investigation, victim.StartInvestigation)));
        if (org.CanUseItem(EvilRumour) && !victim.RumourActive)
            list.Add(new MenuAction(2, 0xDB7, PointsText(29, EvilRumour), () => Use(org, EvilRumour, victim.StartRumour)));
        if (org.CanUseItem(SnitchRumour))
            list.Add(new MenuAction(3, 0xDB7, PointsText(31, SnitchRumour), () => Use(org, SnitchRumour, victim.SnitchRumour)));
        if (org.CanUseItem(AnnounceDeath))
            list.Add(new MenuAction(4, 0xDB7, PointsText(33, AnnounceDeath), () => Use(org, AnnounceDeath, victim.AnnounceDeath)));
        if (org.CanUseItem(FbiSnitch))
            list.Add(new MenuAction(5, 0xDB5, PointsText(35, FbiSnitch), () => PlantSnitch(player, target)));
        return list;
    }

    private static bool Use(Organisation org, int service, System.Action effect)
    {
        if (!org.UseItem(service))
            return false;
        effect();
        return true;
    }

    /// <summary>Command 19h: "Mayor declares that FBI will start bribing investigations ..." (17,48), every player.</summary>
    private static bool Bribing(Team player)
    {
        if (!player.Org.UseItem(BribingInvestigations))
            return false;
        Messages.Show(OriginalTexts.Instance.Get(17, 48));
        foreach (var team in Teams.TeamsInGame.Values.Where(t => t.Org is { Eliminated: false }))
            team.Org.BribingInvestigation();
        return true;
    }

    /// <summary>Commands 1Ah–1Ch: one legalization step for business type 1–3 ("Beer has been legalized." 17,39…47).</summary>
    private static bool Legalization(Team player, int type)
    {
        var level = World.LegalizationLevel(type);
        if (level > 2 || !player.Org.UseItem(Legalize[level]))
            return false;
        World.SetLegalization(type, level + 1);
        Messages.Show(OriginalTexts.Instance.Get(17, 39 + (type - 1) * 3 + level));
        return true;
    }

    /// <summary>
    /// Command 1Dh: the FBI marks the first of the target's gangsters (not a Collector) that isn't a snitch yet; the
    /// buyer may then pay him to switch sides (order 0Ch). "Snitch at ..." (17,49).
    /// </summary>
    private static bool PlantSnitch(Team player, Team target)
    {
        var unit = (Engine.GetMainLoop() as SceneTree)?.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
            .FirstOrDefault(u => !u.IsDead && u.OwnerTeam == target && u.UnitType != UnitCommon.UnitType.Collector && !u.IsSnitch);
        if (unit == null || !player.Org.UseItem(FbiSnitch))
            return false;
        unit.IsSnitch = true;
        Messages.Show($"{OriginalTexts.Instance.Get(17, 49)} {unit.DisplayName}");
        return true;
    }
}
