using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace Capone;

/// <summary>Reading the save file back: ids → objects, teams by colour.</summary>
public sealed class LoadContext
{
    public readonly Dictionary<string, IScheduled> ById = new();
    public readonly Dictionary<string, Team> TeamByColor = new();

    public Team Team(JsonNode node) =>
        node?.GetValue<string>() is { } color && TeamByColor.TryGetValue(color, out var team) ? team : null;

    public T Get<T>(JsonNode node) where T : class =>
        node?.GetValue<string>() is { } id && ById.TryGetValue(id, out var o) ? o as T : null;

    public static Vector2I Cell(JsonNode node) => node is JsonArray a ? new Vector2I((int)a[0]!, (int)a[1]!) : Vector2I.Zero;

    public static Vector2 Pos(JsonNode node) => node is JsonArray a ? new Vector2((float)a[0]!, (float)a[1]!) : Vector2.Zero;

    public static OriginalBusinessTable.Entry Row(JsonNode node) =>
        node is null ? null : OriginalBusinessTable.Instance.Get((int)node);
}

public partial class Company
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        var owner = ctx.Team(o["owner"]);
        if (!IsHq && owner != OwnerTeam)
            SetOwnerTeam(owner);
        if (LoadContext.Row(o["row"]) is { } row && row != Entry && !IsHq)
            SetRow(row);
        CompanyName = (string)o["name"] ?? CompanyName;
        if (_company?.CanBeAttacked is { } health)
        {
            health.MaxHP = (int)o["maxHp"];
            health.HP = (int)o["hp"];
        }
        Uncollected = (long)o["uncollected"];
        Percent = (int)o["percent"];
        CachedIncomeEnabled = (bool)o["cachedIncome"];
        Building = LoadContext.Row(o["building"]);
        BuildStart = (long)o["buildStart"];
        BuildEnd = (long)o["buildEnd"];
        _conversionOwner = ctx.Team(o["conversionOwner"]);
        Repairing = (bool)o["repairing"];
        Discovered = (bool)o["discovered"];
        _inFog = (bool)o["inFog"];
        _seenOwner = ctx.Team(o["seenOwner"]);
        _seenEntry = LoadContext.Row(o["seenRow"]);
        _economyStarted = (bool)o["economyStarted"];
        UpdateSign();
    }
}

public partial class Unit
{
    /// <summary>First pass: the unit's own values (references to other objects come in <see cref="LoadLinks"/>).</summary>
    public void LoadState(JsonObject o)
    {
        SaveId = (string)o["id"];
        Position = LoadContext.Pos(o["pos"]);
        PersonalName = (string)o["name"] ?? "";
        EpiFlagA = (int)o["epiA"];
        EpiFlagB = (int)o["epiB"];
        if (CanBeAttacked != null)
        {
            CanBeAttacked.MaxHP = (int)o["maxHp"];
            CanBeAttacked.HP = (int)o["hp"];
        }
        Salary = (int)o["salary"];
        CarriedMoney = (long)o["money"];
        IsSnitch = (bool)o["snitch"];
        DisguiseMinute = (long)o["disguise"];
        LastShotMinute = (long)o["lastShot"];
        LastFoughtMinute = (long)o["lastFought"];
        Stance = (int)o["stance"];
        var mode = (int)o["moveMode"];
        MoveMode = mode < 0 ? null : mode;
        _moving = (bool)o["moving"];
        _moveTarget = LoadContext.Cell(o["moveTarget"]);
        _lineStart = LoadContext.Cell(o["lineStart"]);
        if ((bool)o["deserter"])
        {
            TeamColor = Teams.NeutralTeamColor;
            _unitOwner = UnitCommon.UnitOwner.Neutral;
            IsDeserter = true;
        }
        if ((bool)o["rogue"])
        {
            TeamColor = Teams.NeutralTeamColor;
            _unitOwner = UnitCommon.UnitOwner.Neutral;
            IsRogue = true;
        }
        if ((bool)o["arrested"])
        {
            IsArrested = true;
            Visible = false;
            _shape?.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        }
        if ((bool)o["inside"])
            EnterBuilding();
    }

