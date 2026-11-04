using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace Capone;

/// <summary>An order or job on a unit that can be written to a save file.</summary>
public interface ISaveableOrder
{
    /// <summary>The order's state; "type" names the order class.</summary>
    JsonObject SaveState();

    /// <summary>Loading: the rest of the state (the constructor got the unit and target).</summary>
    void LoadState(JsonObject o, LoadContext ctx);
}

/// <summary>JSON helpers for the save file.</summary>
public static class SaveJson
{
    public static JsonArray Cell(Vector2I v) => new(v.X, v.Y);

    public static JsonArray Pos(Vector2 v) => new(v.X, v.Y);

    public static string Team(Team team) => team?.TeamColor.ToHtml();

    public static string Id(object o) => o switch
    {
        null => null,
        GodotObject g when !GodotObject.IsInstanceValid(g) => null,
        IScheduled s => s.SaveId,
        _ => null,
    };

    public static JsonArray Longs(IEnumerable<long> values) => new(values.Select(v => (JsonNode)v).ToArray());

    public static JsonArray Floats(IEnumerable<float> values) => new(values.Select(v => (JsonNode)v).ToArray());

    public static JsonArray Ints(IEnumerable<int> values) => new(values.Select(v => (JsonNode)v).ToArray());
}

/// <summary>
/// Quick save (TODO item 20, step 2): F5 writes the whole running episode to <see cref="Path"/> — one slot, no menu.
/// Every part of the game writes its own state (SaveState methods); the pending clock events are saved as data
/// (step 1, <see cref="GameClock.SnapshotEvents"/>). Step 3 (F8) rebuilds the episode from this file.
/// </summary>
public static class SaveGame
{
    public const string Path = "user://quicksave.json";
    public const int Version = 1;

    /// <summary>A save being loaded: the next episode start rebuilds it (set by F8 before the scene reloads).</summary>
    public static JsonObject PendingLoad { get; set; }

    /// <summary>True while a save is applied: new orders and units don't start their own walks and events.</summary>
    public static bool Restoring { get; set; }

    /// <summary>The running episode's state as JSON, or null when no episode runs.</summary>
    public static JsonObject Capture()
    {
        var main = Main.Instance;
        var clock = GameClock.Instance;
        if (main?.Map == null || clock == null || Engine.GetMainLoop() is not SceneTree tree)
            return null;

        var companies = tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(GodotObject.IsInstanceValid).ToList();
        var units = tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
            .Where(u => GodotObject.IsInstanceValid(u) && !u.IsDead && !u.IsQueuedForDeletion()).ToList();

        var root = new JsonObject
        {
            ["version"] = Version,
            ["saved"] = DateTime.Now.ToString("s"),
            ["episode"] = main.Episode,
            ["minutes"] = clock.Minutes,
            ["speed"] = clock.Speed,
            ["saveIds"] = SaveIds.Current,
            ["map"] = CampaignManager.Instance?.CurrentScenario?.MapFile,
            ["world"] = new JsonObject
            {
                ["legalization"] = SaveJson.Ints(Enumerable.Range(0, Organisation.TypeCount).Select(World.LegalizationLevel)),
                ["rogueUnits"] = Organisation.RogueUnits,
                ["allianceVictory"] = Organisation.AllianceVictory,
            },
            ["teams"] = new JsonArray(Teams.TeamsInGame.Values.Select(t => (JsonNode)SaveTeam(t)).ToArray()),
            ["companies"] = new JsonArray(companies.Select(c => (JsonNode)c.SaveState()).ToArray()),
            ["blocks"] = new JsonArray(CityBlock.All.Select(b => (JsonNode)b.SaveState()).ToArray()),
            ["units"] = new JsonArray(units.Select(u => (JsonNode)u.SaveState()).ToArray()),
            ["training"] = Training.SaveState(),
            ["victory"] = VictoryManager.Instance?.SaveState(),
            ["statistics"] = Statistics.Current?.SaveState(),
            ["fog"] = FogOfWar.Instance?.SaveState(),
            // Dead units aren't saved (their bodies would vanish anyway), nor are their events (corpse removal, payday).
            ["events"] = new JsonArray(clock.SnapshotEvents(t => t is not Unit { IsDead: true }).Select(e => (JsonNode)new JsonObject
            {
                ["minute"] = e.Minute, ["target"] = e.Target, ["kind"] = e.Kind, ["arg"] = e.Arg,
            }).ToArray()),
        };
        return root;
    }

