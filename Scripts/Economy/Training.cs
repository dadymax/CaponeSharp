using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Recruiting (HQ menu sub_431FD3, action handler sub_4323F0, training job sub_4274B5 / sub_427505).
/// One unit trains at a time in the player's primary training center (vt+8Ch: the chosen one, else the HQ, else an
/// own training-center business). Training takes the unit table's +24h minutes divided by 1 + the number of own
/// training centers (vt+84h); when it ends the player pays the price and the army service, and the unit appears next
/// to the center. If either can't be paid, nothing appears.
/// </summary>
public static class Training
{
    public class Recruit
    {
        public UnitCommon.UnitType Type;
        /// <summary>Menu slot (6–17: the two columns at x 730 / 760).</summary>
        public int Slot;
        /// <summary>Chicago.tile "Button … up" tile; the next tile is "down".</summary>
        public int Icon;
        /// <summary>Text (20, n) "Recruit … ($%s and %1.0f Army favour points)".</summary>
        public int Text;
        /// <summary>Authority service paid on arrival (table 0x483790), -1 = none.</summary>
        public int Service;
    }

    /// <summary>Menu order of sub_431FD3 (actions 0–8).</summary>
    public static readonly Recruit[] Recruits =
    {
        new() { Type = UnitCommon.UnitType.Thug, Slot = 6, Icon = 0x3A, Text = 17, Service = -1 },
        new() { Type = UnitCommon.UnitType.Pistol, Slot = 7, Icon = 0x3C, Text = 18, Service = 0 },
        new() { Type = UnitCommon.UnitType.Thompson, Slot = 8, Icon = 0x3E, Text = 19, Service = 6 },
        new() { Type = UnitCommon.UnitType.Terrorist, Slot = 9, Icon = 0x34, Text = 20, Service = 4 },
        new() { Type = UnitCommon.UnitType.Sniper, Slot = 10, Icon = 0x40, Text = 21, Service = 1 },
        new() { Type = UnitCommon.UnitType.Shotgun, Slot = 11, Icon = 0x9B6, Text = 22, Service = 2 },
        new() { Type = UnitCommon.UnitType.Hitman, Slot = 12, Icon = 0xAB2, Text = 23, Service = 3 },
        new() { Type = UnitCommon.UnitType.Cadillac, Slot = 13, Icon = 0xAE7, Text = 24, Service = 5 },
        new() { Type = UnitCommon.UnitType.Thief, Slot = 14, Icon = 0xD8B, Text = 25, Service = 0x13 },
    };

    /// <summary>A running training job (class 16h).</summary>
    public class Job
    {
        public Team Team;
        public Recruit Recruit;
        public Company Center;
        public long Start;
        public long End;

        public float Progress(long now) => End <= Start ? 1f : Mathf.Clamp((float)(now - Start) / (End - Start), 0f, 1f);
    }

    private static readonly Dictionary<Team, Job> Jobs = new();

    public static void Reset() => Jobs.Clear();

    public static Job Current(Team team) => team != null && Jobs.TryGetValue(team, out var job) ? job : null;

    public static long Price(UnitCommon.UnitType type) => OriginalUnitTable.Instance.Get((int)type)?.Cost ?? 0;

    public static float ServicePoints(Recruit recruit) =>
        recruit.Service < 0 ? 0 : AuthorityItems.Get(recruit.Service)?.Points ?? 0;

    /// <summary>The menu lists the Thug always and the others when their army service is available.</summary>
    public static bool IsOffered(Organisation org, Recruit recruit) => recruit.Service < 0 || org.CanUseItem(recruit.Service);

    private static IEnumerable<Company> OwnCompanies(Team team) =>
        ((Engine.GetMainLoop() as SceneTree)?.GetNodesInGroup(Company.GroupName).OfType<Company>() ?? Enumerable.Empty<Company>())
        .Where(c => c.OwnerTeam == team && IsInstanceValid(c));

