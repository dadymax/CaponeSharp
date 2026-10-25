using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>One game loaded from an .epi file. Several scenarios form a campaign.</summary>
public class Scenario
{
    public int ScenarioId { get; set; }
    public string ScenarioName { get; set; } = "";

    public int Funds { get; set; }
    public string MapFile { get; set; } = "";
    public bool RandomBases { get; set; } = true;

    public List<EpiPlayerData> Players { get; } = new();
    public List<EpiVictoryCondition> VictoryConditions { get; } = new();
    public List<EpiAlliance> Alliances { get; } = new();
    public List<EpiStartingUnit> StartingUnits { get; } = new();

    public class EpiPlayerData
    {
        public int Order;
        public int Unknown;
        public bool HasBase;
        public string Name = "";
    }

    public class EpiVictoryCondition
    {
        /// <summary>VCOND type number as written in the .epi file (1-8).</summary>
        public int ConditionType;
        public int TargetPlayer;
        public List<string> Parameters = new();
    }

    public class EpiAlliance
    {
        public int FromPlayer;
        public int ToPlayer;
    }

    public class EpiStartingUnit
    {
        public UnitCommon.UnitType UnitType;
        public int PlayerId;
        public Vector2I Position;
        public bool StayFlag;
        public string Name = "";
    }
}