    /// <summary>Second pass: car seats, targets and the order, once every unit and company exists.</summary>
    public void LoadLinks(JsonObject o, LoadContext ctx)
    {
        var seats = o["seats"]!.AsArray();
        for (var i = 0; i < _seats.Length && i < seats.Count; i++)
            _seats[i] = ctx.Get<Unit>(seats[i]);
        Vehicle = ctx.Get<Unit>(o["vehicle"]);
        _moveTargetNode = ctx.Get<Node2D>(o["moveTargetNode"]);
        _orderTarget = ctx.Get<Unit>(o["orderTarget"]);
        _targetStructure = ctx.Get<Node2D>(o["targetStructure"]);
        if (o["attack"] is JsonObject attack)
            CanAttack?.LoadState(attack, ctx);
        if (o["order"] is JsonObject order && CreateOrder(order, ctx) is { } node)
        {
            CurrentOrder = node;
            AddChild(node);
            (node as ISaveableOrder)?.LoadState(order, ctx);
        }
        RefreshOwnerRelation();
    }

    private Node CreateOrder(JsonObject o, LoadContext ctx) => (string)o["type"] switch
    {
        nameof(ExtortOrder) => ctx.Get<Company>(o["company"]) is { } c ? new ExtortOrder(this, c) : null,
        nameof(PatrolOrder) => new PatrolOrder(this, LoadContext.Cell(o["a"]), LoadContext.Cell(o["b"])),
        nameof(StealOrder) => ctx.Get<Company>(o["target"]) is { } t ? new StealOrder(this, t) : null,
        nameof(BoardOrder) => ctx.Get<Unit>(o["car"]) is { } car ? new BoardOrder(this, car) : null,
        nameof(CollectorJob) => CityBlock.All.FirstOrDefault(b => b.Number == (int)o["block"]) is { } block
            ? new CollectorJob(block, this) : null,
        nameof(PoliceJob) => ctx.Get<Unit>(o["gangster"]) is { } g && ctx.Get<Company>(o["station"]) is { } st
            ? new PoliceJob(this, g, st, (bool)o["lawyers"]) : null,
        nameof(PoliceRaid) => ctx.Get<Company>(o["company"]) is { } rc && ctx.Get<Company>(o["station"]) is { } rs
            ? new PoliceRaid(this, rc, rs) : null,
        _ => null,
    };
}

public partial class CanAttack
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        ForcedTarget = ctx.Get<Node2D>(o["forced"]);
        _targetParent = ctx.Get<Node2D>(o["target"]);
        _aim = (int)o["aim"];
        _lastShot = (long)o["lastShot"];
        _thinkTimer = (double)o["think"];
    }
}

public partial class Organisation
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        Cash = (long)o["cash"];
        TotalIn = (long)o["totalIn"];
        TotalOut = (long)o["totalOut"];
        TotalBribed = (long)o["totalBribed"];
        NetIncome = (long)o["netIncome"];
        GrossIncome = (long)o["grossIncome"];
        Salaries = (long)o["salaries"];
        Fill(Shares, o["shares"]);
        var money = o["authorityMoney"]!.AsArray();
        for (var i = 0; i < AuthorityMoney.Length && i < money.Count; i++)
            AuthorityMoney[i] = (long)money[i];
        Fill(Favour, o["favour"]);
        MayExtort = (bool)o["mayExtort"];
        Eliminated = (bool)o["eliminated"];
        PrimaryCenter = ctx.Get<Company>(o["primaryCenter"]);
        RumourStart = (long)o["rumourStart"];
        InvestigationStart = (long)o["investigationStart"];
        FbiVisitStart = (long)o["fbiVisitStart"];
        UpdateMarketShares();
    }

    private static void Fill(float[] target, JsonNode node)
    {
        var a = node!.AsArray();
        for (var i = 0; i < target.Length && i < a.Count; i++)
            target[i] = (float)a[i];
    }
}

public partial class ComputerPlayer
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        var p = o["parameters"]!.AsArray();
        for (var i = 0; i < Parameters.Length && i < p.Count; i++)
            Parameters[i] = (int)p[i];
        _style = (OriginalBusinessTable.BusinessType)(int)o["style"];
        var d = o["decisions"]!.AsArray();
        _recruitChosen = (bool)d[0];
        _recruitThug = (bool)d[1];
        _convert = (bool)d[2];
        _weaker = (bool)d[3];
        _attackIntruder = (bool)d[4];
        _attackTarget = (bool)d[5];
        _attackHuman = (bool)d[6];
        _intruder = ctx.Get<Unit>(o["intruder"]);
        _target = ctx.Team(o["target"]);
        _seen.Clear();
        foreach (var id in o["seen"]!.AsArray())
            if (ctx.Get<Company>(id) is { } c)
                _seen.Add(c);
        _lastBrownout = (long)o["lastBrownout"];
    }
}