    private static JsonObject SaveTeam(Team t)
    {
        var relations = new JsonObject();
        foreach (var (color, relation) in t.TeamRelation)
            relations[color.ToHtml()] = relation.ToString();
        return new JsonObject
        {
            ["color"] = SaveJson.Team(t),
            ["id"] = t.PlayerId,
            ["kind"] = t.Kind,
            ["name"] = t.PlayerName,
            ["local"] = t.IsCurrentPlayerColor,
            ["hasBase"] = t.HasBase,
            ["hq"] = SaveJson.Id(t.HqCompany),
            ["hqCoords"] = SaveJson.Cell(t.HqCoords),
            ["relations"] = relations,
            ["org"] = t.Org?.SaveState(),
            ["ai"] = t.Ai?.SaveState(),
        };
    }

    /// <summary>Reads the save file, or null (no file, unreadable, other version).</summary>
    public static JsonObject Read()
    {
        if (!FileAccess.FileExists(Path))
            return null;
        try
        {
            var state = JsonNode.Parse(FileAccess.GetFileAsString(Path)) as JsonObject;
            if (state == null || (int?)state["version"] != Version)
            {
                GD.PrintErr($"SaveGame: {Path} has another format version");
                return null;
            }
            return state;
        }
        catch (JsonException e)
        {
            GD.PrintErr($"SaveGame: {Path} can't be read ({e.Message})");
            return null;
        }
    }

    /// <summary>Before the map is built: the saved businesses and HQs go to the map generator.</summary>
    public static void PrepareMap(JsonObject state)
    {
        var businesses = new Dictionary<Vector2I, (int, string)>();
        var hqs = new List<Vector2I>();
        foreach (var c in state["companies"]!.AsArray())
        {
            var cell = LoadContext.Cell(c!["cell"]);
            switch ((string)c["kind"])
            {
                case "business":
                    businesses[cell] = ((int)c["row"], (string)c["name"]);
                    break;
                case "hq":
                    hqs.Add(cell);
                    // An HQ took over the business on its cell (it keeps that block and row): build it the same way.
                    if ((int)c["row"] >= 0)
                        businesses[cell] = ((int)c["row"], (string)c["name"]);
                    break;
            }
        }
        TileMapGenerator.LayoutBusinesses = businesses;
        TileMapGenerator.LayoutHqCells = hqs;
    }

