using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// FBI informers (Organisation vt+D4h sub_431542): FBI services 7–0Fh cost no favour points; once the money bribed to
/// the FBI reaches a service's price, its report comes again and again — service 7 every 28 days, the others every
/// week (intervals word_4887D4, timers at the HQ +1B0h; a never-used timer reports at once). Each report plays
/// FBIInforms.wav:
/// <list type="bullet">
/// <item>7 — where a business of a random other, hostile player is (20,9, with its position);</item>
/// <item>8 / 9 / 10 — every other player's funds, income, gangsters (20,10 / 20,11 / 20,12);</item>
/// <item>11 — every other player's market share of a random business type (20,13; type names 20,4–7);</item>
/// <item>13 — where a gangster of a random other, hostile player is (20,14, with its position);</item>
/// <item>15 — where every enemy HQ is (20,16, with its position).</item>
/// </list>
/// Only the local player reads them, so only the local player's HQ runs this.
/// </summary>
public partial class Organisation
{
    /// <summary>unk_4887B4 / word_4887D4: the informer services and their intervals in days.</summary>
    public static readonly (int Item, int Days)[] Informers =
    {
        (0x07, 28), (0x08, 7), (0x09, 7), (0x0A, 7), (0x0B, 7), (0x0C, 7), (0x0D, 7), (0x0F, 7),
    };

    private readonly Dictionary<int, long> _informerLast = new();

    /// <summary>Hourly: the reports that are due.</summary>
    public void InformerTick()
    {
        if (!Team.IsCurrentPlayerColor || Eliminated)
            return;
        var now = ClockNow;
        for (var k = Informers.Length - 1; k >= 0; k--)
        {
            var (item, days) = Informers[k];
            if (!CanUseItem(item))
                continue;
            if (_informerLast.TryGetValue(item, out var last) && now < last + days * (long)GameClock.MinutesPerDay)
                continue;
            _informerLast[item] = now;
            Report(item);
        }
    }

    private List<Team> Rivals() =>
        Teams.TeamsInGame.Values.Where(t => t != Team && t.Org is { Eliminated: false }).ToList();

    private bool Hostile(Team other) => Team.GetTargetRelation(other.TeamColor) == Teams.TeamRelation.Enemy;

    private void Report(int item)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        var texts = OriginalTexts.Instance;
        var rivals = Rivals();
        switch (item)
        {
            case 0x07:
            {
                var companies = tree.GetNodesInGroup(Company.GroupName).OfType<Company>()
                    .Where(c => c.IsEconomic && c.BusinessType != OriginalBusinessTable.BusinessType.Legal
                                && c.OwnerTeam != null && rivals.Contains(c.OwnerTeam) && Hostile(c.OwnerTeam)).ToList();
                if (companies.Count == 0)
                    return;
                var c = companies[GD.RandRange(0, companies.Count - 1)];
                Sound.Play("FBIInforms.wav");
                Messages.Show(PanelInfo.CFormat(texts.Get(20, 9), c.OwnerTeam.PlayerName), Messages.Normal, c.Cell);
                break;
            }
            case 0x08 or 0x09 or 0x0A or 0x0B:
            {
                if (rivals.Count == 0)
                    return;
                Sound.Play("FBIInforms.wav");
                var type = GD.RandRange(1, 4); // sub_431542 case 11: random(4) + 1
                foreach (var t in rivals)
                {
                    var text = item switch
                    {
                        0x08 => PanelInfo.CFormat(texts.Get(20, 10), t.PlayerName, PanelInfo.Money(t.Org.Cash)),
                        0x09 => PanelInfo.CFormat(texts.Get(20, 11), t.PlayerName, PanelInfo.Money(t.Org.NetIncome)),
                        0x0A => PanelInfo.CFormat(texts.Get(20, 12), t.PlayerName, Gangsters(tree, t)),
                        _ => PanelInfo.CFormat(texts.Get(20, 13), texts.Get(20, 3 + type), t.PlayerName, t.Org.MarketShare[type] * 100f),
                    };
                    Messages.Show(text);
                }
                break;
            }
            case 0x0D:
            {
                var gangsters = tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
                    .Where(u => !u.IsDead && !u.InsideBuilding && u.Vehicle == null && u.UnitType != UnitCommon.UnitType.Collector
                                && u.OwnerTeam != null && rivals.Contains(u.OwnerTeam) && Hostile(u.OwnerTeam)).ToList();
                if (gangsters.Count == 0)
                    return;
                var g = gangsters[GD.RandRange(0, gangsters.Count - 1)];
                Sound.Play("FBIInforms.wav");
                Messages.Show(PanelInfo.CFormat(texts.Get(20, 14), g.OwnerTeam.PlayerName), Messages.Normal, g.Cell);
                break;
            }
            case 0x0F:
                foreach (var t in rivals.Where(t => t.HqCompany != null && Hostile(t)))
                {
                    Sound.Play("FBIInforms.wav");
                    Messages.Show(texts.Get(20, 16), Messages.Normal, t.HqCompany.Cell);
                }
                break;
        }
    }

    private static int Gangsters(SceneTree tree, Team team) =>
        tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>()
            .Count(u => !u.IsDead && u.OwnerTeam == team && u.UnitType != UnitCommon.UnitType.Collector);
}
