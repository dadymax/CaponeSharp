using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Random name lists of fnGetRandomParameters (0x43B000): list 1 gangsters (units), 2 company names, 3–5 more
/// business names, 6 bosses. The original tries up to 5 times to pick a name nobody uses yet.
/// </summary>
public static class RandomNames
{
    private static readonly (uint Address, int Count)[] Lists =
    {
        (0, 0),
        (0x48C590, 198), // 1: gangsters ("Джон Гамбино"…)
        (0x48C95C, 8),   // 2: companies ("Лавка Джека"…)
        (0x48C9F0, 10),  // 3
        (0x48CB68, 28),  // 4
        (0x48CC84, 16),  // 5
        (0x48C8EC, 5),   // 6: bosses ("Витто Грандо"…)
    };

    private static readonly Dictionary<int, List<string>> Cache = new();
    private static readonly HashSet<string> Used = new();

    public static IReadOnlyList<string> List(int list)
    {
        if (Cache.TryGetValue(list, out var names))
            return names;
        names = new List<string>();
        var exe = OriginalExe.Instance;
        if (exe.IsLoaded && list > 0 && list < Lists.Length)
            for (var i = 0; i < Lists[list].Count; i++)
                names.Add(exe.ReadStringPointer(Lists[list].Address + (uint)(i * 4)));
        Cache[list] = names;
        return names;
    }

    /// <summary>Random name from a list, avoiding names in use (up to 5 tries, as the original).</summary>
    public static string Pick(int list)
    {
        var names = List(list);
        if (names.Count == 0)
            return "";
        var name = names[GD.RandRange(0, names.Count - 1)];
        for (var tries = 1; tries < 5 && Used.Contains(name); tries++)
            name = names[GD.RandRange(0, names.Count - 1)];
        Used.Add(name);
        return name;
    }

    public static string Gangster() => Pick(1);
    public static string Company() => Pick(2);

    public static void Reset() => Used.Clear();
}

/// <summary>Plays the original sound effects (Sound/*.wav) by file name. Sound ids follow the EXE table at 0x478700.</summary>
public static class Sound
{
    public static readonly string[] BusinessPay =
        { "BusinessPay1.wav", "BusinessPay2.wav", "BusinessPay3.wav", "BusinessPay4.wav", "BusinessPay5.wav", "BusinessPay6.wav" };
    public static readonly string[] BusinessRefuse =
        { "BusinessRefuse1.wav", "BusinessRefuse2.wav", "BusinessRefuse3.wav", "BusinessRefuse4.wav", "BusinessRefuse5.wav" };

    public const int MaxVoices = 16;

    /// <summary>Set false to mute (tests).</summary>
    public static bool Enabled { get; set; } = true;

    private static readonly Dictionary<string, AudioStreamWav> Streams = new();
    private static Node _host;

    public static string RandomOf(string[] files) => files[GD.RandRange(0, files.Length - 1)];

    /// <summary>The game's sound table (off_478700, 205 entries; sub_408A92 plays by index). Null for empty slots.</summary>
    public const uint TableAddress = 0x478700;
    public const int TableSize = 205;
    private static string[] _table;

    public static string FileOf(int id)
    {
        if (_table == null)
        {
            _table = new string[TableSize];
            var exe = OriginalExe.Instance;
            if (exe.IsLoaded)
                for (var i = 0; i < TableSize; i++)
                {
                    var p = exe.ReadU32(TableAddress + (uint)(i * 4));
                    _table[i] = p != 0 && exe.IsValidAddress(p) ? exe.ReadCString(p) : null;
                }
        }
        return id >= 0 && id < TableSize ? _table[id] : null;
    }

    /// <summary>sub_408A92(id): plays entry <paramref name="id"/> of the sound table (nothing for an empty slot).</summary>
    public static void PlayId(int id)
    {
        var file = FileOf(id);
        if (!string.IsNullOrEmpty(file))
            Play(file);
    }

    public static void Play(string file)
    {
        if (!Enabled || DisplayServer.GetName() == "headless")
            return;
        var stream = Load(file);
        if (stream == null)
            return;
        if (_host == null || !GodotObject.IsInstanceValid(_host))
        {
            _host = new Node { Name = "SoundHost" };
            (Engine.GetMainLoop() as SceneTree)?.Root.CallDeferred(Node.MethodName.AddChild, _host);
        }
        // At most MaxVoices at once (a big fight fires many shots per game hour).
        if (_host.GetChildCount() >= MaxVoices)
            return;
        var player = new AudioStreamPlayer { Stream = stream };
        player.Finished += player.QueueFree;
        _host.CallDeferred(Node.MethodName.AddChild, player);
        player.CallDeferred(AudioStreamPlayer.MethodName.Play);
    }

    private static AudioStreamWav Load(string file)
    {
        if (Streams.TryGetValue(file, out var stream))
            return stream;
        var path = OriginalData.Instance.OriginalGameDirectory + "/Sound/" + file;
        stream = System.IO.File.Exists(path) ? AudioStreamWav.LoadFromFile(path) : null;
        if (stream == null)
            GD.PrintErr("Sound: cannot load ", path);
        Streams[file] = stream;
        return stream;
    }
}

/// <summary>
/// Game messages for the local player (sub_408A70 → sub_41D3E0 → the view's list, sub_40FFD0): a kind, the text and a
/// map position (the given one, else the local HQ's). Kind 4 plays Message.wav; <see cref="MessageLog"/> shows the
/// messages of the last 15 seconds (kinds 2 / 3 are the episode results: the end screen shows those).
/// </summary>
public static class Messages
{
    public const int Normal = 0, Warning = 1, Lost = 2, Won = 3, Good = 4;

    public record Entry(string Text, int Kind, Vector2I? Cell, ulong Ticks);

    public static List<Entry> Log { get; } = new();
    public static List<string> History { get; } = new();
    public static event System.Action<string> Posted;

    public static void Show(string text, int kind = Normal, Vector2I? cell = null)
    {
        History.Add(text);
        if (History.Count > 50)
            History.RemoveAt(0);
        Log.Add(new Entry(text, kind, cell, Time.GetTicksMsec()));
        if (Log.Count > 50)
            Log.RemoveAt(0);
        if (kind == Good)
            Sound.Play("Message.wav");
        GD.Print("Message: ", text);
        Posted?.Invoke(text);
    }

    public static string Last => History.LastOrDefault() ?? "";
}
