using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

namespace Capone;

/// <summary>
/// Parser for the original .epi episode files (CP1251, CRLF), modelled on the EXE's reader sub_4670A5 (0x4670A5)
/// and its text-block reader sub_4644A1:
/// <code>
/// EPI_VER10
/// FUNDS n · MAP file · RANDOM 0|1
/// PLAYER id kind hasBase name…          (repeated)
/// VCOND type player values…             (repeated, value count depends on type)
/// UNIT type player x y flagA flagB [name…]  (repeated)
/// ALLIANCE a b                          (repeated)
/// BRIBEADVANCE player authority points money (repeated)
/// SCENARIONAME/STORYTEXT/TIPTEXT/OBJECTIVETEXT/SUCCESSTEXT/FAILURETEXT {…}
/// </code>
/// The EXE reads the keywords in that order; this parser accepts any order but reports what the original would reject.
/// </summary>
public class EpiParser
{
    public const string VersionTag = "EPI_VER10";

    private static readonly string[] TextKeywords =
        { "SCENARIONAME", "STORYTEXT", "TIPTEXT", "OBJECTIVETEXT", "SUCCESSTEXT", "FAILURETEXT" };

    /// <summary>Keyword order of the EXE's reader.</summary>
    private static readonly string[] KeywordOrder =
        { "FUNDS", "MAP", "RANDOM", "PLAYER", "VCOND", "UNIT", "ALLIANCE", "BRIBEADVANCE" };

    /// <summary>Number of integer values after "VCOND type player" (from the per-type fscanf formats at 0x4678EA…0x467BFA).</summary>
    private static readonly Dictionary<int, int> VcondIntCount = new()
    {
        [1] = 1, // money
        [2] = 2,
        [3] = 1, // unit type
        [4] = 1, // unit type
        [5] = 1, // float share, then company type
        [6] = 1,
        [7] = 4, // x y size unitType
        [8] = 6, // x1 y1 x2 y2 type count
        [9] = 2,
    };

    private Scenario _scenario;
    private int _lastKeywordIndex = -1;

    /// <summary>Parses an .epi file. Path may be absolute or res://.</summary>
    public Scenario Parse(string filePath)
    {
        _scenario = new Scenario { FilePath = filePath, EpisodeNumber = EpisodeNumberOf(filePath) };
        _lastKeywordIndex = -1;

        byte[] bytes;
        try
        {
            var path = filePath.StartsWith("res://") || filePath.StartsWith("user://")
                ? ProjectSettings.GlobalizePath(filePath)
                : filePath;
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            Error($"Couldn't open episode file: {e.Message}");
            return _scenario;
        }

        ParseText(OriginalExe.Cp1251.GetString(bytes).Replace("\r\n", "\n").Replace('\r', '\n'));
        return _scenario;
    }

    /// <summary>Parses already decoded episode text (exposed for tests).</summary>
    public Scenario ParseText(string text, Scenario target = null)
    {
        _scenario = target ?? _scenario ?? new Scenario();
        var pos = 0;
        var first = ReadWord(text, ref pos);
        if (first != VersionTag)
        {
            Error($"Unknown episode file version: '{first}'");
            return _scenario;
        }

        var seen = new HashSet<string>();
        while (true)
        {
            var keyword = ReadWord(text, ref pos);
            if (keyword == null)
                break;
            var upper = keyword.ToUpperInvariant();

            if (Array.IndexOf(TextKeywords, upper) >= 0)
            {
                SetText(upper, ReadBlock(text, ref pos, upper));
                continue;
            }

            var line = ReadRestOfLine(text, ref pos);
            var args = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            CheckOrder(upper);
            seen.Add(upper);
            switch (upper)
            {
                case "FUNDS": _scenario.Funds = (int)ToLong(args, 0, "FUNDS"); break;
                case "MAP":
                    if (args.Length < 1) Error("MAP: missing file name");
                    else _scenario.MapFile = args[0];
                    break;
                case "RANDOM": _scenario.RandomBases = ToLong(args, 0, "RANDOM") == 1; break;
                case "PLAYER": ParsePlayer(line); break;
                case "VCOND": ParseVcond(args); break;
                case "UNIT": ParseUnit(line); break;
                case "ALLIANCE": ParseAlliance(args); break;
                case "BRIBEADVANCE": ParseBribeAdvance(args); break;
                default: Error($"Unknown keyword '{keyword}'"); break;
            }
        }

        if (!seen.Contains("FUNDS")) Error("Funds label missing.");
        if (!seen.Contains("MAP")) Error("Map label missing.");
        if (!seen.Contains("RANDOM")) Error("Random label missing.");
        return _scenario;
    }

    private void CheckOrder(string keyword)
    {
        var index = Array.IndexOf(KeywordOrder, keyword);
        if (index < 0)
            return;
        if (index < _lastKeywordIndex)
            Error($"{keyword} after {KeywordOrder[_lastKeywordIndex]}: the original reader expects the order {string.Join(", ", KeywordOrder)}");
        _lastKeywordIndex = Math.Max(_lastKeywordIndex, index);
    }

    /// <summary>PLAYER id kind hasBase name ("%d %d %d %[^\n]")</summary>
    private void ParsePlayer(string line)
    {
        if (!TryReadInts(line, 3, out var values, out var rest))
        {
            Error($"Syntax error in player desc: '{line}'");
            return;
        }
        _scenario.Players.Add(new Scenario.EpiPlayerData
        {
            Id = values[0],
            Kind = values[1],
            HasBase = values[2] != 0,
            Name = rest.Trim(),
        });
    }