public partial class CityBlock
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        SuspendedUntil = (long)o["suspendedUntil"];
        Owners.Clear();
        foreach (var e in o["owners"]!.AsArray())
        {
            var team = ctx.Team(e!["team"]);
            if (team == null)
                continue;
            Owners[team] = new OwnerEntry
            {
                Team = team,
                Collector = ctx.Get<Unit>(e["collector"]),
                IncomeEnabled = (bool)e["incomeEnabled"],
                LostAt = (long)e["lostAt"],
            };
        }
    }
}

public static partial class Training
{
    public static void LoadState(JsonArray jobs, LoadContext ctx)
    {
        Jobs.Clear();
        foreach (var j in jobs)
        {
            var team = ctx.Team(j!["team"]);
            var recruit = Recruits.FirstOrDefault(r => r.Type.ToString() == (string)j["type"] && r.Slot == (int)j["slot"]);
            if (team == null || recruit == null)
                continue;
            Jobs[team] = new Job
            {
                Team = team, Recruit = recruit, Center = ctx.Get<Company>(j["center"]),
                Start = (long)j["start"], End = (long)j["end"],
            };
        }
    }
}

public partial class VictoryManager
{
    public void LoadState(JsonArray conditions)
    {
        for (var i = 0; i < Conditions.Count && i < conditions.Count; i++)
            Conditions[i].LoadState((bool)conditions[i]!["completed"], (long)conditions[i]["progress"]);
    }
}

public partial class VictoryCondition
{
    public void LoadState(bool completed, long progress)
    {
        IsCompleted = completed;
        CurrentProgress = progress;
    }
}

public partial class Statistics
{
    /// <summary>The saved statistics become the current ones.</summary>
    public static Statistics LoadState(JsonObject o, LoadContext ctx)
    {
        var players = o["players"]!.AsArray().Select(ctx.Team).Where(t => t != null).ToList();
        var stats = new Statistics(players, (long)o["startFunds"])
        {
            Count = (int)o["count"],
            Period = (int)o["period"],
            _counter = (int)o["counter"],
        };
        var series = o["series"]!.AsArray();
        for (var s = 0; s < stats._series.Length && s < series.Count; s++)
        {
            var perPlayer = series[s]!.AsArray();
            for (var p = 0; p < stats._series[s].Count && p < perPlayer.Count; p++)
            {
                var slots = perPlayer[p]!.AsArray();
                for (var k = 0; k < stats._series[s][p].Length && k < slots.Count; k++)
                    stats._series[s][p][k] = (long)slots[k];
            }
        }
        Current = stats;
        return stats;
    }
}

public partial class FogOfWar
{
    public void LoadState(JsonArray cells)
    {
        _lastSeen.Clear();
        foreach (var c in cells)
        {
            var a = c!.AsArray();
            _lastSeen[new Vector2I((int)a[0], (int)a[1])] = (long)a[2];
        }
        Refresh();
    }
}

// ---- orders ---------------------------------------------------------------------------------------------------------

public partial class ExtortOrder
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        SaveId = (string)o["id"];
        _attempt = (int)o["attempt"];
        _walking = (bool)o["walking"];
        _finished = (bool)o["finished"];
    }
}

public partial class PatrolOrder
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        SaveId = (string)o["id"];
        _target = LoadContext.Cell(o["target"]);
        _walking = (bool)o["walking"];
        _finished = (bool)o["finished"];
    }
}

public partial class StealOrder
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        _walking = (bool)o["walking"];
        _finished = (bool)o["finished"];
    }
}

public partial class BoardOrder
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        _walking = (bool)o["walking"];
        _finished = (bool)o["finished"];
        _tries = (int)o["tries"];
    }
}

public partial class CollectorJob
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        SaveId = (string)o["id"];
        _target = ctx.Get<Node2D>(o["target"]);
        _scanIndex = (int)o["scanIndex"];
        _roundStart = (long)o["roundStart"];
        _pickedUp = (bool)o["pickedUp"];
        _walking = (bool)o["walking"];
        _finished = (bool)o["finished"];
        _collector.Died += OnCollectorDied;
    }
}

public partial class PoliceJob
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        _state = (State)(int)o["state"];
        _walking = (bool)o["walking"];
        _arrested = (bool)o["arrested"];
        _finished = (bool)o["finished"];
        _chaseStarted = (long)o["chaseStarted"];
        _chaseCell = LoadContext.Cell(o["chaseCell"]);
    }
}

public partial class PoliceRaid
{
    public void LoadState(JsonObject o, LoadContext ctx)
    {
        SaveId = (string)o["id"];
        _state = (int)o["state"];
        _started = (bool)o["started"];
    }
}
