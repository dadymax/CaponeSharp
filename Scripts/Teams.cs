using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Teams are identified by colour; each player's team has an <see cref="Organisation"/> with its finances.</summary>
public static class Teams
{
    public enum TeamRelation { Self, Friend, Enemy, Neutral }

    /// <summary>Same as GDScript's default Color(): opaque black.</summary>
    public static readonly Color NeutralTeamColor = Colors.Black;

    /// <summary>Stand-in for neutral objects (police, units that resigned); never part of <see cref="TeamsInGame"/>.</summary>
    public static readonly Team NeutralTeam = new(NeutralTeamColor);

    public static Dictionary<Color, Team> TeamsInGame { get; } = new();

    /// <summary>The player team of this colour, or null (neutral colour or unknown).</summary>
    public static Team GetTeam(Color teamColor) =>
        teamColor != NeutralTeamColor && TeamsInGame.TryGetValue(teamColor, out var team) ? team : null;

    /// <summary>The team of this colour, created if needed. The neutral colour gives <see cref="NeutralTeam"/>.</summary>
    public static Team GetOrAddTeam(Color teamColor)
    {
        if (teamColor == NeutralTeamColor)
            return NeutralTeam;
        if (!TeamsInGame.TryGetValue(teamColor, out var team))
        {
            team = new Team(teamColor);
            TeamsInGame[teamColor] = team;
        }
        return team;
    }

    public static Team GetCurrentPlayerTeam()
    {
        foreach (var t in TeamsInGame.Values)
            if (t.IsCurrentPlayerColor)
                return t;
        return null;
    }

    public static Team GetTeamByPlayerId(int playerId)
    {
        foreach (var t in TeamsInGame.Values)
            if (t.PlayerId == playerId)
                return t;
        return null;
    }
}

public class Team
{
    public Color TeamColor { get; }
    public bool IsCurrentPlayerColor { get; set; }
    public Dictionary<Color, Teams.TeamRelation> TeamRelation { get; } = new();
    public Vector2I HqCoords { get; set; }
    /// <summary>The player's headquarters (original class 9 object): collectors start and deliver here.</summary>
    public Company HqCompany { get; set; }
    /// <summary>Finances (original HQ object, class 6).</summary>
    public Organisation Org { get; set; }
    /// <summary>Computer opponent (AI HQ, sub_461510) for a PLAYER of kind ≠ 0; null for the human.</summary>
    public ComputerPlayer Ai { get; set; }

    // Player data from the scenario
    public int PlayerId { get; set; } = -1;
    /// <summary>PLAYER kind: 0 = human, other values = computer player types.</summary>
    public int Kind { get; set; }
    public string PlayerName { get; set; } = "";
    public bool HasBase { get; set; }

    public Team(Color teamColor) => TeamColor = teamColor;

    public Teams.TeamRelation GetTargetRelation(Color targetColor)
    {
        if (targetColor == TeamColor)
            return Teams.TeamRelation.Self;
        if (targetColor == Teams.NeutralTeamColor || TeamColor == Teams.NeutralTeamColor)
            return Teams.TeamRelation.Neutral;
        return TeamRelation.TryGetValue(targetColor, out var relation) ? relation : Teams.TeamRelation.Enemy;
    }

    public void SetTargetRelation(Color targetColor, Teams.TeamRelation relation) => TeamRelation[targetColor] = relation;

    public override string ToString() => $"{PlayerName} (player {PlayerId})";
}
