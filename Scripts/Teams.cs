using System.Collections.Generic;
using Godot;

namespace Capone;

/// <summary>Teams are identified by colour. Also holds each player's money.</summary>
public static class Teams
{
    public enum TeamRelation { Self, Friend, Enemy, Neutral }

    /// <summary>Same as GDScript's default Color(): opaque black.</summary>
    public static readonly Color NeutralTeamColor = Colors.Black;

    public static Dictionary<Color, Team> TeamsInGame { get; } = new();
    /// <summary>playerID → money.</summary>
    public static Dictionary<int, int> PlayerMoney { get; } = new();

    public static Team GetOrAddTeam(Color teamColor)
    {
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

    public static void AddPlayerMoney(int playerId, int amount) =>
        PlayerMoney[playerId] = GetPlayerMoney(playerId) + amount;

    public static bool RemovePlayerMoney(int playerId, int amount)
    {
        if (PlayerMoney.TryGetValue(playerId, out var money) && money >= amount)
        {
            PlayerMoney[playerId] = money - amount;
            return true;
        }
        return false;
    }

    public static int GetPlayerMoney(int playerId) => PlayerMoney.GetValueOrDefault(playerId, 0);

    public static void SetPlayerMoney(int playerId, int amount) => PlayerMoney[playerId] = amount;
}

public class Team
{
    public Color TeamColor { get; }
    public bool IsCurrentPlayerColor { get; set; }
    public Dictionary<Color, Teams.TeamRelation> TeamRelation { get; } = new();
    public Vector2I HqCoords { get; set; }

    private Company _hqCompany;
    public Company HqCompany
    {
        get => _hqCompany;
        set
        {
            _hqCompany = value;
            if (value != null)
                value.TeamColor = TeamColor;
        }
    }

    // Player data from the scenario
    public int PlayerId { get; set; } = -1;
    public string PlayerName { get; set; } = "";
    public int PlayerMoney { get; set; }
    public bool HasBase { get; set; }

    public Team(Color teamColor) => TeamColor = teamColor;

    public Teams.TeamRelation GetTargetRelation(Color targetColor)
    {
        if (targetColor == TeamColor)
            return Teams.TeamRelation.Self;
        return TeamRelation.TryGetValue(targetColor, out var relation) ? relation : Teams.TeamRelation.Enemy;
    }

    public void SetTargetRelation(Color targetColor, Teams.TeamRelation relation) => TeamRelation[targetColor] = relation;
}
