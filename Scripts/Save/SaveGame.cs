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
