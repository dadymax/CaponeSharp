using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace Capone;

// Save / load step 2: what each part of the game writes into the save file (SaveGame.Capture). Ids: SaveJson.Id;
// teams by colour (SaveJson.Team); catalogue rows by index; cells as [x, y].

public partial class Company
{
    public JsonObject SaveState() => new()
    {
        ["id"] = SaveId,
        ["cell"] = SaveJson.Cell(Cell),
        ["kind"] = IsHq ? "hq" : IsPoliceStation ? "police" : IsEconomic ? "business" : "building",
        ["block"] = Block?.Number ?? -1,
        ["row"] = Entry?.Index ?? -1,
        ["name"] = CompanyName,
        ["owner"] = SaveJson.Team(OwnerTeam),
        ["hp"] = Hp,
        ["maxHp"] = MaxHp,
        ["uncollected"] = Uncollected,
        ["percent"] = Percent,
        ["cachedIncome"] = CachedIncomeEnabled,
        ["building"] = Building?.Index ?? -1,
        ["buildStart"] = BuildStart,
        ["buildEnd"] = BuildEnd,
        ["conversionOwner"] = SaveJson.Team(_conversionOwner),
        ["repairing"] = Repairing,
        ["discovered"] = Discovered,
        ["inFog"] = _inFog,
        ["seenOwner"] = SaveJson.Team(_seenOwner),
        ["seenRow"] = _seenEntry?.Index ?? -1,
        ["economyStarted"] = _economyStarted,
    };
}

public partial class Unit
{
    public JsonObject SaveState()
    {
        var o = new JsonObject
        {
            ["id"] = SaveId,
            ["type"] = UnitType.ToString(),
            ["owner"] = SaveJson.Team(OwnerTeam),
            ["pos"] = SaveJson.Pos(Position),
            ["name"] = PersonalName,
            ["epiA"] = EpiFlagA,
            ["epiB"] = EpiFlagB,
            ["hp"] = CanBeAttacked?.HP ?? 0,
            ["maxHp"] = CanBeAttacked?.MaxHP ?? 0,
            ["salary"] = Salary,
            ["money"] = CarriedMoney,
            ["arrested"] = IsArrested,
            ["deserter"] = IsDeserter,
            ["rogue"] = IsRogue,
            ["snitch"] = IsSnitch,
            ["inside"] = InsideBuilding,
            ["disguise"] = DisguiseMinute,
            ["lastShot"] = LastShotMinute,
            ["lastFought"] = LastFoughtMinute,
            ["stance"] = Stance,
            ["moveMode"] = MoveMode ?? -1,
            ["moving"] = _moving,
            ["moveTarget"] = SaveJson.Cell(_moveTarget),
            ["moveTargetNode"] = SaveJson.Id(_moveTargetNode),
            ["lineStart"] = SaveJson.Cell(_lineStart),
            ["orderTarget"] = SaveJson.Id(_orderTarget),
            ["targetStructure"] = SaveJson.Id(_targetStructure),
            ["vehicle"] = SaveJson.Id(Vehicle),
            ["seats"] = new JsonArray(_seats.Select(s => (JsonNode)SaveJson.Id(s)).ToArray()),
            ["attack"] = CanAttack?.SaveState(),
            ["order"] = CurrentOrder is ISaveableOrder order && IsInstanceValid(CurrentOrder) ? order.SaveState() : null,
        };
        if (CurrentOrder != null && CurrentOrder is not ISaveableOrder)
            GD.PushWarning($"SaveGame: {DisplayName}'s order {CurrentOrder.GetType().Name} can't be saved");
        return o;
    }
}

public partial class CanAttack
{
    public JsonObject SaveState() => new()
    {
        ["target"] = SaveJson.Id(TargetParent),
        ["forced"] = SaveJson.Id(ForcedTarget),
        ["aim"] = _aim,
        ["lastShot"] = _lastShot,
        ["think"] = _thinkTimer,
    };
}

public partial class Organisation
{
    public JsonObject SaveState() => new()
    {
        ["cash"] = Cash,
        ["startCash"] = StartCash,
        ["totalIn"] = TotalIn,
        ["totalOut"] = TotalOut,
        ["totalBribed"] = TotalBribed,
        ["netIncome"] = NetIncome,
        ["grossIncome"] = GrossIncome,
        ["salaries"] = Salaries,
        ["shares"] = SaveJson.Floats(Shares),
        ["authorityMoney"] = SaveJson.Longs(AuthorityMoney),
        ["favour"] = SaveJson.Floats(Favour),
        ["mayExtort"] = MayExtort,
        ["eliminated"] = Eliminated,
        ["primaryCenter"] = SaveJson.Id(PrimaryCenter),
        ["rumourStart"] = RumourStart,
        ["investigationStart"] = InvestigationStart,
        ["fbiVisitStart"] = FbiVisitStart,
    };
}

public partial class ComputerPlayer
{
    public JsonObject SaveState() => new()
    {
        ["parameters"] = SaveJson.Ints(Parameters),
        ["style"] = (int)_style,
        ["decisions"] = new JsonArray(_recruitChosen, _recruitThug, _convert, _weaker, _attackIntruder, _attackTarget, _attackHuman),
        ["intruder"] = SaveJson.Id(_intruder),
        ["target"] = SaveJson.Team(_target),
        ["seen"] = new JsonArray(_seen.Where(GodotObject.IsInstanceValid).Select(c => (JsonNode)c.SaveId).ToArray()),
        ["lastBrownout"] = _lastBrownout,
    };
}

