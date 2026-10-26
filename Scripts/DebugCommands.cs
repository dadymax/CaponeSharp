using System.Linq;
using System.Text;
using Godot;

namespace Capone;

/// <summary>
/// Test hooks for the MCP game_eval tool, at /root/Main/Debug. Example:
/// <c>get_node("/root/Main/Debug").Status()</c>, <c>.AutoExtort(true)</c>.
/// </summary>
public partial class DebugCommands : Node
{
    private bool _autoExtort;
    private bool _scheduled;

    private Team Player => Teams.GetCurrentPlayerTeam();

    private SceneTree Tree => GetTree();

    public string Status()
    {
        var clock = GameClock.Instance;
        var sb = new StringBuilder();
        sb.Append($"time {GameClock.Format(clock?.Minute ?? 0)} speed {clock?.Speed} paused {clock?.Paused}\n");
        var companies = Tree.GetNodesInGroup(Company.GroupName).OfType<Company>().ToList();
        var units = Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().ToList();
        foreach (var team in Teams.TeamsInGame.Values)
        {
            var owned = companies.Where(c => c.OwnerTeam == team && c.IsEconomic).ToList();
            var mine = units.Where(u => u.OwnerTeam == team && !u.IsDead).ToList();
            sb.Append($"P{team.PlayerId} {team.PlayerName}{(team.IsCurrentPlayerColor ? " (you)" : "")}: {team.Org}; ");
            sb.Append($"companies {owned.Count} (uncollected {owned.Sum(c => c.Uncollected)}); ");
            sb.Append($"units {mine.Count}: {string.Join(",", mine.Select(UnitLine))}\n");
        }
        var police = units.Where(u => u.UnitType is UnitCommon.UnitType.PoliceCar or UnitCommon.UnitType.Policeman && !u.IsDead).ToList();
        sb.Append($"police units {police.Count}; blocks with owners {CityBlock.All.Count(b => b.Owners.Count > 0)}\n");
        return sb.ToString();
    }

    private static string UnitLine(Unit u) =>
        $"{u.UnitType}@{u.Cell}{(u.IsArrested ? "[arrested]" : "")}{(u.CurrentOrder != null ? $"[{u.CurrentOrder.Name}]" : "")}$" + u.CarriedMoney;

    /// <summary>Sends the player's unit number <paramref name="index"/> to extort the nearest company it may extort.</summary>
    public string ExtortNearest(int index)
    {
        var units = PlayerFighters().ToList();
        if (index < 0 || index >= units.Count)
            return $"no unit {index} (have {units.Count})";
        var unit = units[index];
        var target = NearestTarget(unit);
        if (target == null)
            return "no target";
        unit.SetOrder(new ExtortOrder(unit, target));
        return $"{unit.UnitType}@{unit.Cell} -> {target}";
    }

    /// <summary>Every game hour, idle player fighters extort the nearest company they may extort.</summary>
    public string AutoExtort(bool on)
    {
        _autoExtort = on;
        if (on && !_scheduled)
        {
            _scheduled = true;
            Tick();
        }
        return $"auto extort {on}";
    }

    /// <summary>A police car from the nearest station goes after the player's unit number <paramref name="index"/>.</summary>
    public string CallCops(int index)
    {
        var units = PlayerFighters().ToList();
        if (index < 0 || index >= units.Count)
            return $"no unit {index} (have {units.Count})";
        Police.Instance.Dispatch(units[index], lawyersCanHelp: true);
        return $"cops after {units[index].UnitType}@{units[index].Cell}";
    }

    public string Speed(float speed)
    {
        GameClock.Instance.Speed = speed;
        return $"speed {GameClock.Instance.Speed}";
    }

    public string Give(long amount)
    {
        Player.Org.Deposit(amount);
        return Player.Org.ToString();
    }

    public string Companies(int max = 20)
    {
        var list = Tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(c => c.OwnerTeam != null && c.IsEconomic).Take(max);
        return string.Join("\n", list.Select(c => $"{c} hp {c.Hp}/{c.MaxHp} unc {c.Uncollected} inc {c.Income()}"));
    }

    private void Tick()
    {
        if (!IsInstanceValid(this))
            return;
        if (_autoExtort)
            foreach (var unit in PlayerFighters().Where(u => u.CurrentOrder == null && !u.IsMoving))
            {
                var target = NearestTarget(unit);
                if (target != null)
                    unit.SetOrder(new ExtortOrder(unit, target));
            }
        GameClock.Instance.ScheduleIn(GameClock.MinutesPerHour, Tick);
    }

    private System.Collections.Generic.IEnumerable<Unit> PlayerFighters() =>
        Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
            .Where(u => u.OwnerTeam == Player && !u.IsDead && !u.IsArrested
                        && u.UnitType is not (UnitCommon.UnitType.Collector or UnitCommon.UnitType.Cadillac));

    private Company NearestTarget(Unit unit)
    {
        var taken = Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
            .Select(u => (u.CurrentOrder as ExtortOrder)?.Company).Where(c => c != null).ToHashSet();
        return Tree.GetNodesInGroup(Company.GroupName).OfType<Company>()
            .Where(c => !taken.Contains(c) && ExtortOrder.CanExtort(unit, c))
            .OrderBy(c => MapTileMath.GetDistanceExact(unit, c))
            .FirstOrDefault();
    }
}
