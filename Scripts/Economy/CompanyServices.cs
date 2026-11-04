using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// Orders on another player's company (company menu sub_421370, action handler sub_421CF5, commands sub_421E40):
/// <list type="bullet">
/// <item>Slot 5, "Ask Cops to raid the business" (30,13/14, icon 0xAD8, action 2): a business (not legal) in the slums or
/// the middle class; police service by district and price tier (sub_4212C2) — slums 26h / 2Bh / 2Fh, middle class 32h /
/// 35h. A police car drives from the nearest station (raid job sub_445CA4 / sub_445D72); on arrival the local victim
/// gets "Cops raid your business !" (28,0); 15 game minutes later the business is closed: it becomes an unowned legal
/// company (company vt+1B0h sub_422D82). Then the car drives back.</item>
/// <item>Slot 4, "Ask Mayor to demolish the building" (30,15/16, icon 0xDB3, command 0Eh): a slum business; politician
/// service 14h / 18h / 1Dh by tier; "Building has been demolished ..." (30,20 to the buyer, 30,21 to the victim); HP 0
/// and the company is removed (vt+FCh sub_422DE0).</item>
/// <item>Slot 3, "Ask Mayor to arrange brownout" (30,17/18, icon 0xD43, action 9 → block command 1, sub_420150): any
/// company whose block has power; the best usable politician service cuts the block's power — 1Ch two weeks, 19h one
/// week, 15h 1440 × 3.5 minutes (sub_420B20 × dbl_47B654) — and the block's companies earn nothing meanwhile.</item>
/// </list>
/// </summary>
public static class CompanyServices
{
    private static readonly int[,] RaidService = { { 0x26, 0x2B, 0x2F }, { 0x32, 0x35, -1 } };
    private static readonly int[] DemolishService = { 0x14, 0x18, 0x1D };
    private static readonly (int Service, long Minutes)[] BrownoutServices =
    {
        (0x1C, 2 * GameClock.MinutesPerWeek), (0x19, GameClock.MinutesPerWeek), (0x15, (long)(1440 * 3.5)),
    };

    private static string PointsText(int pluralIndex, int service)
    {
        var points = (long)(AuthorityItems.Get(service)?.Points ?? 0);
        return points > 1
            ? PanelInfo.CFormat(OriginalTexts.Instance.Get(30, pluralIndex), points)
            : OriginalTexts.Instance.Get(30, pluralIndex + 1);
    }

    private static int District(Company c) => c.Block?.District ?? 3;

    /// <summary>sub_4212C2: police service for a raid by district (0 slum, 1 middle class) and price tier; -1 = none.</summary>
    public static int RaidServiceFor(int district, int tier) =>
        district is 0 or 1 && tier is >= 0 and <= 2 ? RaidService[district, tier] : -1;

    /// <summary>Politician service for a demolition: slum businesses only, by price tier; -1 = none.</summary>
    public static int DemolishServiceFor(int district, int tier) =>
        district == 0 && tier is >= 0 and <= 2 ? DemolishService[tier] : -1;

    /// <summary>Brownout services in the order they are tried, with their length in game minutes.</summary>
    public static IReadOnlyList<(int Service, long Minutes)> Brownouts => BrownoutServices;

    private static int Tier(Company c) => c.Entry == null ? -1 : BusinessRules.Tier(c.Entry);

    private static int RaidServiceFor(Company c) => RaidServiceFor(District(c), Tier(c));

    private static int DemolishServiceFor(Company c) => DemolishServiceFor(District(c), Tier(c));

    private static bool IsTarget(Team player, Company c) =>
        player?.Org != null && c != null && c.IsEconomic && c.OwnerTeam != player && !c.UnderConstruction;

    public static List<HqServices.MenuAction> Menu(Team player, Company company)
    {
        var list = new List<HqServices.MenuAction>();
        if (!IsTarget(player, company))
            return list;
        var org = player.Org;
        var business = company.BusinessType != OriginalBusinessTable.BusinessType.Legal;
        if (business)
        {
            var raid = RaidServiceFor(company);
            if (raid >= 0 && org.CanUseItem(raid))
                list.Add(new HqServices.MenuAction(5, 0xAD8, PointsText(13, raid), () => Raid(player, company)));
            var demolish = DemolishServiceFor(company);
            if (demolish >= 0 && org.CanUseItem(demolish))
                list.Add(new HqServices.MenuAction(4, 0xDB3, PointsText(15, demolish), () => Demolish(player, company)));
        }
        if (org.CanUseItem(0x15) && company.Block is { IsActive: true })
            list.Add(new HqServices.MenuAction(3, 0xD43, PointsText(17, 0x15), () => Brownout(player, company)));
        return list;
    }

