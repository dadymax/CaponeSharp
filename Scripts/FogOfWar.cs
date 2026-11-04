using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone;

/// <summary>
/// Fog of war for the local player (rules from the user, who played the original; sight rules from the EXE, see
/// <see cref="Vision"/>): the player sees what their own and their allies' units (current vision, unit vt+1ACh) and
/// buildings (4 tiles, company / HQ vt+0E8h; sub_4393C6 marks the tiles) see. Nothing stays explored: when the viewers
/// leave, the fog comes back and other players' units there are hidden. Companies out of sight keep showing their last
/// seen state (<see cref="Company.InFog"/>); companies never seen are hidden (<see cref="Company.Discovered"/>).
/// Unseen cells get the original fog tiles with soft edges (<see cref="FogTiles"/>).
/// A cell counts as seen until 61 game minutes after it was last in sight (map object vt+0D8h stores the minute,
/// vt+0DCh sub_438B80 tests now &lt; mark + 61), so the fog closes about a game hour behind a viewer that walks away.
/// The original marks once per unit think (hourly); here the marks are refreshed continuously.
/// </summary>
public partial class FogOfWar : Node
{
    public const int BuildingSight = 4;
    /// <summary>sub_438B80: a mark keeps the cell seen for this many game minutes (3Dh).</summary>
    public const int SeenMinutes = 61;
    private const double UpdateSeconds = 0.25;

    /// <summary>A setting: off shows everything (as before).</summary>
    public static bool Enabled { get; set; } = true;

    public static FogOfWar Instance { get; private set; }

    private readonly HashSet<Vector2I> _visible = new();
    private readonly HashSet<Vector2I> _inSight = new();
    private readonly Dictionary<Vector2I, long> _lastSeen = new();
    private readonly Dictionary<Company, (Vector2I Cell, bool[] Grid)> _buildingGrids = new();
    private TileMapLayer _map;
    private FogTiles _fogTiles;
    private double _timer;

    public override void _Ready()
    {
        Instance = this;
        _map = GetParent() as TileMapLayer;
        if (_map == null)
            return;
        _fogTiles = new FogTiles();
        _fogTiles.Init(_map);
        CallDeferred(MethodName.AttachFogTiles);
    }

    /// <summary>The fog layer is drawn right after the map, y-sorted with it (same parent).</summary>
    private void AttachFogTiles()
    {
        var parent = _map.GetParent();
        if (parent == null)
            return;
        parent.AddChild(_fogTiles);
        parent.MoveChild(_fogTiles, _map.GetIndex() + 1);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Is <paramref name="cell"/> seen by the local player (always true with the fog off)?</summary>
    public bool IsVisible(Vector2I cell) => !Enabled || _visible.Contains(cell);

    public int VisibleCount => _visible.Count;

    public string Describe() => $"visible cells {_visible.Count}";

    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0)
            return;
        _timer = UpdateSeconds;
        Refresh();
    }

    public void Refresh()
    {
        if (_map == null)
            return;
        var tree = GetTree();
        var player = Teams.GetCurrentPlayerTeam();
        var units = tree.GetNodesInGroup(Unit.GroupName).OfType<Unit>().Where(u => IsInstanceValid(u) && !u.IsDead).ToList();
        var companies = tree.GetNodesInGroup(Company.GroupName).OfType<Company>().Where(IsInstanceValid).ToList();
        var enabled = Enabled && player != null;

        _inSight.Clear();
        if (enabled)
        {
            foreach (var unit in units.Where(u => IsFriendly(player, u.OwnerTeam)))
            {
                var grid = unit.SightGrid(out var center, out var r);
                Add(grid, center, r);
            }
            foreach (var company in companies.Where(c => IsFriendly(player, c.OwnerTeam)))
            {
                var cell = _map.LocalToMap(company.Position);
                if (!_buildingGrids.TryGetValue(company, out var cached) || cached.Cell != cell)
                    _buildingGrids[company] = cached = (cell, Vision.Build(cell, BuildingSight));
                Add(cached.Grid, cell, BuildingSight);
            }
        }
        // vt+0D8h marks, vt+0DCh: seen while now < mark + 61.
        var now = GameClock.Instance?.Minute ?? 0;
        foreach (var cell in _inSight)
            _lastSeen[cell] = now;
        _visible.Clear();
        if (enabled)
        {
            var stale = new List<Vector2I>();
            foreach (var (cell, minute) in _lastSeen)
                if (now < minute + SeenMinutes)
                    _visible.Add(cell);
                else
                    stale.Add(cell);
            foreach (var cell in stale)
                _lastSeen.Remove(cell);
        }
        else
            _lastSeen.Clear();

        foreach (var unit in units)
            unit.Visible = !unit.IsArrested && !unit.InsideBuilding
                           && (!enabled || IsFriendly(player, unit.OwnerTeam) || _visible.Contains(_map.LocalToMap(unit.Position)));
        foreach (var company in companies)
        {
            company.InFog = enabled && !_visible.Contains(_map.LocalToMap(company.Position));
            if (!company.InFog)
                company.Discovered = true;
            // Never seen yet: hidden (its building stays part of the darkened map).
            company.Visible = !enabled || company.Discovered;
        }

        _fogTiles?.Refresh(_visible, enabled);
    }

    public static bool IsFriendly(Team player, Team owner) =>
        owner != null && player.GetTargetRelation(owner.TeamColor) is Teams.TeamRelation.Self or Teams.TeamRelation.Friend;

    private void Add(bool[] grid, Vector2I center, int r)
    {
        var size = 2 * r + 1;
        for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
                if (grid[(dy + r) * size + dx + r])
                    _inSight.Add(center + new Vector2I(dx, dy));
    }
}
