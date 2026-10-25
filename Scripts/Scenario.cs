using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>One game loaded from an original .epi episode file. The episodes in order form the campaign.</summary>
public class Scenario
{
    public string FilePath { get; set; } = "";
    /// <summary>N of epN.epi, or 0.</summary>
    public int EpisodeNumber { get; set; }

    public string ScenarioName { get; set; } = "";
    public string StoryText { get; set; } = "";
    public string TipText { get; set; } = "";
    public string ObjectiveText { get; set; } = "";
    public string SuccessText { get; set; } = "";
    public string FailureText { get; set; } = "";

    public int Funds { get; set; }
    /// <summary>Map file name as written in the episode (looked up case-insensitively in Maps/).</summary>
    public string MapFile { get; set; } = "";
    /// <summary>RANDOM 1: bases go to random start points; 0: in PLAYER order.</summary>
    public bool RandomBases { get; set; }

    public List<EpiPlayerData> Players { get; } = new();
    public List<EpiVictoryCondition> VictoryConditions { get; } = new();
    public List<EpiAlliance> Alliances { get; } = new();
    public List<EpiStartingUnit> StartingUnits { get; } = new();
    public List<EpiBribeAdvance> BribeAdvances { get; } = new();

    /// <summary>Problems found while parsing (the original game would refuse some of these files).</summary>
    public List<string> Errors { get; } = new();

    public bool IsValid => Errors.Count == 0;

    /// <summary>PLAYER id kind hasBase name</summary>
    public class EpiPlayerData
    {
        /// <summary>Player number used by VCOND/UNIT/ALLIANCE/BRIBEADVANCE (1-based in the original episodes).</summary>
        public int Id;
        /// <summary>0 = human ("User"); 2, 3, 4, 6 = computer players of different kinds.</summary>
        public int Kind;
        public bool HasBase;
        public string Name = "";

        public bool IsHuman => Kind == 0;
    }

    /// <summary>VCOND type player values…, typed per condition (see VictoryCondition).</summary>
    public class EpiVictoryCondition
    {
        /// <summary>VCOND type number as written in the .epi file (1–9).</summary>
        public int ConditionType;
        public int TargetPlayer;
        /// <summary>Integer values after the player, in file order.</summary>
        public List<long> Values = new();
        /// <summary>The float value of type 5 (market share, e.g. 0.10).</summary>
        public float Share;
    }

    /// <summary>ALLIANCE a b: player a doesn't fight player b (one-way).</summary>
    public class EpiAlliance
    {
        public int FromPlayer;
        public int ToPlayer;
    }

    /// <summary>UNIT type player x y flagA flagB [name]</summary>
    public class EpiStartingUnit
    {
        /// <summary>Original type number (index into the EXE unit table).</summary>
        public int OriginalType;
        /// <summary>Null if the number is not a known type.</summary>
        public UnitCommon.UnitType? UnitType;
        public int PlayerId;
        /// <summary>Original map coordinates.</summary>
        public Vector2I Position;
        /// <summary>5th number. When 0 the EXE calls the unit's vtable+0x174 setter with 0 (clears the value copied from the price).</summary>
        public int FlagA;
        /// <summary>6th number, passed to vtable+0x1EC. 1 is used for stationary guards.</summary>
        public int FlagB;
        public string Name = "";
    }

    /// <summary>BRIBEADVANCE player authority points money: starting favour with an authority.</summary>
    public class EpiBribeAdvance
    {
        public int PlayerId;
        /// <summary>1–4. Order in the EXE spending window: police, army, politician, FBI (not verified).</summary>
        public int Authority;
        public float Points;
        public long Money;
    }
}