    /// <summary>
    /// F8, after the map, the players and the computer players exist (no starting units, no fresh events): puts the
    /// saved state back (clock, world, teams, HQs, companies, units and their orders, blocks, training, victory
    /// progress, statistics, fog) and schedules the saved events again.
    /// </summary>
    public static void Apply(JsonObject state, SceneTree tree)
    {
        var clock = GameClock.Instance;
        var campaign = CampaignManager.Instance;
        Restoring = true;
        try
        {
            clock.RestoreTime((double)state["minutes"]);
            clock.Speed = (float)(double)state["speed"];
            var world = state["world"]!.AsObject();
            var legal = world["legalization"]!.AsArray();
            for (var i = 0; i < legal.Count; i++)
                World.SetLegalization(i, (int)legal[i]);
            Organisation.RogueUnits = (bool)world["rogueUnits"];
            Organisation.AllianceVictory = (bool)world["allianceVictory"];

            var ctx = new LoadContext();
            foreach (var team in Teams.TeamsInGame.Values)
                ctx.TeamByColor[team.TeamColor.ToHtml()] = team;
            foreach (var c in tree.GetNodesInGroup(Company.GroupName).OfType<Company>())
                ctx.ById[c.SaveId] = c;
            foreach (var b in CityBlock.All)
                ctx.ById[b.SaveId] = b;

            // Teams: alliances and HQs first (company owners and unit sprites depend on them).
            foreach (var t in state["teams"]!.AsArray())
            {
                var team = ctx.Team(t!["color"]);
                if (team == null)
                    continue;
                foreach (var (color, relation) in t["relations"]!.AsObject())
                    if (System.Enum.TryParse<Teams.TeamRelation>((string)relation, out var r))
                        team.TeamRelation[Color.FromHtml(color)] = r;
                team.HqCoords = LoadContext.Cell(t["hqCoords"]);
                team.HqCompany = ctx.Get<Company>(t["hq"]);
                team.HqCompany?.MakeHq(team);
                if (team.Org != null)
                    ctx.ById[team.Org.SaveId] = team.Org;
                if (team.Ai != null)
                    ctx.ById[team.Ai.SaveId] = team.Ai;
            }

            foreach (var c in state["companies"]!.AsArray())
                if (ctx.Get<Company>(c!["id"]) is { } company)
                    company.LoadState(c.AsObject(), ctx);

            // Units: create them all, then link cars, targets and orders.
            var saved = new List<(Unit Unit, JsonObject State)>();
            foreach (var u in state["units"]!.AsArray())
            {
                if (!System.Enum.TryParse<UnitCommon.UnitType>((string)u!["type"], out var type))
                    continue;
                var team = ctx.Team(u["owner"]);
                var unit = campaign.CreateUnit(type, team, campaign.TileMap.LocalToMap(LoadContext.Pos(u["pos"])));
                unit.LoadState(u.AsObject());
                ctx.ById[unit.SaveId] = unit;
                saved.Add((unit, u.AsObject()));
            }
            foreach (var (unit, u) in saved)
            {
                unit.LoadLinks(u, ctx);
                if (unit.CurrentOrder is IScheduled order)
                    ctx.ById[order.SaveId] = order;
            }

            foreach (var t in state["teams"]!.AsArray())
            {
                var team = ctx.Team(t!["color"]);
                if (t["org"] is JsonObject org)
                    team?.Org?.LoadState(org, ctx);
                if (t["ai"] is JsonObject ai)
                    team?.Ai?.LoadState(ai, ctx);
            }
            foreach (var b in state["blocks"]!.AsArray())
                CityBlock.All.FirstOrDefault(x => x.Number == (int)b!["number"])?.LoadState(b.AsObject(), ctx);
            Training.LoadState(state["training"]!.AsArray(), ctx);
            if (state["victory"] is JsonArray victory)
                VictoryManager.Instance?.LoadState(victory);
            if (state["statistics"] is JsonObject stats)
                Statistics.LoadState(stats, ctx);
            if (state["fog"] is JsonArray fog)
                FogOfWar.Instance?.LoadState(fog);

            ctx.ById["training"] = Training.Scheduler;
            if (Statistics.Current != null)
                ctx.ById["stats"] = Statistics.Current;
            if (VictoryManager.Instance != null)
                ctx.ById["victory"] = VictoryManager.Instance;
            var lost = 0;
            foreach (var e in state["events"]!.AsArray())
            {
                if (ctx.Get<IScheduled>(e!["target"]) is { } target)
                    clock.Schedule((long)e["minute"], target, (string)e["kind"], (long)e["arg"]);
                else
                    lost++;
            }
            if (lost > 0)
                GD.PushWarning($"SaveGame: {lost} saved events had no target");
            SaveIds.Reset((long)state["saveIds"]);
            GD.Print($"SaveGame: loaded episode {state["episode"]} at {GameClock.Format(clock.Minute)}: "
                     + $"{saved.Count} units, {state["events"]!.AsArray().Count - lost} events");
        }
        finally
        {
            Restoring = false;
            TileMapGenerator.LayoutBusinesses = null;
            TileMapGenerator.LayoutHqCells = null;
        }
    }

    /// <summary>F5: writes the save file. Returns whether it was written.</summary>
    public static bool Save()
    {
        var state = Capture();
        if (state == null)
            return false;
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PrintErr($"SaveGame: can't write {Path} ({FileAccess.GetOpenError()})");
            return false;
        }
        file.StoreString(state.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        GD.Print($"SaveGame: saved episode {state["episode"]} at {GameClock.Format(GameClock.Instance.Minute)} to {ProjectSettings.GlobalizePath(Path)}");
        return true;
    }
}
