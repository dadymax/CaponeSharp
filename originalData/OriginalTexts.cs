using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>
/// The EXE's UI texts. <c>fnMessagePrep(group, index)</c> (0x467000) returns
/// <c>groups[group][index * 3 + language]</c>: every entry is an EN/ES/RU triple of string pointers.
/// The 37 groups (table address, entry count) are set up by <c>sub_466DB0</c> (0x466DB0).
/// </summary>
public class OriginalTexts
{
    /// <summary>(table address, entry count) per group, as stored by sub_466DB0.</summary>
    private static readonly (uint Address, int Count)[] Groups =
    {
        (0x49201C, 8), (0x493950, 0x3F), (0x493C68, 1), (0x493D1C, 6), (0x493DC8, 2), (0x494EE0, 0x58),
        (0x495320, 1), (0x495358, 3), (0x4958E4, 0x18), (0x497454, 0x48), (0x4978BC, 1), (0x497964, 2),
        (0x497BE0, 0xA), (0x497C7C, 1), (0x498200, 0x12), (0x4982F0, 1), (0x499E14, 0x4F), (0x49BDA4, 0x32),
        (0x49C014, 1), (0x49C4F8, 0xB), (0x49D234, 0x1E), (0x49D3B8, 1), (0x49D3E8, 1), (0x49D40C, 1),
        (0x49D440, 1), (0x49D878, 0xA), (0x49D908, 1), (0x49D938, 2), (0x49DA18, 2), (0x49DE6C, 0x17),
        (0x49E958, 0x18), (0x49EAC4, 1), (0x49EAE8, 1), (0x49EB24, 1), (0x49EB48, 1), (0x49EB70, 1),
        (0x49EBCC, 1),
    };

    public const string MissingText = "** Error: No text defined! **";

    private static OriginalTexts _instance;
    public static OriginalTexts Instance => _instance ??= new OriginalTexts();

    private readonly Dictionary<(int, int, OriginalExe.Language), string> _cache = new();

    public int GroupCount => Groups.Length;
    public int EntryCount(int group) => group >= 0 && group < Groups.Length ? Groups[group].Count : 0;

    /// <summary>Text (group, index) in the game's text language.</summary>
    public string Get(int group, int index) => Get(group, index, OriginalData.TextLanguage);

    public string Get(int group, int index, OriginalExe.Language language)
    {
        if (group < 0 || group >= Groups.Length || index < 0 || index >= Groups[group].Count)
            return MissingText;
        if (_cache.TryGetValue((group, index, language), out var text))
            return text;
        var exe = OriginalExe.Instance;
        if (!exe.IsLoaded)
            return MissingText;
        var va = Groups[group].Address + (uint)(index * 12 + (int)language * 4);
        if (exe.IsValidAddress(exe.ReadU32(va)))
            text = exe.ReadStringPointer(va);
        else if (language != OriginalExe.Language.English)
            text = Get(group, index, OriginalExe.Language.English); // some groups (Legal Crime lessons, help) have no RU/ES
        else
            text = MissingText;
        _cache[(group, index, language)] = text;
        return text;
    }

    /// <summary>True if the entry has its own text in <paramref name="language"/> (no English fallback).</summary>
    public bool HasTranslation(int group, int index, OriginalExe.Language language)
    {
        var exe = OriginalExe.Instance;
        if (!exe.IsLoaded || group < 0 || group >= Groups.Length || index < 0 || index >= Groups[group].Count)
            return false;
        return exe.IsValidAddress(exe.ReadU32(Groups[group].Address + (uint)(index * 12 + (int)language * 4)));
    }

    /// <summary>All entries, for checks and exploration.</summary>
    public IEnumerable<(int Group, int Index, string Text)> All(OriginalExe.Language language)
    {
        for (var g = 0; g < Groups.Length; g++)
            for (var i = 0; i < Groups[g].Count; i++)
                yield return (g, i, Get(g, i, language));
    }
}
