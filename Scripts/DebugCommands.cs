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

    public string StatusLine() => PanelInfo.StatusText;

    /// <summary>
    /// Text map of what player unit <paramref name="index"/> sees: @ the unit, # building seen, x building hidden,
    /// . ground seen, space ground hidden. Rows are map y, columns map x.
    /// </summary>
    public string SightMap(int index)
    {
        var unit = PlayerFighters().ElementAtOrDefault(index);
        if (unit == null)
            return "no unit";
        var grid = unit.SightGrid(out var center, out var r);
        var sb = new StringBuilder($"{unit.UnitType} at {center}, r {r}\n");
        for (var dy = -r; dy <= r; dy++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                var cell = center + new Vector2I(dx, dy);
                var seen = Vision.Visible(grid, center, r, cell);
                var open = Vision.SeeThrough(cell);
                sb.Append(dx == 0 && dy == 0 ? '@' : open ? (seen ? '.' : ' ') : (seen ? '#' : 'x'));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Posts a game message (kind 0 normal, 4 good).</summary>
    public string Say(string text, int kind = 0)
    {
        Messages.Show(text, kind);
        return $"{Messages.Log.Count} messages";
    }

    /// <summary>Ends the episode as lost (the end screen with the statistics; a loss saves no progress).</summary>
    public string Lose()
    {
        CampaignManager.Instance?.OnPlayerDefeated(Player, OriginalTexts.Instance.Get(19, 1));
        return $"statistics: {Statistics.Current?.Count} slots, {Statistics.Current?.Players.Count} players";
    }

    /// <summary>Hits player <paramref name="playerId"/>'s HQ for <paramref name="hp"/>, as if by one of the local player's units.</summary>
    public string DamageHq(int playerId, int hp)
    {
        var hq = Teams.GetTeamByPlayerId(playerId)?.HqCompany;
        if (hq?.CanBeAttacked == null || !IsInstanceValid(hq))
            return "no HQ";
        var attacker = Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
            .FirstOrDefault(u => !u.IsDead && u.CanAttack != null && u.OwnerTeam != null && u.OwnerTeam != hq.OwnerTeam)?.CanAttack;
        hq.CanBeAttacked.ReceiveDamage(hp, attacker);
        return IsInstanceValid(hq) ? $"HQ HP {hq.Hp}/{hq.MaxHp}" : "HQ destroyed";
    }

    /// <summary>Sets the salary of every unit of player <paramref name="playerId"/> (episode units often cost nothing).</summary>
    public string SetSalary(int playerId, int salary)
    {
        var units = Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => !u.IsDead && u.OwnerTeam?.PlayerId == playerId).ToList();
        foreach (var unit in units)
            unit.Salary = salary;
        return $"{units.Count} units";
    }

    /// <summary>The player's unit number <paramref name="index"/> quits as if unpaid and walks to map cell (x, y).</summary>
    public string Desert(int index, int x, int y)
    {
        var unit = PlayerFighters().ElementAtOrDefault(index);
        if (unit == null)
            return "no unit";
        unit.Resign();
        unit.WalkToCell(new Vector2I(x, y));
        return $"{unit.UnitType}@{unit.Cell} deserts -> ({x}, {y})";
    }

    /// <summary>Deserters (unpaid units that quit) and rogue units with their targets.</summary>
    public string Deserters() =>
        string.Join("\n", Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => !u.IsDead && (u.IsDeserter || u.IsRogue)).Select(u =>
        {
            var t = u.CanAttack?.TargetParent;
            var target = t is Unit tu ? $"{tu.UnitType} of {tu.OwnerTeam?.PlayerName ?? (tu.IsDeserter ? "deserter" : "-")}" : t?.ToString() ?? "-";
            return $"{u.UnitType}{(u.IsRogue ? " rogue" : " deserter")} @{u.Cell} HP {u.CanBeAttacked?.HP} target {target}";
        }));

    /// <summary>Eliminated players' units go rogue instead of disappearing.</summary>
    public string RogueUnits(bool on)
    {
        Organisation.RogueUnits = on;
        return $"rogue units {on}";
    }

    /// <summary>Units and economic companies per player.</summary>
    public string Owners()
    {
        var units = Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => !u.IsDead).ToList();
        var companies = Tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(c => c.IsEconomic).ToList();
        var sb = new StringBuilder();
        foreach (var team in Teams.TeamsInGame.Values)
            sb.Append($"P{team.PlayerId} {team.PlayerName}{(team.Org?.Eliminated == true ? " [out]" : "")}: units {units.Count(u => u.OwnerTeam == team)}, companies {companies.Count(c => c.OwnerTeam == team)} (business {companies.Count(c => c.OwnerTeam == team && c.BusinessType != OriginalBusinessTable.BusinessType.Legal)})\n");
        sb.Append($"rogue units {units.Count(u => u.IsRogue)}, unowned companies {companies.Count(c => c.OwnerTeam == null)} (business {companies.Count(c => c.OwnerTeam == null && c.BusinessType != OriginalBusinessTable.BusinessType.Legal)})");
        return sb.ToString();
    }

    /// <summary>State of every computer player.</summary>
    public string Ai() => string.Join("\n", Teams.TeamsInGame.Values.Where(t => t.Ai != null).Select(t => t.Ai.Describe() + " | " + t.Org));

    /// <summary>Walks the player's unit number <paramref name="index"/> to map cell (x, y) like a Go order.</summary>
    /// <summary>Go (or Force Go) order for the player's unit number <paramref name="index"/> to map cell (x, y).</summary>
    public string Order(int index, int x, int y, bool force)
    {
        var unit = PlayerFighters().ElementAtOrDefault(index);
        if (unit == null)
            return "no unit";
        unit.GoTo(unit.Map.MapToLocal(new Vector2I(x, y)), force);
        return $"{unit.UnitType}@{unit.Cell} {(force ? "force go" : "go")} -> ({x}, {y}) mode {unit.MoveMode}";
    }

    /// <summary>Turns the fog of war on or off; reports visible cells and hidden units / companies in fog.</summary>
    public string Fog(bool on)
    {
        FogOfWar.Enabled = on;
        FogOfWar.Instance?.Refresh();
        var units = Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => !u.IsDead).ToList();
        var companies = Tree.GetNodesInGroup(Company.GroupName).OfType<Company>().ToList();
        return $"{FogOfWar.Instance?.Describe()}\nfog {on}: visible cells {FogOfWar.Instance?.VisibleCount}, hidden units {units.Count(u => !u.Visible)}/{units.Count}, companies in fog {companies.Count(c => c.InFog)}/{companies.Count}, shown with an outdated owner {companies.Count(c => c.ShownOwner != c.OwnerTeam)}";
    }

    /// <summary>Patrol order for the player's unit number <paramref name="index"/> between its cell and (x, y).</summary>
    public string Patrol(int index, int x, int y)
    {
        var unit = PlayerFighters().ElementAtOrDefault(index);
        if (unit == null)
            return "no unit";
        unit.SetOrder(new PatrolOrder(unit, unit.Cell, new Vector2I(x, y)));
        return $"{unit.UnitType}@{unit.Cell} patrols to ({x}, {y})";
    }

    /// <summary>HQ service state of player <paramref name="playerId"/> and the legalization levels.</summary>
    public string HqState(int playerId)
    {
        var org = Teams.GetTeamByPlayerId(playerId)?.Org;
        if (org == null)
            return "no player";
        return $"{org} rumour {org.RumourActive} investigation {org.InvestigationActive} fbi {org.FbiVisitActive} ({org.FbiVisitProgress:0.00}) factors [{string.Join(" ", org.TypeFactor.Select(f => f.ToString("0.00")))}] "
               + $"legal [{World.LegalizationLevel(1)} {World.LegalizationLevel(2)} {World.LegalizationLevel(3)}] bribed [{string.Join(" ", org.AuthorityMoney)}] snitches {Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Count(u => u.IsSnitch && u.OwnerTeam?.Org == org)}";
    }

    /// <summary>Order mode, movement and target of every player unit.</summary>
    public string Orders() =>
        string.Join("\n", PlayerFighters().Select(u =>
            $"{u.UnitType}@{u.Cell} order {u.CurrentOrder?.Name ?? "-"} mode {u.MoveMode?.ToString() ?? "-"} moving {u.IsMoving} target {(u.CanAttack?.TargetParent as Unit)?.UnitType.ToString() ?? "-"} HP {u.CanBeAttacked?.HP}"));

    public string GoTo(int index, int x, int y)
    {
        var unit = PlayerFighters().ElementAtOrDefault(index);
        if (unit == null)
            return "no unit";
        unit.MoveTo(unit.Map.MapToLocal(new Vector2I(x, y)));
        return $"{unit.UnitType}@{unit.Cell} -> ({x}, {y})";
    }

    /// <summary>
    /// Coordinate convention check: how many episode units and map start points land on walkable cells when read as
    /// (x, y) — the current FromOriginal — and when swapped.
    /// </summary>
    public string CheckCoords()
    {
        var cm = CampaignManager.Instance;
        var map = cm?.TileMap;
        if (map == null)
            return "no map";
        var blocked = new System.Collections.Generic.HashSet<Vector2I>(map.GetMeta(TileMapGenerator.TileNonWalkable).AsGodotArray<Vector2I>());
        var used = new System.Collections.Generic.HashSet<Vector2I>(map.GetUsedCells());
        bool Walkable(Vector2I original)
        {
            var c = MapTileMath.FromOriginal(map, original);
            return used.Contains(c) && !blocked.Contains(c);
        }
        int asIs = 0, swapped = 0, n = 0;
        foreach (var u in cm.CurrentScenario.StartingUnits)
        {
            n++;
            if (Walkable(u.Position)) asIs++;
            if (Walkable(new Vector2I(u.Position.Y, u.Position.X))) swapped++;
        }
        var sb = new StringBuilder($"units walkable: as-is {asIs}/{n}, swapped {swapped}/{n}\n");
        var original = OriginalData.Instance.Maps.Maps.Values.FirstOrDefault(m => m.FileName.Equals(cm.CurrentScenario.MapFile, System.StringComparison.OrdinalIgnoreCase));
        if (original != null)
            foreach (var p in original.StartPoints)
                sb.Append($"start {p}: as-is walkable {Walkable(p)} building {blocked.Contains(MapTileMath.FromOriginal(map, p))}; swapped walkable {Walkable(new Vector2I(p.Y, p.X))} building {blocked.Contains(MapTileMath.FromOriginal(map, new Vector2I(p.Y, p.X)))}\n");
        return sb.ToString();
    }

    /// <summary>Every unit: type, owner, cell, HP, current target.</summary>
    public string Units()
    {
        return string.Join("\n", Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => !u.IsDead).Select(u =>
            $"{u.UnitType} {u.OwnerTeam?.PlayerName ?? "-"} @{u.Cell} HP {u.CanBeAttacked?.HP}/{u.CanBeAttacked?.MaxHP} target {(u.CanAttack?.TargetParent as Unit)?.UnitType.ToString() ?? "-"} fighting {u.IsFighting}"));
    }

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
            sb.Append($"  shares [{string.Join(" ", team.Org.Shares.Select(v => v.ToString("0.#")))}] favour [{string.Join(" ", team.Org.Favour.Select(v => v.ToString("0.#")))}] bribed [{string.Join(" ", team.Org.AuthorityMoney)}]\n");
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

    /// <summary>Shows a unit (kind "unit", index among the player's units), an own company or the HQ in the panel.</summary>
    public string Select(string kind, int index = 0)
    {
        Node2D node = kind switch
        {
            "unit" => Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => u.OwnerTeam == Player).ElementAtOrDefault(index),
            "company" => Tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(c => c.OwnerTeam == Player && c.IsEconomic).ElementAtOrDefault(index),
            "enemybusiness" => Tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(c => c.OwnerTeam != null && c.OwnerTeam != Player && c.IsEconomic
                && c.BusinessType != OriginalBusinessTable.BusinessType.Legal && (c.Block?.District ?? 3) == 0).ElementAtOrDefault(index),
            "neutral" => Tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(c => c.OwnerTeam == null && c.IsEconomic).ElementAtOrDefault(index),
            "enemy" => Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => !u.IsDead && u.OwnerTeam != null && u.OwnerTeam != Player
                && u.UnitType is UnitCommon.UnitType.Thug or UnitCommon.UnitType.Pistol).ElementAtOrDefault(index),
            "hq" => Player.HqCompany,
            "enemyhq" => Teams.TeamsInGame.Values.Where(t => t != Player && t.HqCompany != null).Select(t => t.HqCompany).ElementAtOrDefault(index),
            "snitch" => Tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => !u.IsDead && u.IsSnitch).ElementAtOrDefault(index),
            _ => null,
        };
        GuiGameback.UpdateSelectionIcon(node);
        return node?.ToString() ?? "nothing";
    }

    /// <summary>A Cadillac next to the player's HQ with the player's first gangster in it, selected.</summary>
    public string Car()
    {
        if (Player?.HqCompany == null)
            return "no HQ";
        var car = CampaignManager.Instance.CreateUnitNear(UnitCommon.UnitType.Cadillac, Player, Player.HqCompany.Cell);
        var rider = PlayerFighters().FirstOrDefault(u => u != car && !u.IsCar);
        var boarded = rider != null && car.Board(rider);
        GuiGameback.UpdateSelectionIcon(car);
        return $"car at {car.Cell}, boarded {boarded}, passengers {car.Passengers.Count()}";
    }

    /// <summary>Rows the player's own company number <paramref name="index"/> could become (type 1–5).</summary>
    public string Offers(int index, int type)
    {
        var company = OwnCompany(index);
        if (company == null)
            return "no company";
        var rows = BusinessRules.Offers(company, (OriginalBusinessTable.BusinessType)type);
        return company + "\n" + string.Join("\n", rows.Select(r => $"{r.Index} {r.Name} price {r.Price} favour {BusinessRules.FavourNeeded(r)} build {r.Value1C} min"));
    }

    /// <summary>Converts own company <paramref name="index"/> to its first offer of <paramref name="type"/>.</summary>
    public string Convert(int index, int type, int nth = 0)
    {
        var company = OwnCompany(index);
        var row = company == null ? null : BusinessRules.Offers(company, (OriginalBusinessTable.BusinessType)type).ElementAtOrDefault(nth);
        return row == null ? "nothing to build" : $"{company.StartConversion(row)}: {row}";
    }

    /// <summary>Adds favour points and bribe money with an authority (1 army, 2 FBI, 3 politician, 4 police).</summary>
    public string Favour(int authority, float points, long money)
    {
        Player.Org.Favour[authority] += points;
        Player.Org.AuthorityMoney[authority] += money;
        return $"favour {Player.Org.Favour[authority]}, money {Player.Org.AuthorityMoney[authority]}";
    }

    /// <summary>Takes <paramref name="hp"/> hit points from own company <paramref name="index"/>.</summary>
    public string Damage(int index, int hp)
    {
        var company = OwnCompany(index);
        if (company?.CanBeAttacked == null)
            return "no company";
        company.CanBeAttacked.HP = Mathf.Max(1, company.CanBeAttacked.HP - hp);
        return company.ToString();
    }

    private Company OwnCompany(int index) =>
        Tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(c => c.OwnerTeam == Player && c.IsEconomic).ElementAtOrDefault(index);

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