    private static bool IsInstanceValid(GodotObject o) => GodotObject.IsInstanceValid(o);

    /// <summary>Catalogue +28h = 4: training-center support business (company vt+1A8h).</summary>
    public static bool IsTrainingCenter(Company company) => company.IsEconomic && company.Entry?.Value28 == 4;

    /// <summary>vt+8Ch (sub_431E63): the chosen center if still own, else the HQ, else an own training-center business.</summary>
    public static Company PrimaryCenter(Team team)
    {
        var chosen = team?.Org?.PrimaryCenter;
        if (chosen != null && IsInstanceValid(chosen) && chosen.OwnerTeam == team && IsTrainingCenter(chosen))
            return chosen;
        if (team?.HqCompany != null && IsInstanceValid(team.HqCompany))
            return team.HqCompany;
        return OwnCompanies(team).FirstOrDefault(IsTrainingCenter);
    }

    /// <summary>vt+84h (sub_431DF3): 1 + own training centers.</summary>
    public static int SpeedDivisor(Team team) => 1 + OwnCompanies(team).Count(IsTrainingCenter);

    /// <summary>Action 0–8 of the recruit menu: starts training (sub_427505 event 1).</summary>
    public static bool Start(Team team, Recruit recruit)
    {
        var clock = GameClock.Instance;
        var center = PrimaryCenter(team);
        if (clock == null || center == null || team.Org == null || Current(team) != null || !IsOffered(team.Org, recruit))
            return false;
        var minutes = (OriginalUnitTable.Instance.Get((int)recruit.Type)?.Value24 ?? 600) / SpeedDivisor(team);
        var job = new Job { Team = team, Recruit = recruit, Center = center, Start = clock.Minute, End = clock.Minute + minutes };
        Jobs[team] = job;
        GD.Print($"Training: {team.PlayerName} trains a {recruit.Type} ({minutes} min)");
        clock.ScheduleAt(job.End, () => Finish(job));
        return true;
    }

    /// <summary>Event 3E8h: pay the price and the service, then the unit appears next to the center.</summary>
    private static void Finish(Job job)
    {
        if (Current(job.Team) != job)
            return;
        Jobs.Remove(job.Team);
        var org = job.Team.Org;
        var price = Price(job.Recruit.Type);
        if (org == null || org.Eliminated || !org.CanAfford(price))
            return;
        if (job.Recruit.Service >= 0 && !org.UseItem(job.Recruit.Service))
            return;
        org.Spend(price);
        var cm = CampaignManager.Instance;
        if (cm == null || !IsInstanceValid(job.Center))
            return;
        var free = cm.FreeCellsAround(job.Center.Cell, 1);
        var unit = cm.CreateUnit(job.Recruit.Type, job.Team, free.Count > 0 ? free[0] : job.Center.Cell);
        GD.Print($"Training: {job.Team.PlayerName} got a {job.Recruit.Type} '{unit.DisplayName}' for {price}");
        if (job.Team.IsCurrentPlayerColor)
            Sound.Play(ArriveSound(job.Recruit.Type));
    }

    /// <summary>sub_427505 switch: arrive sounds; others (Thug, Thief…) ThugArrive.</summary>
    public static string ArriveSound(UnitCommon.UnitType type) => type switch
    {
        UnitCommon.UnitType.Pistol => "PistolArrive.wav",
        UnitCommon.UnitType.Thompson => "ThompsonArrive.wav",
        UnitCommon.UnitType.Terrorist => "BombmanArrive.wav",
        UnitCommon.UnitType.Sniper => "SniperArrive.wav",
        UnitCommon.UnitType.Cadillac => "NewCar.wav",
        UnitCommon.UnitType.Shotgun => "ShotgunArrive.wav",
        UnitCommon.UnitType.Hitman => "HitmanArrive.wav",
        _ => "ThugArrive.wav",
    };
}