public partial class CityBlock
{
    public JsonObject SaveState() => new()
    {
        ["number"] = Number,
        ["suspendedUntil"] = SuspendedUntil,
        ["owners"] = new JsonArray(Owners.Values.Select(e => (JsonNode)new JsonObject
        {
            ["team"] = SaveJson.Team(e.Team),
            ["collector"] = SaveJson.Id(e.Collector),
            ["incomeEnabled"] = e.IncomeEnabled,
            ["lostAt"] = e.LostAt,
        }).ToArray()),
    };
}

public static partial class Training
{
    public static JsonArray SaveState() => new(Jobs.Values.Select(j => (JsonNode)new JsonObject
    {
        ["team"] = SaveJson.Team(j.Team),
        ["type"] = j.Recruit.Type.ToString(),
        ["slot"] = j.Recruit.Slot,
        ["center"] = SaveJson.Id(j.Center),
        ["start"] = j.Start,
        ["end"] = j.End,
    }).ToArray());
}

public partial class VictoryManager
{
    public JsonArray SaveState() => new(Conditions.Select(c => (JsonNode)new JsonObject
    {
        ["type"] = c.Type.ToString(),
        ["player"] = c.TargetPlayer,
        ["completed"] = c.IsCompleted,
        ["progress"] = c.CurrentProgress,
    }).ToArray());
}

public partial class Statistics
{
    public JsonObject SaveState() => new()
    {
        ["players"] = new JsonArray(Players.Select(p => (JsonNode)SaveJson.Team(p)).ToArray()),
        ["count"] = Count,
        ["period"] = Period,
        ["counter"] = _counter,
        ["startFunds"] = StartFunds,
        ["series"] = new JsonArray(_series.Select(series => (JsonNode)new JsonArray(
            series.Select(player => (JsonNode)SaveJson.Longs(player)).ToArray())).ToArray()),
    };
}

public partial class FogOfWar
{
    /// <summary>Cells seen lately (cell → game minute last seen); companies keep their own last-seen state.</summary>
    public JsonArray SaveState() => new(_lastSeen.Select(kv => (JsonNode)new JsonArray(kv.Key.X, kv.Key.Y, kv.Value)).ToArray());
}

// ---- orders and jobs on units -------------------------------------------------------------------------------------

public partial class ExtortOrder : ISaveableOrder
{
    public JsonObject SaveState() => new()
    {
        ["type"] = nameof(ExtortOrder), ["id"] = SaveId, ["company"] = SaveJson.Id(_company),
        ["attempt"] = _attempt, ["walking"] = _walking, ["finished"] = _finished,
    };
}

public partial class PatrolOrder : ISaveableOrder
{
    public JsonObject SaveState() => new()
    {
        ["type"] = nameof(PatrolOrder), ["id"] = SaveId, ["a"] = SaveJson.Cell(_a), ["b"] = SaveJson.Cell(_b),
        ["target"] = SaveJson.Cell(_target), ["walking"] = _walking, ["finished"] = _finished,
    };
}

public partial class StealOrder : ISaveableOrder
{
    public JsonObject SaveState() => new()
    {
        ["type"] = nameof(StealOrder), ["target"] = SaveJson.Id(_target), ["walking"] = _walking, ["finished"] = _finished,
    };
}

public partial class BoardOrder : ISaveableOrder
{
    public JsonObject SaveState() => new()
    {
        ["type"] = nameof(BoardOrder), ["car"] = SaveJson.Id(_car), ["walking"] = _walking, ["finished"] = _finished,
        ["tries"] = _tries,
    };
}

public partial class CollectorJob : ISaveableOrder
{
    public JsonObject SaveState() => new()
    {
        ["type"] = nameof(CollectorJob), ["id"] = SaveId, ["block"] = _block?.Number ?? -1,
        ["target"] = SaveJson.Id(_target), ["scanIndex"] = _scanIndex, ["roundStart"] = _roundStart,
        ["pickedUp"] = _pickedUp, ["walking"] = _walking, ["finished"] = _finished,
    };
}

public partial class PoliceJob : ISaveableOrder
{
    public JsonObject SaveState() => new()
    {
        ["type"] = nameof(PoliceJob), ["gangster"] = SaveJson.Id(_gangster), ["station"] = SaveJson.Id(_station),
        ["lawyers"] = _lawyersCanHelp, ["state"] = (int)_state, ["walking"] = _walking, ["arrested"] = _arrested,
        ["finished"] = _finished, ["chaseStarted"] = _chaseStarted, ["chaseCell"] = SaveJson.Cell(_chaseCell),
    };
}

public partial class PoliceRaid : ISaveableOrder
{
    public JsonObject SaveState() => new()
    {
        ["type"] = nameof(PoliceRaid), ["id"] = SaveId, ["company"] = SaveJson.Id(_company),
        ["station"] = SaveJson.Id(_station), ["state"] = _state, ["started"] = _started,
    };
}
