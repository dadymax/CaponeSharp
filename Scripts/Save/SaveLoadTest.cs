using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace Capone;

/// <summary>
/// Save / load round trip (TODO item 20, step 3): <c>-- --save-load-test[=episode] [--hours=N]</c> plays the episode
/// for N game hours (default 60) fast, saves (F5's code), loads it again through the F8 path (the scene reloads and
/// the episode is rebuilt from the file), captures the state at once and compares it with the saved one, then plays
/// two more game days to see the loaded game run without failed events. Exit code 0 = no differences.
/// </summary>
public partial class SaveLoadTest : Node
{
    private static int _phase; // 0 playing before the save, 1 loaded
    private static JsonObject _before;
    private static int _failures;

    public static bool Requested(string[] args) => args.Any(a => a == "--save-load-test" || a.StartsWith("--save-load-test="));

    public static int Episode(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith("--save-load-test="));
        return arg != null && int.TryParse(arg["--save-load-test=".Length..], out var n) ? n : 7;
    }

    private static int Hours(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith("--hours="));
        return arg != null && int.TryParse(arg["--hours=".Length..], out var n) ? n : 60;
    }

    private long _endMinute = -1;
    private int _errorsAtLoad;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var clock = GameClock.Instance;
        clock.Speed = 32;
        if (_phase == 1)
        {
            var after = SaveGame.Capture();
            Compare(_before, after);
            _errorsAtLoad = GameClock.EventErrors;
            _endMinute = clock.Minute + 2 * GameClock.MinutesPerDay;
            clock.Speed = 32;
        }
    }

    public override void _Process(double delta)
    {
        var clock = GameClock.Instance;
        if (_phase == 0)
        {
            if (clock.Minute < Hours(OS.GetCmdlineUserArgs()) * GameClock.MinutesPerHour)
                return;
            _before = SaveGame.Capture();
            SaveGame.Save();
            _phase = 1;
            GD.Print($"SaveLoadTest: saved at {GameClock.Format(clock.Minute)}, loading");
            if (Main.Instance?.QuickLoad() != true)
            {
                Fail("F8 didn't load the save");
                Quit();
            }
            SetProcess(false);
            return;
        }
        if (_endMinute >= 0 && clock.Minute >= _endMinute)
        {
            if (GameClock.EventErrors > _errorsAtLoad)
                Fail($"{GameClock.EventErrors - _errorsAtLoad} game events failed after loading");
            if (clock.ClosureEvents > 0)
                Fail($"{clock.ClosureEvents} closure events after loading");
            GD.Print($"SaveLoadTest: played on to {GameClock.Format(clock.Minute)} after loading");
            Quit();
        }
    }

    private void Quit()
    {
        GD.Print($"=== Save/load test done: {_failures} failure(s) ===");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static void Fail(string text)
    {
        _failures++;
        GD.Print("FAIL " + text);
    }

    // ---- comparison -------------------------------------------------------------------------------------------------

    private static readonly HashSet<string> Ignored = new() { "saved", "speed", "fog" };

    private static void Compare(JsonObject before, JsonObject after)
    {
        if (before == null || after == null)
        {
            Fail("no state to compare");
            return;
        }
        var diffs = new List<string>();
        foreach (var (key, value) in before)
        {
            if (Ignored.Contains(key))
                continue;
            Diff(key, Normalize(key, value), Normalize(key, after[key]), diffs);
        }
        GD.Print($"SaveLoadTest: {before["units"]!.AsArray().Count} units, {before["companies"]!.AsArray().Count} companies, "
                 + $"{before["events"]!.AsArray().Count} events compared; fog cells {before["fog"]?.AsArray().Count} / {after["fog"]?.AsArray().Count}");
        foreach (var d in diffs.Take(40))
            Fail(d);
        if (diffs.Count > 40)
            Fail($"... {diffs.Count - 40} more differences");
    }

    /// <summary>Lists in a stable order: objects by "id" (or "number", "color"), events by time, target, kind, arg.</summary>
    private static JsonNode Normalize(string key, JsonNode node)
    {
        if (node is not JsonArray array)
            return node;
        var items = array.Select(n => n?.DeepClone()).ToList();
        string Key(JsonNode n) => key == "events"
            ? $"{(long)n!["minute"]:D12}|{n["target"]}|{n["kind"]}|{n["arg"]}"
            : n is JsonObject o ? (o["id"] ?? o["number"] ?? o["color"] ?? o["team"])?.ToJsonString() ?? o.ToJsonString() : n?.ToJsonString() ?? "";
        if (items.All(n => n is JsonObject) && key is "units" or "companies" or "blocks" or "teams" or "events" or "training")
            items = items.OrderBy(Key, StringComparer.Ordinal).ToList();
        return new JsonArray(items.ToArray());
    }

    private static void Diff(string path, JsonNode a, JsonNode b, List<string> diffs)
    {
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var (k, v) in oa)
                Diff($"{path}.{k}", v, ob[k], diffs);
            foreach (var (k, _) in ob)
                if (!oa.ContainsKey(k))
                    diffs.Add($"{path}.{k}: only after loading");
            return;
        }
        if (a is JsonArray aa && b is JsonArray ab)
        {
            if (aa.Count != ab.Count)
            {
                diffs.Add($"{path}: {aa.Count} items before, {ab.Count} after");
                return;
            }
            for (var i = 0; i < aa.Count; i++)
                Diff($"{path}[{Label(aa[i], i)}]", aa[i], ab[i], diffs);
            return;
        }
        var sa = a?.ToJsonString() ?? "null";
        var sb = b?.ToJsonString() ?? "null";
        if (sa != sb && !NearlyEqual(a, b))
            diffs.Add($"{path}: {sa} -> {sb}");
    }

    private static string Label(JsonNode n, int i) => n is JsonObject o && (o["id"] ?? o["number"] ?? o["color"]) is { } id ? id.ToJsonString().Trim('"') : i.ToString();

    private static bool NearlyEqual(JsonNode a, JsonNode b)
    {
        try
        {
            return a is JsonValue va && b is JsonValue vb && Math.Abs(va.GetValue<double>() - vb.GetValue<double>()) < 1e-3;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