    private void ParseVcond(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out var type) || !int.TryParse(args[1], out var player))
        {
            Error($"Syntax error in vcond: '{string.Join(' ', args)}'");
            return;
        }
        if (!VcondIntCount.TryGetValue(type, out var count))
        {
            Error($"Unknown VCOND type {type}");
            return;
        }

        var vc = new Scenario.EpiVictoryCondition { ConditionType = type, TargetPlayer = player };
        var index = 2;
        if (type == 5)
        {
            if (args.Length < 3 || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vc.Share))
            {
                Error($"Syntax error in vcond 5: '{string.Join(' ', args)}'");
                return;
            }
            index = 3;
        }
        for (var i = 0; i < count; i++, index++)
        {
            if (index >= args.Length || !long.TryParse(args[index], out var value))
            {
                Error($"Syntax error in vcond {type}: expected {count} values in '{string.Join(' ', args)}'");
                return;
            }
            vc.Values.Add(value);
        }
        _scenario.VictoryConditions.Add(vc);
    }

    /// <summary>UNIT type player x y flagA flagB [name] (" %d %d %d %d %d %d" then "%*[ ]%[^\n]")</summary>
    private void ParseUnit(string line)
    {
        if (!TryReadInts(line, 6, out var v, out var rest))
        {
            Error($"Syntax error in unit desc: '{line}'");
            return;
        }
        var unitType = UnitCommon.FromOriginalId(v[0]);
        if (unitType == null)
            Error($"UNIT: unknown unit type {v[0]}");
        _scenario.StartingUnits.Add(new Scenario.EpiStartingUnit
        {
            OriginalType = v[0],
            UnitType = unitType,
            PlayerId = v[1],
            Position = new Vector2I(v[2], v[3]),
            FlagA = v[4],
            FlagB = v[5],
            Name = rest.Trim(),
        });
    }

    private void ParseAlliance(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out var from) || !int.TryParse(args[1], out var to))
        {
            Error($"Syntax error in alliance desc: '{string.Join(' ', args)}'");
            return;
        }
        _scenario.Alliances.Add(new Scenario.EpiAlliance { FromPlayer = from, ToPlayer = to });
    }

    /// <summary>BRIBEADVANCE player authority points money (" %d %d %f %ld ")</summary>
    private void ParseBribeAdvance(string[] args)
    {
        if (args.Length < 4
            || !int.TryParse(args[0], out var player)
            || !int.TryParse(args[1], out var authority)
            || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var points)
            || !long.TryParse(args[3], out var money))
        {
            Error($"Syntax error in bribeadvance desc: '{string.Join(' ', args)}'");
            return;
        }
        _scenario.BribeAdvances.Add(new Scenario.EpiBribeAdvance
        {
            PlayerId = player, Authority = authority, Points = points, Money = money,
        });
    }

    private void SetText(string keyword, string text)
    {
        switch (keyword)
        {
            case "SCENARIONAME": _scenario.ScenarioName = text; break;
            case "STORYTEXT": _scenario.StoryText = text; break;
            case "TIPTEXT": _scenario.TipText = text; break;
            case "OBJECTIVETEXT": _scenario.ObjectiveText = text; break;
            case "SUCCESSTEXT": _scenario.SuccessText = text; break;
            case "FAILURETEXT": _scenario.FailureText = text; break;
        }
    }

    #region Lexing

    /// <summary>Next whitespace-separated word, or null at the end.</summary>
    private static string ReadWord(string text, ref int pos)
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            pos++;
        if (pos >= text.Length)
            return null;
        var start = pos;
        while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != '{')
            pos++;
        return text[start..pos];
    }

    private static string ReadRestOfLine(string text, ref int pos)
    {
        var end = text.IndexOf('\n', pos);
        if (end < 0)
            end = text.Length;
        var line = text[pos..end];
        pos = end;
        return line.Trim();
    }

    /// <summary>Text between { and the next }, with line breaks kept.</summary>
    private string ReadBlock(string text, ref int pos, string keyword)
    {
        var open = text.IndexOf('{', pos);
        if (open < 0)
        {
            Error($"{keyword}: missing '{{'");
            pos = text.Length;
            return "";
        }
        var close = text.IndexOf('}', open + 1);
        if (close < 0)
        {
            Error($"{keyword}: missing '}}'");
            close = text.Length;
        }
        var body = text[(open + 1)..close];
        pos = Math.Min(close + 1, text.Length);
        return body.Trim();
    }

    /// <summary>Reads <paramref name="count"/> integers from the start of a line; the remainder is returned in <paramref name="rest"/>.</summary>
    private static bool TryReadInts(string line, int count, out int[] values, out string rest)
    {
        values = new int[count];
        var pos = 0;
        for (var i = 0; i < count; i++)
        {
            var word = ReadWord(line, ref pos);
            if (word == null || !int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
            {
                rest = "";
                return false;
            }
        }
        rest = pos < line.Length ? line[pos..] : "";
        return true;
    }

    private long ToLong(string[] args, int index, string keyword)
    {
        if (index < args.Length && long.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v;
        Error($"{keyword}: expected a number");
        return 0;
    }

    #endregion

    private static int EpisodeNumberOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("ep", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[2..], out var n) ? n : 0;
    }

    private void Error(string message)
    {
        var text = $"{Path.GetFileName(_scenario.FilePath)}: {message}";
        _scenario.Errors.Add(text);
        GD.PrintErr("EpiParser: ", text);
    }
}
