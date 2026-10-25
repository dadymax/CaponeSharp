using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Capone;

/// <summary>
/// Loads a scenario from an .epi file. Supports FUNDS, MAP, RANDOM, PLAYER, VCOND, ALLIANCE, UNIT.
/// Not yet supported: CP1251 text, {…} text blocks, BRIBEADVANCE, numeric unit types of the original files.
/// </summary>
public class EpiParser
{
    private static readonly char[] Separators = { ' ', '\t' };

    private Scenario _scenario = new();

    public Scenario Parse(string filePath)
    {
        _scenario = new Scenario();

        using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr("EpiParser: failed to open file: ", filePath);
            return _scenario;
        }

        while (!file.EofReached())
        {
            var line = file.GetLine().Trim();
            if (line.Length > 0)
                ParseLine(line);
        }
        return _scenario;
    }

    private void ParseLine(string line)
    {
        var commentIdx = line.IndexOf('\'');
        if (commentIdx >= 0)
            line = line[..commentIdx].Trim();
        if (line.Length == 0)
            return;

        var parts = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        // Arguments after the keyword.
        var args = parts[1..];
        switch (parts[0].ToUpperInvariant())
        {
            case "FUNDS": ParseFunds(args); break;
            case "MAP": ParseMap(args); break;
            case "RANDOM": ParseRandom(args); break;
            case "PLAYER": ParsePlayer(args); break;
            case "VCOND": ParseVcond(args); break;
            case "ALLIANCE": ParseAlliance(args); break;
            case "UNIT": ParseUnit(args); break;
            // Unknown keywords are ignored.
        }
    }

    /// <summary>FUNDS amount</summary>
    private void ParseFunds(string[] args)
    {
        if (args.Length >= 1)
            _scenario.Funds = ToInt(args[0]);
    }

    /// <summary>MAP filename</summary>
    private void ParseMap(string[] args)
    {
        if (args.Length >= 1)
            _scenario.MapFile = args[0].ToUpperInvariant();
    }

    /// <summary>RANDOM 0|1</summary>
    private void ParseRandom(string[] args)
    {
        if (args.Length >= 1)
            _scenario.RandomBases = ToInt(args[0]) == 1;
    }

    /// <summary>PLAYER order unknown hasBase name with spaces</summary>
    private void ParsePlayer(string[] args)
    {
        if (args.Length < 4)
            return;
        _scenario.Players.Add(new Scenario.EpiPlayerData
        {
            Order = ToInt(args[0]),
            Unknown = ToInt(args[1]),
            HasBase = ToInt(args[2]) == 1,
            Name = JoinName(args, 3),
        });
    }

    /// <summary>VCOND type targetPlayer params…</summary>
    private void ParseVcond(string[] args)
    {
        if (args.Length < 2)
            return;
        _scenario.VictoryConditions.Add(new Scenario.EpiVictoryCondition
        {
            ConditionType = ToInt(args[0]),
            TargetPlayer = ToInt(args[1]),
            Parameters = new List<string>(args[2..]),
        });
    }

    /// <summary>ALLIANCE fromPlayer toPlayer</summary>
    private void ParseAlliance(string[] args)
    {
        if (args.Length < 2)
            return;
        _scenario.Alliances.Add(new Scenario.EpiAlliance
        {
            FromPlayer = ToInt(args[0]),
            ToPlayer = ToInt(args[1]),
        });
    }

    /// <summary>UNIT type playerID x y stayFlag [name with spaces]</summary>
    private void ParseUnit(string[] args)
    {
        if (args.Length < 5)
            return;
        if (!Enum.TryParse<UnitCommon.UnitType>(args[0], out var type) || int.TryParse(args[0], out _))
        {
            GD.PrintErr("EpiParser: unknown unit type: ", args[0]);
            type = UnitCommon.UnitType.Bat;
        }
        _scenario.StartingUnits.Add(new Scenario.EpiStartingUnit
        {
            UnitType = type,
            PlayerId = ToInt(args[1]),
            Position = new Vector2I(ToInt(args[2]), ToInt(args[3])),
            StayFlag = ToInt(args[4]) == 1,
            Name = args.Length >= 6 ? JoinName(args, 5) : "",
        });
    }

    private static string JoinName(string[] args, int from) =>
        string.Join(' ', args[from..]).Trim().Trim('"');

    private static int ToInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