    /// <summary>Action 2: pay the police service and send a car to raid the business.</summary>
    public static bool Raid(Team player, Company company)
    {
        var service = RaidServiceFor(company);
        if (!IsTarget(player, company) || service < 0 || !player.Org.UseItem(service))
            return false;
        Sound.Play("POLICE_S.WAV");
        var station = Police.Instance?.NearestStation(company);
        var car = station == null ? null : CampaignManager.Instance?.SpawnPoliceCar(station);
        if (car == null)
        {
            company.ReleaseToNeutral();
            return true;
        }
        car.SetOrder(new PoliceRaid(car, company, station));
        GD.Print($"Police: raid on {company} ordered by {player.PlayerName}");
        return true;
    }

    /// <summary>Command 0Eh: pay the politician and demolish the business.</summary>
    public static bool Demolish(Team player, Company company)
    {
        var service = DemolishServiceFor(company);
        if (!IsTarget(player, company) || service < 0 || !player.Org.UseItem(service))
            return false;
        if (player.IsCurrentPlayerColor)
            Messages.Show(OriginalTexts.Instance.Get(30, 20), Messages.Normal, company.Cell);
        else if (company.OwnerTeam?.IsCurrentPlayerColor == true)
            Messages.Show(OriginalTexts.Instance.Get(30, 21), Messages.Normal, company.Cell);
        company.Demolish();
        return true;
    }

    /// <summary>Action 9 → block command 1: the best usable politician service cuts the block's power.</summary>
    public static bool Brownout(Team player, Company company)
    {
        var block = company.Block;
        if (block == null || !block.IsActive || player?.Org == null)
            return false;
        foreach (var (service, minutes) in BrownoutServices)
            if (player.Org.UseItem(service))
            {
                block.SuspendedUntil = (GameClock.Instance?.Minute ?? 0) + minutes;
                GD.Print($"Brownout in block {block.Number} for {minutes} minutes, ordered by {player.PlayerName}");
                return true;
            }
        return false;
    }
}

/// <summary>The raid car's job (sub_445D72): drive to the business, raid it after 15 minutes, drive back and leave.</summary>
public partial class PoliceRaid : Node, IScheduled
{
    public string SaveId { get; } = SaveIds.Next("raid");

    /// <summary>15 minutes after arriving the business is closed, then the car drives back.</summary>
    public void OnClockEvent(string kind, long arg)
    {
        if (kind != "close")
            return;
        if (IsInstanceValid(_company) && _company.OwnerTeam != null)
            _company.ReleaseToNeutral();
        _state = 2;
        if (IsInstanceValid(_car) && IsInstanceValid(_station))
            _car.DriveTo(_station);
    }

    public const int RaidMinutes = 15;
    private readonly Unit _car;
    private readonly Company _company;
    private readonly Company _station;
    private int _state; // 0 driving there, 1 raiding, 2 driving back
    private bool _started;

    public PoliceRaid() { }

    public PoliceRaid(Unit car, Company company, Company station)
    {
        _car = car;
        _company = company;
        _station = station;
        Name = "PoliceRaid";
    }

    public string Describe() =>
        $"raid car at {(IsInstanceValid(_car) ? _car.Cell.ToString() : "-")}, moving {(IsInstanceValid(_car) && _car.IsMoving)}, state {_state}, "
        + $"target {(IsInstanceValid(_company) ? _company.Cell.ToString() : "-")}";

    private bool NearEnough()
    {
        var d = _car.Cell - _company.Cell;
        return Mathf.Abs(d.X) + Mathf.Abs(d.Y) <= 2;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsInstanceValid(_car) || _car.IsDead)
            return;
        if (!_started)
        {
            _started = true;
            _car.WalkTo(_company);
            return;
        }
        // The car keeps to the road (sub_432E80), and a business is ringed by pavement: our detours would circle the
        // block for ever, so the raid starts once the car stands within two cells of it.
        if (_state == 0 && _car.IsMoving && IsInstanceValid(_company) && NearEnough())
            _car.StopMoving();
        if (_car.IsMoving)
            return;
        switch (_state)
        {
            case 0:
                _state = 1;
                if (IsInstanceValid(_company) && _company.OwnerTeam?.IsCurrentPlayerColor == true)
                {
                    Sound.Play("PoliceRaid.wav");
                    Messages.Show(OriginalTexts.Instance.Get(28, 0), Messages.Normal, _company.Cell);
                }
                GameClock.Instance?.ScheduleIn(RaidMinutes, this, "close");
                break;
            case 2:
                _state = 3;
                if (IsInstanceValid(_car))
                    _car.Leave();
                break;
        }
    }
}
